using GrandUMI.Cards;
using GrandUMI.Effects.Scripted;
using GrandUMI.Game;

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
        var candidates = CollectListeners(s, trigger, payload);
        // 排序：回合玩家在前，同方按场上从左到右
        candidates.Sort((a, b) =>
        {
            int aTurn = a.OwnerIdx == s.CurrentTurnPlayer ? 0 : 1;
            int bTurn = b.OwnerIdx == s.CurrentTurnPlayer ? 0 : 1;
            return aTurn.CompareTo(bTurn);
        });

        foreach (var c in candidates)
        {
            await Resolve(s, c.OwnerIdx, c.Source, trigger, prompts, payload);
            // 规则处理点：若已分胜负则中断
            if (s.IsGameOver) return;
        }
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

    /// <summary>由 AtomicOps 在状态变更时调用，把 watcher 事件入队到当前效果所属的 state（无效果上下文时忽略）</summary>
    public static void NotifyWatcher(EffectTrigger trigger, Dictionary<string, object?>? payload = null)
        => _ambient?.EnqueueWatcher(trigger, payload);

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
    public static async Task Resolve(GameState s, int ownerIdx, CardInstance source, EffectTrigger trigger, IPromptService prompts, Dictionary<string, object?>? payload = null)
    {
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
        _ambient = s;
        _currentSourceAL.Value = source;
        _actingSideAL.Value = ownerIdx;
        _promptsAL.Value = prompts;
        _depth++;
        try
        {
            var ctx = new EffectContext
            {
                State = s,
                OwnerIndex = ownerIdx,
                Source = source,
                Trigger = trigger,
                Prompts = prompts,
                Engine = (prompts as PromptSystem)?.Engine,
            };
            if (payload is not null)
                foreach (var (k, v) in payload) ctx.Vars[k] = v;

            // “发动过事件”是发动历史，而不是事件效果是否成功解决。
            // 统一在效果运行时记录，确保从手牌直接发动、反击发动和被其它效果免费发动都能被统计。
            if (source.Info.Kind == CardKind.Event
                && source.Info.Cost >= 3
                && (trigger == EffectTrigger.EventMain || trigger == EffectTrigger.EventCounter))
            {
                s.Players[ownerIdx].HasActivatedBaseCost3PlusEventThisTurn = true;
            }

            // 持续"效果无效"：被持续无效化的卡（整卡或该类触发），其效果不发动
            if (source.IsEffectsNullified || s.IsContinuouslyNullified(source) || s.IsTriggerNullified(source, trigger)) return;

            // 出牌流程会统一调用 OnEnterField，即使卡牌没有该时点效果；只为确实声明了
            // 当前触发时机的卡记录表现，避免无效果卡登场时误播“效果发动”。
            // 提示型快照会立即带走事件，无交互效果则随本批次最终快照发送。
            if (HasEffectForTrigger(source, trigger))
                ctx.Engine?.QueueEffectActivation(ownerIdx, source, trigger);

            // 1. 优先用手写脚本
            var scripted = ScriptedEffectRegistry.TryGet(source.Info.Number);
            if (scripted is not null && scripted.HandlesTrigger(trigger))
            {
                await scripted.Resolve(ctx);
                return;
            }

            // 2. 已确认省略项的前置补齐层：支付旧 DSL 无法表达的成本、注册持续效果，
            // 或在旧定义不精确时完整接管该触发。返回 false 表示本次已处理/取消，不再执行 DSL。
            if (!await DeclaredOmissionEffects.BeforeDsl(ctx)) return;

            // 3. 退回 DSL
            await Dsl.DslInterpreter.TryResolve(ctx);

            // 4. 后置补齐层：依赖 DSL 刚选中的同一目标，补结算条件加成、关键字或后续动作。
            await DeclaredOmissionEffects.AfterDsl(ctx);
        }
        finally
        {
            if (donBefore is not null)
            {
                foreach (var don in s.Players[ownerIdx].CostArea)
                    if (donBefore.TryGetValue(don.Id, out var before)
                        && before == DonState.Rest && don.State == DonState.Active)
                        don.State = DonState.Rest;
            }
            _depth--;
            if (isRootResolve)
                EnqueueLifeLeaveWatchers(s, lifeBefore0!, lifeBefore1!);
            _ambient = prevAmbient;
            _currentSourceAL.Value = prevSource;
            _actingSideAL.Value = prevActing;
            _promptsAL.Value = prevPrompts;
            // 最外层效果结束后，排空期间积累的【KO时】、反应式 watcher 与被效果登场卡的【登场时】。
            if (_depth == 0 && !_draining
                && (s.PendingKOEffects.Count > 0 || s.PendingWatchers.Count > 0 || s.PendingEnterFields.Count > 0))
                await DrainPendingEnterFields(s, prompts);
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
        if (card is null) return; // 已离场，跳过
        await Resolve(s, ef.Owner, card, EffectTrigger.OnEnterField, prompts);
        if (!s.IsGameOver && card.Info.Kind == CardKind.Character)
            await TriggerEvent(s, EffectTrigger.OnAllyCharEnter, prompts,
                new Dictionary<string, object?>
                {
                    ["cardId"] = card.Id.ToString(),
                    ["owner"] = ef.Owner,
                    ["from"] = ef.From,
                    ["effectSourceKind"] = ef.EffectSourceKind?.ToString(),
                    ["effectSourceNumber"] = ef.EffectSourceNumber,
                });
    }

    private record Candidate(int OwnerIdx, CardInstance Source);

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
            if (attacker != null && HasEffectForTrigger(attacker, trigger))
                list.Add(new(ai2, attacker));
            return list;
        }

        for (int i = 0; i < 2; i++)
        {
            if (skipIdx.HasValue && i == skipIdx.Value) continue;
            var p = s.Players[i];
            // 领袖
            if (HasEffectForTrigger(p.Leader, trigger))
                list.Add(new(i, p.Leader));
            // 角色
            foreach (var c in p.Characters)
                if (HasEffectForTrigger(c, trigger))
                    list.Add(new(i, c));
            // 舞台
            if (p.StageCard is not null && HasEffectForTrigger(p.StageCard, trigger))
                list.Add(new(i, p.StageCard));
        }
        return list;
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

public static class ScriptedEffectRegistry
{
    private static readonly Dictionary<string, IScriptedEffect> _byNumber = new();
    private static readonly object _scanLock = new();
    private static volatile bool _scanned;

    public static IScriptedEffect? TryGet(string number)
    {
        if (!_scanned) ScanAll();
        return _byNumber.TryGetValue(number, out var e) ? e : null;
    }

    public static void Register(IScriptedEffect effect) => _byNumber[effect.CardNumber] = effect;

    private static void ScanAll()
    {
        // 必须加锁且 _scanned 置位放在填充之后：否则并发下另一线程见 _scanned==true 即跳过扫描，
        // 但此时 _byNumber 尚未填充完，TryGet 返回 null → 脚本卡效果被漏判（如领袖永续被动不注册）。
        lock (_scanLock)
        {
            if (_scanned) return;
            // 反射扫描当前程序集中所有 IScriptedEffect 实现
            var asm = typeof(ScriptedEffectRegistry).Assembly;
            foreach (var t in asm.GetTypes())
            {
                if (t.IsAbstract || t.IsInterface) continue;
                if (!typeof(IScriptedEffect).IsAssignableFrom(t)) continue;
                try
                {
                    var inst = (IScriptedEffect)Activator.CreateInstance(t)!;
                    _byNumber[inst.CardNumber] = inst;
                }
                catch { /* 跳过构造失败的 */ }
            }
            Console.WriteLine($"[Effects] 已注册 {_byNumber.Count} 张手写卡效果");
            _scanned = true;
        }
    }
}
