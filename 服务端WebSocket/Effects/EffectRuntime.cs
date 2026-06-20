using GrandUMI.Cards;
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

    /// <summary>由 AtomicOps 在状态变更时调用，把 watcher 事件入队到当前效果所属的 state（无效果上下文时忽略）</summary>
    public static void NotifyWatcher(EffectTrigger trigger, Dictionary<string, object?>? payload = null)
        => _ambient?.EnqueueWatcher(trigger, payload);

    /// <summary>手牌因效果被丢弃时入队 OnHandDiscarded（owner=该手牌所属方）；仅效果上下文内有效。</summary>
    public static void NotifyHandDiscarded(PlayerState p)
    {
        var s = _ambient;
        if (s is null) return;
        int owner = ReferenceEquals(s.Players[0], p) ? 0 : ReferenceEquals(s.Players[1], p) ? 1 : -1;
        if (owner < 0) return;
        s.EnqueueWatcher(EffectTrigger.OnHandDiscarded, new Dictionary<string, object?> { ["owner"] = owner });
    }

    /// <summary>对单个卡牌的指定触发时机解析效果</summary>
    public static async Task Resolve(GameState s, int ownerIdx, CardInstance source, EffectTrigger trigger, IPromptService prompts, Dictionary<string, object?>? payload = null)
    {
        var prevAmbient = _ambient;
        var prevSource = _currentSourceAL.Value;
        var prevActing = _actingSideAL.Value;
        _ambient = s;
        _currentSourceAL.Value = source;
        _actingSideAL.Value = ownerIdx;
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

            // 持续"效果无效"：被持续无效化的卡（整卡或该类触发），其效果不发动
            if (s.IsContinuouslyNullified(source) || s.IsTriggerNullified(source, trigger)) return;

            // 1. 优先用手写脚本
            var scripted = ScriptedEffectRegistry.TryGet(source.Info.Number);
            if (scripted is not null && scripted.HandlesTrigger(trigger))
            {
                await scripted.Resolve(ctx);
                return;
            }

            // 2. 退回 DSL
            await Dsl.DslInterpreter.TryResolve(ctx);
        }
        finally
        {
            _depth--;
            _ambient = prevAmbient;
            _currentSourceAL.Value = prevSource;
            _actingSideAL.Value = prevActing;
            // 最外层效果结束后，排空期间积累的反应式 watcher 事件 + 被效果登场卡的【登场时】
            if (_depth == 0 && !_draining && (s.PendingWatchers.Count > 0 || s.PendingEnterFields.Count > 0))
                await DrainWatchers(s, prompts);
        }
    }

    /// <summary>排空 watcher 队列 + 被效果登场卡的【登场时】（带再入上限防死循环）</summary>
    private static async Task DrainWatchers(GameState s, IPromptService prompts)
    {
        _draining = true;
        try
        {
            int guard = 0;
            while ((s.PendingWatchers.Count > 0 || s.PendingEnterFields.Count > 0) && guard++ < 50)
            {
                // 优先结算"被效果登场角色的登场时"，保证登场连锁先于普通 watcher
                if (s.PendingEnterFields.Count > 0)
                {
                    var ef = s.PendingEnterFields[0];
                    s.PendingEnterFields.RemoveAt(0);
                    await ResolveEnterField(s, ef, prompts);
                    if (s.IsGameOver) { s.PendingWatchers.Clear(); s.PendingEnterFields.Clear(); break; }
                    continue;
                }
                var ev = s.PendingWatchers[0];
                s.PendingWatchers.RemoveAt(0);
                await TriggerEvent(s, ev.Trigger, prompts, ev.Payload);
                if (s.IsGameOver) { s.PendingWatchers.Clear(); s.PendingEnterFields.Clear(); break; }
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
                new Dictionary<string, object?> { ["cardId"] = card.Id.ToString(), ["owner"] = ef.Owner, ["from"] = ef.From });
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
