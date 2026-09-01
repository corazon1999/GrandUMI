using System.Reflection;
using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects.Rules;
using GrandUMI.Effects.Scripted;
using GrandUMI.Game;
using GrandUMI.Game.Hex;
using GrandUMI.Game.Snapshot;

namespace GrandUMI.Effects;

/// <summary>
/// 效果触发与解决的入口。
///
/// 卡牌效果的实现来源：
///   1. ScriptedEffectRegistry: 手写 C# 类（复杂卡 / 领航）
///   2. DslInterpreter:         OP15.json 中的声明式 DSL（约 80 张常规卡）
///
/// 触发顺序：回合玩家优先 → 非回合玩家 → 重复直到无新触发
/// </summary>
public static class EffectRuntime
{
    /// <summary>
    /// 在指定触发时机，对所有可响应的效果按规则顺序解析。
    /// 调用方应在状态变更后（出牌/攻击/KO/抽牌等）调用此方法。
    /// </summary>
    public static async Task TriggerEvent(GameState s, EffectTrigger trigger, IPromptService prompts, Dictionary<string, object?>? payload = null)
    {
        await HexRules.OnGameEventAsync(s, trigger, prompts, payload);
        if (s.IsGameOver) return;
        var candidates = CollectListeners(s, trigger, payload);
        var effects = candidates
            .Select(candidate => new TriggeredCandidate(candidate.OwnerIdx, candidate.Source, trigger, payload))
            .ToList();
        await ResolveTriggeredCandidatesInOrder(s, prompts, effects);
    }

    // ── Wave2 反应式 watcher 基础设施 ──
    // 用 AsyncLocal 而非 [ThreadStatic]：效果解析大量 async，续延会在线程池任意线程恢复，
    // ThreadStatic 跨 await 后会读到该线程上别的引擎留下的值（并发房间互相污染 / 重放发散）。
    // AsyncLocal 随 async 流向下游传播，且被调用方的改动不回流至调用方——这恰好实现：
    // _ambient 在深层 op 里读到正确 state；_depth 的嵌套判定（最外层 ==0 才排空）自然成立。
    private static readonly AsyncLocal<GameState?> _ambientAL = new();
    private static readonly AsyncLocal<int> _depthAL = new();
    private static readonly AsyncLocal<bool> _drainingAL = new();
    private static readonly AsyncLocal<CardInstance?> _currentSourceAL = new();
    private static readonly AsyncLocal<int?> _actingSideAL = new();
    private static readonly AsyncLocal<IPromptService?> _promptsAL = new();
    private static readonly AsyncLocal<EffectLeaveReplacementProcess?> _leaveReplacementProcessAL = new();
    private static GameState? _ambient { get => _ambientAL.Value; set => _ambientAL.Value = value; }
    private static int _depth { get => _depthAL.Value; set => _depthAL.Value = value; }
    private static bool _draining { get => _drainingAL.Value; set => _drainingAL.Value = value; }

    /// <summary>当前正在解析的效果源卡（无效果上下文时为 null）。Prompt 系统据此告知客户端"在结算哪张卡的效果"。
    /// 与 _ambient 同走 AsyncLocal：随 async 流向 callee 传播、不回流 caller，天然支持嵌套效果与并发房间隔离。</summary>
    public static CardInstance? CurrentSource => _currentSourceAL.Value;

    /// <summary>当前正在解析的效果的控制方下标（= Resolve 的 ownerIdx）；无效果上下文时为 -1。
    /// 用于"因对方效果离场"判定：若某卡因效果离场而 CurrentActingSide 为其对手，则属"对方效果"。</summary>
    public static int CurrentActingSide => _actingSideAL.Value ?? -1;

    /// <summary>当前正在解析的效果所属对局（无效果上下文时为 null）。
    /// 供 AtomicOps 在状态变更时查询持续型限制（如持续来源的 CannotBeRested）。</summary>
    public static GameState? CurrentState => _ambientAL.Value;

    /// <summary>当前效果解析可用的 Prompt 服务（无效果上下文时为 null）。
    /// 供 AtomicOps 内需要临时交互的场景（如 PlayFromHandFree 满场自选弃谁）使用，
    /// 免去给 136 处同步调用点改签名。</summary>
    public static IPromptService? CurrentPrompts => _promptsAL.Value;

    /// <summary>
    /// 一张卡牌效果的一次完整结算中已经支付的离场置换。旧卡效可能把多目标离场拆成多个步骤，
    /// 因此不能只依赖“同时离场”目标集合；置换触发自身会继承外层效果的处理过程。
    /// </summary>
    private sealed class EffectLeaveReplacementProcess(GameState state)
    {
        public GameState State { get; } = state;
        public List<(int Owner, Func<CardInstance, bool> Matches)> Grants { get; } = new();
    }

    internal static void RegisterEffectLeaveReplacement(
        GameState state, int owner, Func<CardInstance, bool> matches)
    {
        var process = _leaveReplacementProcessAL.Value;
        if (process is null || !ReferenceEquals(process.State, state)) return;
        process.Grants.Add((owner, matches));
    }

    internal static bool IsEffectLeaveReplacementCovered(
        GameState state, int owner, CardInstance card)
    {
        var process = _leaveReplacementProcessAL.Value;
        return process is not null
            && ReferenceEquals(process.State, state)
            && process.Grants.Any(grant => grant.Owner == owner && grant.Matches(card));
    }

    /// <summary>由 AtomicOps 在状态变更时调用，把 watcher 事件入队到当前效果所属的 state（无效果上下文时忽略）。
    /// 角色因效果离场时附带效果控制方，供“因对方的效果离场”类监听准确判定。</summary>
    public static void NotifyWatcher(EffectTrigger trigger, Dictionary<string, object?>? payload = null)
    {
        var state = _ambient;
        if (state is null) return;

        if (trigger is EffectTrigger.OnCharLeaveField or EffectTrigger.OnAnyCharKOd or EffectTrigger.OnCharRested)
        {
            payload = payload is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(payload);
            payload.TryAdd("actingSide", CurrentActingSide);
            payload.TryAdd("sourceNumber", CurrentSource?.Info.Number);
        }

        state.EnqueueWatcher(trigger, payload);
    }

    /// <summary>是否正在支付效果成本（DSL PayActivationCost 内为 true）。
    /// 供 OnHandDiscarded 消费端按各自规则区分成本与收益阶段；OP12-040 两者均会触发。</summary>
    private static readonly AsyncLocal<bool> _payingCostAL = new();
    public static bool PayingCost { get => _payingCostAL.Value; set => _payingCostAL.Value = value; }

    /// <summary>手牌因效果被丢弃时入队 OnHandDiscarded（owner=该手牌所属方）；仅效果上下文内有效。
    /// payload 额外携带丢弃来源：sourceNumber=当前结算效果的来源卡番号、actingSide=效果控制方、
    /// isCost=是否成本支付，供只关心收益阶段或只关心成本阶段的监听按各自规则判定。</summary>
    public static void NotifyHandDiscarded(PlayerState p)
    {
        var s = _ambient;
        if (s is null) return;
        int owner = ReferenceEquals(s.Players[0], p) ? 0 : ReferenceEquals(s.Players[1], p) ? 1 : -1;
        if (owner < 0) return;
        // ST33-004 的“因效果而被丢弃”包含效果文本冒号前的发动成本；
        // PayingCost 仍通过 watcher payload 下发，供其他监听按各自规则区分。
        p.HandDiscardedByEffectThisTurn = true;
        s.EnqueueWatcher(EffectTrigger.OnHandDiscarded, new Dictionary<string, object?>
        {
            ["owner"] = owner,
            ["sourceNumber"] = CurrentSource?.Info.Number,
            ["actingSide"] = CurrentActingSide,
            ["isCost"] = PayingCost,
        });
    }

    /// <summary>对单个卡牌的指定触发时机解析效果</summary>
    public static async Task Resolve(
        GameState s,
        int ownerIdx,
        CardInstance source,
        EffectTrigger trigger,
        IPromptService prompts,
        Dictionary<string, object?>? payload = null,
        bool hexCopy = false)
    {
        var owner = s.Players[ownerIdx];
        int turnOnceCountBefore = owner.TurnOnceUsed.Count;
        var turnOnceKeysBefore = owner.TurnOnceUsed.ToHashSet(StringComparer.Ordinal);
        var cardOnceKeysBefore = source.OncePerTurnUsedKeys.ToHashSet(StringComparer.Ordinal);
        // 许多旧脚本直接操作 LifeArea，容易漏派发“生命牌离场”监听。
        // 仅最外层效果记录前后生命区的卡实例，统一补齐实际离开的生命牌事件；
        // 嵌套效果共用这一轮快照，避免同一张生命牌被重复通知。
        bool isRootResolve = _depth == 0;
        HashSet<Guid>? lifeBefore0 = isRootResolve
            ? s.Players[0].LifeArea.Select(c => c.Id).ToHashSet()
            : null;
        HashSet<Guid>? lifeBefore1 = isRootResolve
            ? s.Players[1].LifeArea.Select(c => c.Id).ToHashSet()
            : null;
        // EB04-016：限制建立后的后续角色效果不得把既有休息咚转为活跃。
        // 只记录本次解析前已存在的咚，避免误伤“从咚卡组追加活跃咚”。
        bool blockCharacterDonActivation = source.Info.Kind == CardKind.Character
            && s.NoActivateDonByCharacterEffectThisTurn.Contains(ownerIdx);
        Dictionary<Guid, DonState>? donBefore = blockCharacterDonActivation
            ? s.Players[ownerIdx].CostArea.ToDictionary(d => d.Id, d => d.State)
            : null;
        var prevAmbient = _ambient;
        var prevSource = _currentSourceAL.Value;
        var prevActing = _actingSideAL.Value;
        var prevPrompts = _promptsAL.Value;
        var prevLeaveReplacementProcess = _leaveReplacementProcessAL.Value;
        bool inheritLeaveReplacementProcess = prevLeaveReplacementProcess is not null
            && ReferenceEquals(prevLeaveReplacementProcess.State, s)
            && trigger is EffectTrigger.PreKO
                or EffectTrigger.OnAllyWillBeKOd
                or EffectTrigger.OnAllyWillLeaveField;
        if (!inheritLeaveReplacementProcess)
            _leaveReplacementProcessAL.Value = new EffectLeaveReplacementProcess(s);
        _ambient = s;
        _currentSourceAL.Value = source;
        _actingSideAL.Value = ownerIdx;
        _promptsAL.Value = prompts;
        _depth++;
        try
        {
            var activationProbe = HexRules.CanCopyEffect(s, ownerIdx, trigger, hexCopy)
                ? new TriggerActivationProbe(prompts)
                : null;
            var ctx = new EffectContext
            {
                State = s,
                OwnerIndex = ownerIdx,
                Source = source,
                Trigger = trigger,
                Prompts = activationProbe?.Prompts ?? prompts,
                Engine = (prompts as PromptSystem)?.Engine,
            };
            if (payload is not null)
                foreach (var (k, v) in payload) ctx.Vars[k] = v;

            // 静态场上效果并不属于【登场时】效果。即使 OP09-081 等效果令【登场时】无效，
            // 角色自身的持续费用、力量与守护能力仍须完成注册，待整卡无效结束后也能自动恢复。
            var scripted = CardRulesetManager.For(s).TryGetScriptedEffect(source.Info.Number);
            if (trigger == EffectTrigger.OnEnterField && scripted is IFieldStaticEffect fieldStatic)
                await fieldStatic.RegisterFieldStatic(ctx);

            // “发动过事件”是发动历史，而不是事件效果是否成功解决。
            // 统一在效果运行时记录，确保从手牌直接发动、反击发动和被其它效果免费发动都能被统计。
            if (source.Info.Kind == CardKind.Event
                && source.Info.Cost >= 3
                && (trigger == EffectTrigger.EventMain || trigger == EffectTrigger.EventCounter))
            {
                s.Players[ownerIdx].HasActivatedBaseCost3PlusEventThisTurn = true;
            }

            // 持续"效果无效"：被持续无效化的卡（整卡或该类已印刷触发），其效果不发动。
            // 内部借 OnEnterField 初始化静态能力、但卡面并无【登场时】的卡不能被选择性无效化拦截。
            if (source.IsEffectsNullified || s.IsContinuouslyNullified(source)
                || (HasEffectForTrigger(source, trigger) && s.IsTriggerNullified(source, trigger))) return;

            // 一些监听效果的卡面时机虽然匹配，但当前事件归属、回合或成本条件并不成立。
            // 在效果排序和发动表现之前做同一份权威门禁，避免先向玩家显示“效果发动”，
            // 随后脚本再静默 return；直接 Resolve 的测试/回放入口也必须遵守相同约束。
            if (scripted is ITriggeredEffectAvailability triggerAvailability
                && !triggerAvailability.IsTriggerAvailable(s, ownerIdx, source, trigger, payload)) return;

            // H16/H17 等“再次发动”只绑定到实际进入解决的效果。脚本中的条件失败、成本失败、
            // 空候选静默返回均不得抢占本回合第一次复制机会。
            activationProbe?.Begin(s);

            // 出牌流程会统一调用 OnEnterField，即使卡牌没有该时点效果；只为确实声明了
            // 当前触发时机的卡记录表现，避免无效果卡登场时误播“效果发动”。
            // 提示型快照会立即带走事件，无交互效果则随本批次最终快照发送。
            if (HasEffectForTrigger(source, trigger))
                ctx.Engine?.QueueEffectActivation(ownerIdx, source, trigger);

            // 1. 优先用手写脚本
            if (scripted is not null && scripted.HandlesTrigger(trigger))
            {
                await scripted.Resolve(ctx);
                bool actuallyActivated = activationProbe?.WasActivated(s, ctx) ?? true;
                HexRules.ApplyInventorSecondUse(s, ownerIdx, source, turnOnceKeysBefore, cardOnceKeysBefore);
                MarkOncePerTurnCardUsedIfConsumed(s, owner, source, turnOnceCountBefore);
                await ResolveHexCopyAsync(
                    s, ownerIdx, source, trigger, prompts, payload, hexCopy, actuallyActivated);
                return;
            }

            // 2. 已确认省略项的前置补齐层：支付旧 DSL 无法表达的成本、注册持续效果，
            // 或在旧定义不精确时完整接管该触发。返回 false 表示本次已处理/取消，不再执行 DSL。
            if (!await DeclaredOmissionEffects.BeforeDsl(ctx))
            {
                bool actuallyActivated = activationProbe?.WasActivated(s, ctx) ?? true;
                await ResolveHexCopyAsync(
                    s, ownerIdx, source, trigger, prompts, payload, hexCopy, actuallyActivated);
                return;
            }

            // 3. 退回 DSL
            bool dslActivated = await Dsl.DslInterpreter.TryResolve(ctx);

            // 4. 后置补齐层：依赖 DSL 刚选中的同一目标，补结算条件加成、关键字或后续动作。
            await DeclaredOmissionEffects.AfterDsl(ctx);
            bool dslActuallyActivated = dslActivated || (activationProbe?.WasActivated(s, ctx) ?? false);
            HexRules.ApplyInventorSecondUse(s, ownerIdx, source, turnOnceKeysBefore, cardOnceKeysBefore);
            MarkOncePerTurnCardUsedIfConsumed(s, owner, source, turnOnceCountBefore);
            await ResolveHexCopyAsync(
                s, ownerIdx, source, trigger, prompts, payload, hexCopy, dslActuallyActivated);
        }
        catch (OptionalEffectDeclinedException)
        {
            // 玩家在尚未支付成本时返回上一级并改选“不发动”，属于正常交互，不应记为效果异常。
            return;
        }
        finally
        {
            (prompts as PromptSystem)?.ClearOptionalConfirmation(ownerIdx, source.Id);
            if (donBefore is not null)
            {
                foreach (var don in s.Players[ownerIdx].CostArea)
                    if (donBefore.TryGetValue(don.Id, out var before)
                        && before == DonState.Rest && don.State == DonState.Active)
                        don.State = DonState.Rest;
            }
            _depth--;
            if (isRootResolve)
                await HandleLifeZoneChangesAsync(s, prompts, lifeBefore0!, lifeBefore1!);
            _ambient = prevAmbient;
            _currentSourceAL.Value = prevSource;
            _actingSideAL.Value = prevActing;
            _promptsAL.Value = prevPrompts;
            _leaveReplacementProcessAL.Value = prevLeaveReplacementProcess;
            if (isRootResolve)
                s.EvaluateDeckOut();
            // 最外层效果结束后，排空期间积累的【KO时】、反应式 watcher 与被效果登场卡的【登场时】。
            if (_depth == 0 && !_draining
                && (s.PendingKOEffects.Count > 0 || s.PendingWatchers.Count > 0 || s.PendingEnterFields.Count > 0))
                await DrainPendingEnterFields(s, prompts);
        }
    }

    private static void MarkOncePerTurnCardUsedIfConsumed(GameState s, PlayerState owner, CardInstance source, int turnOnceCountBefore)
    {
        if (owner.TurnOnceUsed.Count > turnOnceCountBefore && OncePerTurnEffectCatalog.Contains(source.Info.Number, s))
            owner.OncePerTurnEffectUsedCardIds.Add(source.Id);
    }

    private static async Task ResolveHexCopyAsync(
        GameState state,
        int owner,
        CardInstance source,
        EffectTrigger trigger,
        IPromptService prompts,
        Dictionary<string, object?>? payload,
        bool alreadyCopied,
        bool actuallyActivated)
    {
        if (!actuallyActivated
            || !HasEffectForTrigger(source, trigger)
            || !HexRules.ShouldCopyEffect(state, owner, trigger, alreadyCopied))
            return;
        var copyPayload = payload is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(payload);
        copyPayload["hexCopied"] = true;
        await Resolve(state, owner, source, trigger, prompts, copyPayload, hexCopy: true);
    }

    /// <summary>
    /// 手写脚本仍沿用 Task 接口，无法直接返回“是否真正发动”。仅在玩家拥有复制类海克斯时，
    /// 以权威状态变化、有效交互或公开动作作为实际发动证据，避免给所有历史脚本增加侵入式标记。
    /// </summary>
    private sealed class TriggerActivationProbe
    {
        private string? _before;
        private bool _interactionAccepted;

        public TriggerActivationProbe(IPromptService prompts)
            => Prompts = new TrackingPromptService(prompts, () => _interactionAccepted = true);

        public IPromptService Prompts { get; }

        public void Begin(GameState state)
        {
            _interactionAccepted = false;
            _before = Fingerprint(state);
        }

        public bool WasActivated(GameState state, EffectContext context)
            => context.ExplicitActivationObserved
               || _interactionAccepted
               || (_before is not null && !string.Equals(_before, Fingerprint(state), StringComparison.Ordinal));

        private static string Fingerprint(GameState state)
            => JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(state));
    }

    private sealed class TrackingPromptService(IPromptService inner, Action markAccepted) : IPromptService
    {
        public async Task<List<string>> ChooseCards(
            int playerIdx,
            string kind,
            string text,
            IReadOnlyList<string> validChoices,
            int min,
            int max,
            Dictionary<string, object?>? extra = null)
        {
            var chosen = await inner.ChooseCards(playerIdx, kind, text, validChoices, min, max, extra);
            if (chosen.Count > 0) markAccepted();
            return chosen;
        }

        public async Task<bool> ConfirmOptional(int playerIdx, string text)
        {
            // 单独确认“愿意发动”不代表成本或效果已实际落地；后续权威状态变化才计入发动。
            return await inner.ConfirmOptional(playerIdx, text);
        }

        public async Task<int> ChooseOption(int playerIdx, string text, IReadOnlyList<string> options)
        {
            int selected = await inner.ChooseOption(playerIdx, text, options);
            markAccepted();
            return selected;
        }

        public async Task<bool> AskLifeTrigger(int playerIdx, CardInstance lifeCard, bool hasRealTrigger)
        {
            bool accepted = await inner.AskLifeTrigger(playerIdx, lifeCard, hasRealTrigger);
            if (accepted) markAccepted();
            return accepted;
        }
    }

    /// <summary>
    /// 为一次最外层卡牌效果中实际离开生命区的每张卡入队监听。
    /// 采用卡实例 Id 差集而不是数量差：补生命、换生命、批量移生命都能正确识别；
    /// 单纯重排后仍在生命区的卡不会被误判为离场。
    /// </summary>
    private static void EnqueueLifeLeaveWatchers(GameState s, HashSet<Guid> lifeBefore0, HashSet<Guid> lifeBefore1)
    {
        EnqueueForPlayer(0, lifeBefore0);
        EnqueueForPlayer(1, lifeBefore1);

        void EnqueueForPlayer(int owner, HashSet<Guid> before)
        {
            var after = s.Players[owner].LifeArea.Select(c => c.Id).ToHashSet();
            int leftCount = before.Count(id => !after.Contains(id));
            if (leftCount > 0) s.LifeLeftThisTurn.Add(owner);
            for (int i = 0; i < leftCount; i++)
            {
                s.EnqueueWatcher(EffectTrigger.OnLifeLeaveField,
                    new Dictionary<string, object?>
                    {
                        ["owner"] = owner,
                        ["toZero"] = s.Players[owner].LifeArea.Count == 0,
                    });
            }
        }
    }

    private static async Task HandleLifeZoneChangesAsync(
        GameState state,
        IPromptService prompts,
        HashSet<Guid> lifeBefore0,
        HashSet<Guid> lifeBefore1)
    {
        EnqueueLifeLeaveWatchers(state, lifeBefore0, lifeBefore1);
        if ((prompts as PromptSystem)?.Engine is not { } engine) return;

        for (int owner = 0; owner < 2; owner++)
        {
            var before = owner == 0 ? lifeBefore0 : lifeBefore1;
            int added = state.Players[owner].LifeArea.Count(card => !before.Contains(card.Id));
            if (added > 0)
                await HexRules.OnLifeAddedAsync(engine, owner, added);
        }
    }

    /// <summary>排空旧同步 KO 的【KO时】、watcher 队列与被效果登场卡的【登场时】（带再入上限防死循环）。
    /// 反馈#203：改为 public，供 LifeRevealManager 在"纯自登场"(PlayFromTrashFree 只 EnqueueEnterField)之后
    /// 显式排空一次——否则该链路后续若无 depth-0 的 Resolve，PendingEnterFields 不会被排空，
    /// 导致自登场角色(如 PRB02-012 奈美)的【登场时】延迟甚至不触发。
    /// _draining 守卫保证与 Resolve 的 finally 排空互不重入。</summary>
    public static async Task DrainPendingEnterFields(GameState s, IPromptService prompts)
    {
        if (_draining) return; // 已在排空中(如被更外层的 finally 触发)——避免重入
        _draining = true;
        try
        {
            int guard = 0;
            while ((s.PendingKOEffects.Count > 0 || s.PendingWatchers.Count > 0 || s.PendingEnterFields.Count > 0)
                   && guard++ < 50)
            {
                // KO 已经发生，先定向结算离场卡自身的【KO时】；卡已不在场，不能走 CollectListeners。
                if (s.PendingKOEffects.Count > 0)
                {
                    var ko = s.PendingKOEffects[0];
                    s.PendingKOEffects.RemoveAt(0);
                    var previousReason = s.KOReason;
                    var previousActingSide = s.KOActingSide;
                    var previousSource = s.KOSourceCardId;
                    s.KOReason = "effect";
                    s.KOActingSide = ko.ActingSide;
                    s.KOSourceCardId = ko.SourceCardId;
                    try
                    {
                        await Resolve(s, ko.Owner, ko.Card, EffectTrigger.OnKO, prompts);
                    }
                    finally
                    {
                        s.KOReason = previousReason;
                        s.KOActingSide = previousActingSide;
                        s.KOSourceCardId = previousSource;
                    }
                    if (s.IsGameOver)
                    {
                        s.PendingKOEffects.Clear();
                        s.PendingWatchers.Clear();
                        s.PendingEnterFields.Clear();
                        break;
                    }
                    continue;
                }
                // 优先结算"被效果登场角色的登场时"，保证登场连锁先于普通 watcher
                if (s.PendingEnterFields.Count > 0)
                {
                    var ef = s.PendingEnterFields[0];
                    s.PendingEnterFields.RemoveAt(0);
                    await ResolveEnterField(s, ef, prompts);
                    if (s.IsGameOver)
                    {
                        s.PendingKOEffects.Clear();
                        s.PendingWatchers.Clear();
                        s.PendingEnterFields.Clear();
                        break;
                    }
                    continue;
                }
                var ev = s.PendingWatchers[0];
                s.PendingWatchers.RemoveAt(0);
                await TriggerEvent(s, ev.Trigger, prompts, ev.Payload);
                if (s.IsGameOver)
                {
                    s.PendingKOEffects.Clear();
                    s.PendingWatchers.Clear();
                    s.PendingEnterFields.Clear();
                    break;
                }
            }
        }
        finally { _draining = false; }
    }

    /// <summary>对一张"被效果登场"的卡定向解析其【登场时】效果，并派发 OnAllyCharEnter（仅角色）。
    /// 与正常打出路径（GameEngine.ResolveEffectAsync）一致；卡若在排空前已离场则跳过。</summary>
    private static async Task ResolveEnterField(GameState s, PendingEnterField ef, IPromptService prompts)
    {
        var p = s.Players[ef.Owner];
        CardInstance? card = p.Characters.FirstOrDefault(c => c.Id == ef.CardId);
        if (card is null && p.StageCard is { } st && st.Id == ef.CardId) card = st;
        if (card is null && p.ExtraStageCard is { } extraStage && extraStage.Id == ef.CardId) card = extraStage;
        if (card is null) return; // 已离场，跳过
        var enterPayload = new Dictionary<string, object?>
        {
            ["cardId"] = card.Id.ToString(),
            ["owner"] = ef.Owner,
            ["from"] = ef.From,
            ["effectSourceKind"] = ef.EffectSourceKind?.ToString(),
            ["effectSourceNumber"] = ef.EffectSourceNumber,
        };

        // 没有登场时效果的卡仍要经过 Resolve 注册静态场上能力，但不应作为排序候选展示。
        if (!HasEffectForTrigger(card, EffectTrigger.OnEnterField))
        {
            await Resolve(s, ef.Owner, card, EffectTrigger.OnEnterField, prompts);
            if (s.IsGameOver) return;
        }

        var effects = new List<TriggeredCandidate>();
        if (HasEffectForTrigger(card, EffectTrigger.OnEnterField))
            effects.Add(new TriggeredCandidate(ef.Owner, card, EffectTrigger.OnEnterField, null));
        if (card.Info.Kind == CardKind.Character)
            effects.AddRange(CollectListeners(s, EffectTrigger.OnAllyCharEnter, enterPayload)
                .Select(candidate => new TriggeredCandidate(candidate.OwnerIdx, candidate.Source,
                    EffectTrigger.OnAllyCharEnter, enterPayload)));
        await ResolveTriggeredCandidatesInOrder(s, prompts, effects);
    }

    private record Candidate(int OwnerIdx, CardInstance Source);
    private record TriggeredCandidate(
        int OwnerIdx,
        CardInstance Source,
        EffectTrigger Trigger,
        Dictionary<string, object?>? Payload);

    /// <summary>同一规则处理点内，回合玩家优先；同一玩家有多个待发效果时由该玩家决定顺序。</summary>
    private static async Task ResolveTriggeredCandidatesInOrder(
        GameState state,
        IPromptService prompts,
        List<TriggeredCandidate> remaining)
    {
        while (remaining.Count > 0 && !state.IsGameOver)
        {
            int owner = remaining.Any(candidate => candidate.OwnerIdx == state.CurrentTurnPlayer)
                ? state.CurrentTurnPlayer
                : remaining[0].OwnerIdx;
            var owned = remaining.Where(candidate => candidate.OwnerIdx == owner).ToList();
            var selected = owned[0];
            if (owned.Count > 1)
            {
                var duplicateSourceIds = owned.GroupBy(candidate => candidate.Source.Id)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToHashSet();
                string Token(TriggeredCandidate candidate) => duplicateSourceIds.Contains(candidate.Source.Id)
                    ? $"{candidate.Source.Id}:{candidate.Trigger}"
                    : candidate.Source.Id.ToString();
                var tokens = owned.Select(Token).ToList();
                var chosen = await prompts.ChooseCards(owner, "EffectOrder",
                    "多个效果同时触发，请选择下一个要结算的效果",
                    tokens, 1, 1,
                    new Dictionary<string, object?>
                    {
                        ["choiceCards"] = owned.Select(candidate => new
                        {
                            id = Token(candidate),
                            number = candidate.Source.Info.Number,
                            trigger = candidate.Trigger.ToString(),
                        }).ToList(),
                    });
                if (chosen.Count > 0)
                    selected = owned.FirstOrDefault(candidate => Token(candidate) == chosen[0]) ?? selected;
            }

            remaining.Remove(selected);
            await Resolve(state, selected.OwnerIdx, selected.Source, selected.Trigger, prompts, selected.Payload);
        }
    }

    private static List<Candidate> CollectListeners(GameState s, EffectTrigger trigger, Dictionary<string, object?>? payload)
    {
        var list = new List<Candidate>();
        // 【对方的攻击时】只收集防守方（非攻击方）的卡牌，避免攻击方自己的此效果被错误触发
        int? skipIdx = null;
        if (trigger == EffectTrigger.OnOppAttackDeclare && payload != null && payload.TryGetValue("AttackerIdx", out var ai))
            skipIdx = (int)ai!;
        // 【我方/对方的回合结束时】按回合归属过滤：OnMyTurnEnd 仅当前回合方的卡，OnOppTurnEnd 仅对方的卡
        else if (trigger == EffectTrigger.OnMyTurnEnd) skipIdx = 1 - s.CurrentTurnPlayer;
        else if (trigger == EffectTrigger.OnOppTurnEnd) skipIdx = s.CurrentTurnPlayer;

        // 【攻击时】(OnAttackDeclare) 按规则仅「本卡」攻击时触发：直接定位本次战斗的攻击者，
        // 不遍历全场，避免我方任意卡攻击误触其他卡的【攻击时】效果。
        if (trigger == EffectTrigger.OnAttackDeclare && s.CurrentBattle is { } b)
        {
            int ai2 = b.AttackerPlayerIndex;
            var atkP = s.Players[ai2];
            var attacker = atkP.Leader.Id == b.AttackerCardId
                ? atkP.Leader
                : atkP.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
            if (attacker != null && HasEffectForTrigger(attacker, trigger)
                && IsTriggeredEffectAvailable(s, ai2, attacker, trigger, payload))
                list.Add(new(ai2, attacker));
            return list;
        }

        for (int i = 0; i < 2; i++)
        {
            if (skipIdx.HasValue && i == skipIdx.Value) continue;
            var p = s.Players[i];
            // 领袖
            if (HasEffectForTrigger(p.Leader, trigger)
                && IsTriggeredEffectAvailable(s, i, p.Leader, trigger, payload))
                list.Add(new(i, p.Leader));
            // 角色
            foreach (var c in p.Characters)
                if (HasEffectForTrigger(c, trigger)
                    && IsTriggeredEffectAvailable(s, i, c, trigger, payload))
                    list.Add(new(i, c));
            // 舞台
            if (p.StageCard is not null && HasEffectForTrigger(p.StageCard, trigger)
                && IsTriggeredEffectAvailable(s, i, p.StageCard, trigger, payload))
                list.Add(new(i, p.StageCard));
            if (p.ExtraStageCard is not null && HasEffectForTrigger(p.ExtraStageCard, trigger)
                && IsTriggeredEffectAvailable(s, i, p.ExtraStageCard, trigger, payload))
                list.Add(new(i, p.ExtraStageCard));
        }
        return list;
    }

    private static bool IsTriggeredEffectAvailable(
        GameState state,
        int ownerIndex,
        CardInstance source,
        EffectTrigger trigger,
        Dictionary<string, object?>? payload)
    {
        var scripted = CardRulesetManager.For(state).TryGetScriptedEffect(source.Info.Number);
        return scripted is not ITriggeredEffectAvailability availability
            || availability.IsTriggerAvailable(state, ownerIndex, source, trigger, payload);
    }

    /// <summary>
    /// 判断卡牌是否含某触发时机的效果。
    /// 触发时机由卡牌数据预计算的 EffectTags 决定（迁移自旧的卡面原文扫描，行为一致）；
    /// OnLifeRevealTrigger 例外，仍按生命【触发】字段 Trigger 判定。
    /// </summary>
    public static bool HasEffectForTrigger(CardInstance c, EffectTrigger t)
    {
        if (c.IsEffectsNullified) return false;
        if (t == EffectTrigger.OnLifeRevealTrigger)
            return !string.IsNullOrEmpty(c.Info.Trigger);
        return Array.IndexOf(c.Info.EffectTags, t.ToString()) >= 0;
    }
}

/// <summary>手写卡牌脚本接口</summary>
public interface IScriptedEffect
{
    string CardNumber { get; }
    bool HandlesTrigger(EffectTrigger trigger);
    Task Resolve(EffectContext ctx);
}

/// <summary>
/// 手写【启动主要】效果的额外发动条件。动作入口、合法动作列表与客户端权威快照
/// 都通过同一接口判定，避免按钮可点但结算阶段静默失败。
/// 返回 null 表示可以发动，否则返回拒绝原因。
/// </summary>
public interface IActivatedMainAvailability
{
    string? GetActivatedMainUnavailableReason(GameState state, int ownerIndex, CardInstance source);
}

/// <summary>
/// 监听型效果的当前事件可用性。卡面 EffectTags 只表示“拥有该时机”，本接口进一步判断
/// 此次事件的归属、回合、成本与目标是否满足；不满足时不得进入效果排序或发动表现。
/// </summary>
public interface ITriggeredEffectAvailability
{
    bool IsTriggerAvailable(
        GameState state,
        int ownerIndex,
        CardInstance source,
        EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload);
}

/// <summary>
/// 角色进入场上时需要注册、但规则上不属于【登场时】的静态效果。
/// 该注册发生在选择性触发无效判定之前，注册后的持续效果仍会随整卡无效而停用。
/// </summary>
public interface IFieldStaticEffect
{
    Task RegisterFieldStatic(EffectContext ctx);
}

/// <summary>卡牌是否包含至少一项【每回合1次】效果。</summary>
public static class OncePerTurnEffectCatalog
{
    // 手写效果缺少统一元数据，集中登记卡号；实际已使用状态由 EffectRuntime 在
    // TurnOnceUsed 成功增加后记录，取消发动或支付失败不会消耗界面标识。
    private static readonly HashSet<string> ScriptedCards = new(StringComparer.Ordinal)
    {
        "EB01-002", "EB01-008", "EB01-037", "EB01-040", "EB01-047", "EB02-006", "EB02-010", "EB02-023",
        "EB02-035", "EB02-061", "EB03-001", "EB03-008", "EB03-013", "EB03-026", "EB03-033", "EB03-061",
        "EB04-007", "EB04-012", "EB04-031", "EB04-035", "EB04-043", "EB04-044", "OP01-002", "OP01-004",
        "OP01-031", "OP01-051", "OP01-061", "OP01-062", "OP01-112", "OP02-025", "OP02-026", "OP02-071",
        "OP02-093", "OP02-094", "OP03-005", "OP03-076", "OP04-024", "OP04-053", "OP04-058", "OP04-060",
        "OP04-063", "OP04-070", "OP04-072", "OP04-090", "OP04-102", "OP04-105", "OP05-001", "OP05-002",
        "OP05-026", "OP05-029", "OP05-031", "OP05-032", "OP05-041", "OP05-053", "OP05-060", "OP05-074",
        "OP05-080", "OP05-098", "OP05-100", "OP05-107", "OP05-109", "OP05-119", "OP06-009", "OP06-011",
        "OP06-015", "OP06-021", "OP06-042", "OP06-044", "OP06-062", "OP06-076", "OP06-102", "OP06-111",
        "OP06-117", "OP06-118", "OP07-001", "OP07-010", "OP07-029", "OP07-031", "OP07-038", "OP07-042",
        "OP07-048", "OP07-060", "OP07-097", "OP08-001", "OP08-002", "OP08-021", "OP08-046", "OP08-056",
        "OP08-057", "OP08-067", "OP08-074", "OP08-079", "OP08-101", "OP08-105", "OP09-022", "OP09-023",
        "OP09-032", "OP09-061", "OP09-074", "OP09-084", "OP09-093", "OP10-001", "OP10-003", "OP10-022", "OP10-034",
        "OP10-036", "OP10-037", "OP10-042", "OP10-066", "OP10-071", "OP10-074", "OP10-086", "OP10-092", "OP10-102",
        "OP10-118", "OP11-001", "OP11-012", "OP11-022", "OP11-031", "OP11-041", "OP11-043", "OP11-062",
        "OP11-071", "OP11-072", "OP11-073", "OP11-074", "OP11-077", "OP11-088", "OP11-101", "OP11-102",
        "OP11-107", "OP11-117", "OP12-001", "OP12-004", "OP12-008", "OP12-020", "OP12-041", "OP12-053",
        "OP12-061", "OP12-069", "OP12-081", "OP12-091", "OP12-099", "OP13-002", "OP13-017", "OP13-026",
        "OP13-046", "OP13-078", "OP13-079", "OP13-081", "OP13-100", "OP14-001", "OP14-009", "OP14-016", "OP14-020",
        "OP14-029", "OP14-041", "OP14-060", "OP14-068", "OP14-079", "OP14-080", "OP14-092", "OP14-105",
        "OP15-001", "OP15-002", "OP15-003", "OP15-008", "OP15-010", "OP15-017", "OP15-022", "OP15-023",
        "OP15-041", "OP15-058", "OP15-114", "OP16-001", "OP16-018", "OP16-022", "OP16-041", "OP16-080",
        "OP17-001", "OP17-010", "OP17-020", "OP17-025", "OP17-030", "OP17-034", "OP17-040", "OP17-048",
        "OP17-049", "OP17-053", "OP17-058", "OP17-062", "OP17-063", "OP17-064", "OP17-072", "OP17-101",
        "P-011", "P-073", "P-076", "P-077", "P-086", "P-095", "P-096", "P-111", "P-122", "PRB01-001",
        "PRB02-002", "ST02-010", "ST03-007", "ST04-001", "ST05-010", "ST09-010", "ST10-002", "ST10-006",
        "ST10-007", "ST10-011", "ST10-014", "ST12-001", "ST12-010", "ST13-001", "ST13-002", "ST13-003",
        "ST15-005", "ST19-003", "ST19-004", "ST19-005", "ST20-002", "ST22-001", "ST22-005", "ST25-003",
        "ST31-001", "ST34-001", "ST36-005", "OP18-021", "OP18-060", "OP18-119", "EB05-010",
    };

    public static bool Contains(string cardNumber, GameState? state = null)
        => ScriptedCards.Contains(cardNumber)
           || (state is null
               ? Dsl.DslInterpreter.HasOncePerTurnEffect(cardNumber)
               : CardRulesetManager.For(state).HasOncePerTurnEffect(cardNumber));
}

public static class ScriptedEffectRegistry
{
    private static readonly Dictionary<string, IScriptedEffect> LegacyOverrides = new(StringComparer.OrdinalIgnoreCase);

    public static IScriptedEffect? TryGet(string number)
        => LegacyOverrides.TryGetValue(number, out var legacy)
            ? legacy
            : CardRulesetManager.Current.TryGetScriptedEffect(number);

    /// <summary>仅保留给旧测试/调试代码；线上规则热更新必须通过不可变规则包激活。</summary>
    public static void Register(IScriptedEffect effect) => LegacyOverrides[effect.CardNumber] = effect;

    internal static IReadOnlyDictionary<string, IScriptedEffect> ScanAssembly(
        Assembly assembly,
        bool rejectDuplicates = false)
    {
        var effects = new Dictionary<string, IScriptedEffect>(StringComparer.OrdinalIgnoreCase);
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }

        foreach (var type in types)
        {
            if (type.IsAbstract || type.IsInterface || !typeof(IScriptedEffect).IsAssignableFrom(type)) continue;
            try
            {
                var instance = (IScriptedEffect)Activator.CreateInstance(type)!;
                if (rejectDuplicates && effects.ContainsKey(instance.CardNumber))
                    throw new InvalidOperationException($"程序集 {assembly.GetName().Name} 重复注册卡效 {instance.CardNumber}");
                // 内置程序集沿用旧注册器的兼容行为：同一卡号后扫描到的实现覆盖前者。
                effects[instance.CardNumber] = instance;
            }
            catch (MissingMethodException)
            {
                // 没有无参构造函数的类型不是可加载的卡效插件。
            }
        }
        return effects;
    }
}
