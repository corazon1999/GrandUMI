using GrandUMI.Effects.Rules;

namespace GrandUMI.Game;

/// <summary>开局决定先后手时的一轮双方六面骰结果。</summary>
public sealed record StartingDiceRound(int Player0, int Player1);

/// <summary>
/// 完整对局状态。引擎所有操作都在此对象上进行。
/// </summary>
public class GameState
{
    public required string RoomId { get; init; }

    /// <summary>本局创建时锁定的卡效规则版本；整局以及重启重放期间都不得改变。</summary>
    public string RulesetId { get; internal set; } = "unassigned";

    /// <summary>规则集运行时句柄不进入状态序列化，恢复时按 RulesetId 重新绑定。</summary>
    internal CardRuleset? Ruleset { get; set; }

    /// <summary>Per-match RNG seed used for deterministic replay.</summary>
    public required int RngSeed { get; init; }

    private Random? _rng;
    /// <summary>
    /// 本局确定性 RNG（由 RngSeed 惰性派生）。所有洗牌/随机都必须走它——
    /// 禁止用共享静态 Random，否则重放无法重现、且并发房间会互相干扰。
    /// </summary>
    public Random Rng => _rng ??= new Random(RngSeed);

    /// <summary>Monotonic random event sequence within this match.</summary>
    public int RandomSeq { get; set; }

    /// <summary>双方玩家。约定 0 = 房主/匹配 P1，1 = 加入者/匹配 P2</summary>
    public PlayerState[] Players { get; } = new PlayerState[2];

    /// <summary>当前回合玩家索引（0 / 1）</summary>
    public int CurrentTurnPlayer { get; set; }

    /// <summary>第一回合的先攻方索引；-1 表示仍在等待骰点胜者选择。</summary>
    public int FirstPlayer { get; set; }

    /// <summary>开局骰点的全部轮次；同点轮次也会保留，供客户端播放重骰过程。</summary>
    public List<StartingDiceRound> StartingDiceRounds { get; } = new();

    /// <summary>最终骰点较大、拥有先后手选择权的玩家索引；非骰点开局为 -1。</summary>
    public int StartingPlayerChooser { get; set; } = -1;

    /// <summary>骰点胜者选择先后手的服务端权威截止时间；null 表示已完成选择或无需骰点。</summary>
    public DateTime? StartingPlayerChoiceDeadlineUtc { get; set; }

    /// <summary>是否已经确定第一回合的先攻方。</summary>
    public bool StartingPlayerChosen => FirstPlayer is 0 or 1;

    public int TurnCount { get; set; } = 1;

    public Phase Phase { get; set; } = Phase.Reset;

    /// <summary>等待玩家选择（响应 Prompt）时不为 null</summary>
    public PendingPrompt? PendingPrompt { get; set; }

    /// <summary>检索/公开牌时短暂下发给双方展示，仅在那一次公开广播时非空（即设即清，不入存档）</summary>
    public RevealInfo? PendingReveal { get; set; }

    /// <summary>当前正在进行的战斗（仅 BattleAttack/Block/Counter/Damage 阶段非 null）</summary>
    public BattleContext? CurrentBattle { get; set; }

    /// <summary>胜负已分时为非空；协商平局时保持为空。</summary>
    public int? WinnerIndex { get; set; }
    /// <summary>双方同意因 Bug 结束本局时为 true；平局没有胜者。</summary>
    public bool IsDraw { get; set; }
    public string? GameOverReason { get; set; }
    public bool IsGameOver => WinnerIndex.HasValue || IsDraw;

    public const int DrawRequestRejectionLimit = 3;
    /// <summary>当前等待回应的平局申请发起者；没有申请时为 null。</summary>
    public int? PendingDrawRequester { get; set; }
    /// <summary>双方各自在本局发起平局申请后被拒绝的次数。</summary>
    public int[] DrawRequestRejectionCounts { get; } = [0, 0];

    /// <summary>双方完成换牌后最终保留的起手牌卡号，用于对局结束时写入公开 Leader 统计。</summary>
    public List<string>[] StartingHandCardNumbers { get; } = [new(), new()];

    /// <summary>公开匹配的双方独立操作棋钟；选择先后手与调度手牌阶段不启用。</summary>
    public bool OperationClockEnabled { get; set; }
    public long[] OperationClockRemainingMs { get; } = [1_200_000, 1_200_000];
    public int OperationClockActivePlayer { get; set; } = -1;
    public DateTime? OperationClockSyncUtc { get; set; }
    public bool OperationClockPaused { get; set; }
    public MatchKind MatchKind { get; set; } = MatchKind.UnknownHuman;

    /// <summary>序号（每次状态变化 +1，便于客户端识别快照新旧）</summary>
    public int Tick { get; set; }

    /// <summary>
    /// PreKO 触发期间共享的"已拦截 KO"集合。
    /// BattleEngine.KOCardAsync 开始前清空，触发 PreKO 后检查；
    /// 置换效果脚本通过 ctx.State.PreventKO(card) 写入。
    /// </summary>
    public HashSet<Guid> PreventKOCardIds { get; } = new();

    public void MarkPreventKO(Guid cardId) => PreventKOCardIds.Add(cardId);

    /// <summary>
    /// Cards being KO'd by the same simultaneous process. Replacement effects may use
    /// this set to replace the whole matching part of that process with one payment.
    /// </summary>
    public IReadOnlySet<Guid>? SimultaneousKOVictimIds { get; internal set; }

    /// <summary>
    /// Cards leaving the field together as the result of one effect.  Replacement
    /// effects can use this to replace every matching leave with one payment.
    /// </summary>
    public IReadOnlySet<Guid>? SimultaneousLeaveVictimIds { get; internal set; }

    /// <summary>"代替离场使其不离场"置换守护写入的卡集合：离场路径(KO/退手牌/回卡组/置入生命)检查后取消该卡本次离场。</summary>
    public HashSet<Guid> PreventLeaveCardIds { get; } = new();
    public void MarkPreventLeave(Guid cardId) => PreventLeaveCardIds.Add(cardId);

    /// <summary>Apply one replacement to every matching victim in the active simultaneous process.</summary>
    public void MarkPreventEffectLeaveBatch(int ownerIdx, Guid currentVictimId,
        Func<CardInstance, bool> matches, bool isKoReplacement = false)
    {
        // 除当前显式批次外，记录到本张卡牌效果的完整结算过程，兼容旧实现逐条处理多目标离场。
        Effects.EffectRuntime.RegisterEffectLeaveReplacement(this, ownerIdx, matches);

        if (SimultaneousKOVictimIds is { } koVictims)
        {
            foreach (var card in Players[ownerIdx].Characters)
            {
                if (!koVictims.Contains(card.Id) || !matches(card)) continue;
                MarkPreventKO(card.Id);
                MarkPreventLeave(card.Id);
            }
            return;
        }

        if (SimultaneousLeaveVictimIds is { } leaveVictims)
        {
            foreach (var card in Players[ownerIdx].Characters)
                if (leaveVictims.Contains(card.Id) && matches(card)) MarkPreventLeave(card.Id);
            return;
        }

        if (isKoReplacement) MarkPreventKO(currentVictimId);
        else MarkPreventLeave(currentVictimId);
    }

    /// <summary>永续效果列表（来源卡离场时由 ContinuousEffectRegistry 清理）</summary>
    public List<ContinuousEffect> ContinuousEffects { get; } = new();

    /// <summary>待派发的反应式 watcher 事件队列（AtomicOps 同步入队，EffectRuntime 在最外层效果结束后异步排空）</summary>
    public List<PendingWatcher> PendingWatchers { get; } = new();

    /// <summary>旧同步效果 KO 产生的定向【KO时】队列。
    /// 目标卡已离场，不能再通过场上监听器收集，故保存卡实例及 KO 来源上下文，在最外层效果结束后异步结算。</summary>
    public List<PendingKOEffect> PendingKOEffects { get; } = new();

    /// <summary>待触发【登场时】的"被效果登场"卡牌队列：AtomicOps.Play*Free 同步入队，
    /// EffectRuntime 在最外层效果结束后定向解析其【登场时】并派发 OnAllyCharEnter（修：卡效登场的角色登场时不发动）。</summary>
    public List<PendingEnterField> PendingEnterFields { get; } = new();

    /// <summary>true 时本回合结束不切换玩家（"在本回合之后追加获得我方的回合"）</summary>
    public bool ExtraTurnPending { get; set; }

    /// <summary>本回合无法登场角色卡牌的玩家集合（OP14-020）</summary>
    public HashSet<int> NoPlayCharacterThisTurn { get; } = new();

    /// <summary>本回合禁止登场“原本费用不低于阈值”的角色；键为玩家索引（OP13-118）。</summary>
    public Dictionary<int, int> NoPlayCharacterOriginalCostGteThisTurn { get; } = new();

    /// <summary>本回合无法通过"我方的效果"将生命卡牌加入手牌的玩家集合（ST15-001）</summary>
    public HashSet<int> NoEffectLifeToHandThisTurn { get; } = new();

    /// <summary>本回合无法通过角色效果将咚!!转为活跃状态的玩家集合（EB04-016）。</summary>
    public HashSet<int> NoActivateDonByCharacterEffectThisTurn { get; } = new();

    /// <summary>本回合中生命牌曾离开过生命区的玩家。供 P-120 等手牌静态减费实时判定。</summary>
    public HashSet<int> LifeLeftThisTurn { get; } = new();

    /// <summary>攻击前置弃牌税：该玩家所有角色攻击前需弃 N 张手牌（OP08-043）。0=无</summary>
    public int[] AttackTaxDiscard { get; } = new int[2];

    /// <summary>延迟到本回合结束执行的任务（OP06-006 等）；EnterEndPhase 处理</summary>
    public List<EndTurnTask> EndOfTurnTasks { get; } = new();

    /// <summary>延迟到"下个对方主要阶段开始时"执行的任务（PRB02-005 路飞）。
    /// 登记方 Owner；EndTurnAsync 切到 Owner 的对方回合后执行并清除。</summary>
    public List<NextOppMainPhaseTask> NextOppMainPhaseTasks { get; } = new();

    /// <summary>"规则上卡组变为0张时改判胜利"的玩家集合（OP03-040 奈美领袖，OnGameStart 登记）</summary>
    public HashSet<int> DeckOutVictoryPlayers { get; } = new();

    /// <summary>本回合一次性消费的"下次登场减费"（OP02-025）；HandPlayCost 预览、CardPlayer.Play 消费、回合末清</summary>
    public List<OneShotPlayDiscount> OneShotPlayDiscounts { get; } = new();

    /// <summary>取本卡当前适用的一次性登场减费量（取首个匹配项的额度，不消费）</summary>
    public int OneShotDiscountFor(int playerIdx, CardInstance card)
    {
        var d = OneShotPlayDiscounts.FirstOrDefault(x => x.Matches(playerIdx, card));
        return d?.Amount ?? 0;
    }

    /// <summary>入队一个 watcher 事件（由 AtomicOps 在状态变更时调用，仅在效果解析期间有意义）</summary>
    public void EnqueueWatcher(Effects.EffectTrigger trigger, Dictionary<string, object?>? payload = null)
        => PendingWatchers.Add(new PendingWatcher { Trigger = trigger, Payload = payload ?? new() });

    /// <summary>登记一张已被效果 KO 的卡，稍后定向发动其【KO时】效果。</summary>
    public void EnqueueKOEffect(int owner, CardInstance card, int actingSide, Guid? sourceCardId)
        => PendingKOEffects.Add(new PendingKOEffect
        {
            Owner = owner,
            Card = card,
            ActingSide = actingSide,
            SourceCardId = sourceCardId,
        });

    /// <summary>入队一张"被效果登场"的卡牌（由 AtomicOps.Play*Free 调用），稍后定向触发其【登场时】效果。
    /// from = 来源区("hand"/"trash"/"deck"/"life")，供 OnAllyCharEnter 监听卡区分（如 OP16-079 仅废弃区登场赋速攻）。</summary>
    public void EnqueueEnterField(int owner, CardInstance card, string? from = null)
        => PendingEnterFields.Add(new PendingEnterField
        {
            Owner = owner,
            CardId = card.Id,
            From = from,
            EffectSourceKind = Effects.EffectRuntime.CurrentSource?.Info.Kind,
            EffectSourceNumber = Effects.EffectRuntime.CurrentSource?.Info.Number,
        });

    /// <summary>
    /// OP09-022 静态规则：该领袖效果生效时，我方角色均以休息状态登场。
    /// 所有正常登场与效果登场入口都通过本方法统一判定。
    /// </summary>
    public bool ShouldCharacterEnterRested(int playerIdx, CardInstance card)
    {
        if (card.Info.Kind != Cards.CardKind.Character) return false;
        var leader = Players[playerIdx].Leader;
        return leader.Info.Number == "OP09-022"
            && !leader.IsEffectsNullified
            && !IsContinuouslyNullified(leader);
    }

    /// <summary>评估指定卡当前从 ContinuousEffects 获得的总力量加成</summary>
    public int ContinuousPowerBonus(int sideIdx, CardInstance card)
    {
        int sum = 0;
        foreach (var eff in ContinuousEffects)
        {
            // 先按维度短路再调谓词：零力量增量的效果对力量无贡献，求值它的谓词不仅浪费、
            // 还会引发递归（如 OP16-017 力量谓词内查费用，若此处不跳过则费用查询又回调它）。
            if (eff.PowerDelta == 0 && eff.PowerDeltaResolver is null) continue;
            if (!IsContinuousEffectActive(eff)) continue;
            if (!MatchesContinuousScope(eff, sideIdx, card)) continue;
            if (!eff.Predicate(this, sideIdx, card)) continue;
            sum += eff.PowerDelta + (eff.PowerDeltaResolver?.Invoke(this, sideIdx, card) ?? 0);
        }
        return sum;
    }

    /// <summary>评估持续效果对指定卡“原本力量”的覆盖；多条同时生效时以后注册者为准。</summary>
    public int? ContinuousOriginalPowerOverride(int sideIdx, CardInstance card)
    {
        int? value = null;
        foreach (var eff in ContinuousEffects)
        {
            if (!eff.OriginalPowerOverride.HasValue) continue;
            if (!IsContinuousEffectActive(eff)) continue;
            if (!MatchesContinuousScope(eff, sideIdx, card)) continue;
            if (!eff.Predicate(this, sideIdx, card)) continue;
            value = eff.OriginalPowerOverride.Value;
        }
        return value;
    }

    /// <summary>统一计算规则意义上的“原本力量”，包含卡实例及持续效果的“变为X”覆盖。</summary>
    public int OriginalPowerOf(int sideIdx, CardInstance card)
        => ContinuousOriginalPowerOverride(sideIdx, card)
           ?? card.OriginalPowerOverride
           ?? card.OriginalPowerOverridesUntilOppEnd.LastOrDefault()?.Value
           ?? card.Info.Power;

    /// <summary>统一计算某张卡当前力量：基础 + 咚 + 临时修正 + 永续修正</summary>
    public int CurrentPowerOf(int sideIdx, CardInstance card)
    {
        var p = Players[sideIdx];
        int donCount = p.AttachedDonCount(card.Id);
        bool ownerTurn = CurrentTurnPlayer == sideIdx;
        int basePower = card.CurrentPower(donCount, ownerTurn);
        int instanceOriginalPower = card.OriginalPowerOverride
            ?? card.OriginalPowerOverridesUntilOppEnd.LastOrDefault()?.Value
            ?? card.Info.Power;
        basePower += OriginalPowerOf(sideIdx, card) - instanceOriginalPower;
        return basePower + ContinuousPowerBonus(sideIdx, card);
    }

    /// <summary>
    /// 按卡自动定位其所属一方再计算当前力量（含咚!!、临时修正及持续光环）。
    /// 卡须在场上；不在场上时回退为卡实例自身可计算的力量。
    /// </summary>
    public int CurrentPowerOf(CardInstance card)
    {
        int side = SideOf(card);
        if (side >= 0) return CurrentPowerOf(side, card);
        return card.CurrentPower(0, ownerTurn: false);
    }

    /// <summary>评估指定卡当前从 ContinuousEffects 获得的总费用修正</summary>
    public int ContinuousCostBonus(int sideIdx, CardInstance card, string? excludedSourceCardId = null)
    {
        int sum = 0;
        foreach (var eff in ContinuousEffects)
        {
            // 先按维度短路再调谓词：零费用增量的效果对费用无贡献，跳过其谓词求值，
            // 同时切断"力量谓词 → 查费用 → 又求值该力量谓词"的递归（OP16-017）。
            if (eff.CostDelta == 0 && eff.CostDeltaResolver is null) continue;
            if (excludedSourceCardId is not null && eff.SourceCardId == excludedSourceCardId) continue;
            if (!IsContinuousEffectActive(eff)) continue;
            if (!MatchesContinuousScope(eff, sideIdx, card)) continue;
            if (!eff.Predicate(this, sideIdx, card)) continue;
            sum += eff.CostDelta + (eff.CostDeltaResolver?.Invoke(this, sideIdx, card) ?? 0);
        }
        return sum;
    }

    /// <summary>手牌打出该卡的实际费用（含持续费用光环，如"从手牌登场X角色费用-1"），最低 0</summary>
    public int HandPlayCost(int playerIdx, CardInstance card)
    {
        int v = card.Info.Cost + card.CostModThisTurn + card.CostModPersistent
                + ContinuousCostBonus(playerIdx, card)
                + Effects.HandStaticCost.Delta(this, playerIdx, card)   // 手牌中静态减费（如 OP16-005）
                - OneShotDiscountFor(playerIdx, card);                   // 一次性下次登场减费（OP02-025，预览不消费）
        return v < 0 ? 0 : v;
    }

    /// <summary>定位某卡当前所属的一方下标（场上：角色/领袖/舞台）；不在场返回 -1</summary>
    public int SideOf(CardInstance card)
    {
        for (int s = 0; s < 2; s++)
        {
            var p = Players[s];
            if (p.Characters.Contains(card) || ReferenceEquals(p.Leader, card) || ReferenceEquals(p.StageCard, card))
                return s;
        }
        return -1;
    }

    /// <summary>该卡当前是否被某 ContinuousEffect 持续/条件赋予了关键词 kw</summary>
    public bool HasContinuousKeyword(CardInstance card, string kw)
    {
        int side = SideOf(card);
        if (side < 0) return false;
        foreach (var eff in ContinuousEffects)
            if (eff.GrantKeyword == kw && IsContinuousEffectActive(eff)
                && MatchesContinuousScope(eff, side, card) && eff.Predicate(this, side, card)) return true;
        return false;
    }

    /// <summary>该卡当前是否被持续效果保护不会被 KO。context: "battle"/"effect"（来源场景）</summary>
    public bool IsKoGuarded(CardInstance card, string context)
    {
        int side = SideOf(card);
        if (side < 0) return false;
        foreach (var eff in ContinuousEffects)
        {
            if (eff.KoGuard is null) continue;
            if (!IsContinuousEffectActive(eff)) continue;
            if (eff.KoGuard != "any" && eff.KoGuard != context) continue;
            if (!MatchesContinuousScope(eff, side, card)) continue;
            if (eff.Predicate(this, side, card)) return true;
        }
        return false;
    }

    /// <summary>该卡当前是否被持续效果保护不会离开场上（含KO/退手/放底/置生命）。context: "effect"（来源场景）</summary>
    public bool IsLeaveGuarded(CardInstance card, string context)
    {
        int side = SideOf(card);
        if (side < 0) return false;
        foreach (var eff in ContinuousEffects)
        {
            if (eff.LeaveGuard is null) continue;
            if (!IsContinuousEffectActive(eff)) continue;
            if (eff.LeaveGuard != "any" && eff.LeaveGuard != context) continue;
            if (!MatchesContinuousScope(eff, side, card)) continue;
            if (eff.Predicate(this, side, card)) return true;
        }
        return false;
    }

    /// <summary>当前正在进行的 KO 的来源（"battle"/"effect"），及发起方下标（效果KO时为发动效果的一方）。
    /// 由 KO 流程在触发受害者 OnKO / 守护者前设置，供 EB01-057 等"因对方的效果而被KO"判定，KO 结束后复位。</summary>
    public string? KOReason;
    public int KOActingSide = -1;
    /// <summary>当前效果KO的来源卡 Id（= 发动该KO效果的卡），供"不会因对方某类角色的效果而被KO"判定（OP14-003）。</summary>
    public Guid? KOSourceCardId;

    /// <summary>该卡当前是否被持续效果无效化（整卡）</summary>
    public bool IsContinuouslyNullified(CardInstance card)
    {
        int side = SideOf(card);
        if (side < 0) return false;
        foreach (var eff in ContinuousEffects)
            if (eff.NullifyEffect && IsContinuousEffectActive(eff)
                && MatchesContinuousScope(eff, side, card) && eff.Predicate(this, side, card)) return true;
        return false;
    }

    /// <summary>该卡的某一类触发当前是否被持续效果选择性无效化（如仅【登场时】）</summary>
    public bool IsTriggerNullified(CardInstance card, Effects.EffectTrigger trigger)
    {
        int side = SideOf(card);
        if (side < 0) return false;
        foreach (var eff in ContinuousEffects)
            if (eff.NullifyOnlyTrigger == trigger && IsContinuousEffectActive(eff)
                && MatchesContinuousScope(eff, side, card) && eff.Predicate(this, side, card)) return true;
        return false;
    }

    /// <summary>该卡当前是否被持续效果阻止在重置阶段转为活跃</summary>
    public bool IsResetPrevented(CardInstance card)
    {
        int side = SideOf(card);
        if (side < 0) return false;
        foreach (var eff in ContinuousEffects)
            if (eff.PreventReset && IsContinuousEffectActive(eff)
                && MatchesContinuousScope(eff, side, card) && eff.Predicate(this, side, card)) return true;
        return false;
    }

    /// <summary>该卡当前是否被持续效果条件性施加了某限制（如条件性 CannotAttack）</summary>
    public bool HasContinuousRestriction(CardInstance card, RestrictionKind kind)
    {
        int side = SideOf(card);
        if (side < 0) return false;
        foreach (var eff in ContinuousEffects)
            if (eff.GrantRestriction == kind && IsContinuousEffectActive(eff)
                && MatchesContinuousScope(eff, side, card) && eff.Predicate(this, side, card)) return true;
        return false;
    }

    /// <summary>
    /// 统一执行持续效果的作用范围。此前 Scope 只被记录而没有参与结算，导致我方角色光环
    /// 泄漏到对方场上与双方手牌。手牌效果必须显式声明 IncludeHand。
    /// </summary>
    private bool MatchesContinuousScope(ContinuousEffect effect, int targetSide, CardInstance card)
    {
        var scope = effect.Scope;
        int sourceSide = ContinuousSourceSide(effect);
        if (sourceSide >= 0 && scope.Side >= 0)
        {
            int expectedSide = scope.Side == 0 ? sourceSide : 1 - sourceSide;
            if (targetSide != expectedSide) return false;
        }

        if (targetSide < 0 || targetSide >= Players.Length) return false;
        var target = Players[targetSide];
        if (ReferenceEquals(target.Leader, card))
            return scope.IncludeLeader && (scope.Filter?.Invoke(card) ?? true);
        if (target.Characters.Contains(card))
            return scope.IncludeCharacters && (scope.Filter?.Invoke(card) ?? true);
        if (target.Hand.Contains(card))
            return scope.IncludeHand && (scope.Filter?.Invoke(card) ?? true);

        // 持续效果目前不定义舞台、卡组、生命区或废弃区作用范围。
        return false;
    }

    /// <summary>供战斗等独立流程统一判断持续效果是否可作用于目标。</summary>
    public bool IsContinuousEffectApplicable(ContinuousEffect effect, int targetSide, CardInstance card)
        => IsContinuousEffectActive(effect)
           && MatchesContinuousScope(effect, targetSide, card)
           && effect.Predicate(this, targetSide, card);

    private int ContinuousSourceSide(ContinuousEffect effect)
    {
        if (effect.SourceCardId.Length < 36 || !Guid.TryParse(effect.SourceCardId[..36], out var sourceId))
            return -1;

        for (int side = 0; side < Players.Length; side++)
        {
            var player = Players[side];
            if (player.Leader.Id == sourceId || player.StageCard?.Id == sourceId
                || player.Characters.Any(card => card.Id == sourceId)
                || player.Hand.Any(card => card.Id == sourceId)
                || player.Deck.Any(card => card.Id == sourceId)
                || player.LifeArea.Any(card => card.Id == sourceId)
                || player.Trash.Any(card => card.Id == sourceId))
                return side;
        }
        return -1;
    }

    /// <summary>
    /// 持续效果仍由来源卡的效果产生。来源角色被“效果无效”后，它提供的费用、力量、关键词及限制必须一并停止。
    /// 带后缀的来源标识（如“{card-id}-oppnullify”）也按前 36 位 Guid 定位原卡。
    /// </summary>
    private bool IsContinuousEffectActive(ContinuousEffect effect)
        => IsContinuousEffectActive(effect, new HashSet<Guid>());

    private bool IsContinuousEffectActive(ContinuousEffect effect, HashSet<Guid> evaluatingSources)
    {
        if (effect.SourceCardId.Length < 36 || !Guid.TryParse(effect.SourceCardId[..36], out var sourceId))
            return true;

        foreach (var player in Players)
        {
            var source = player.Leader.Id == sourceId
                ? player.Leader
                : player.StageCard?.Id == sourceId
                    ? player.StageCard
                    : player.Characters.FirstOrDefault(card => card.Id == sourceId);
            if (source is null) continue;
            if (source.IsEffectsNullified) return false;

            // 持续无效也必须停用该卡已注册的力量、费用、关键词等光环。
            // 评估无效光环自身时可能形成循环引用，用来源卡集合截断递归。
            if (!evaluatingSources.Add(sourceId)) return true;
            try
            {
                int sourceSide = SideOf(source);
                foreach (var nullifier in ContinuousEffects)
                {
                    if (!nullifier.NullifyEffect || ReferenceEquals(nullifier, effect)) continue;
                    if (!IsContinuousEffectActive(nullifier, evaluatingSources)) continue;
                    if (!MatchesContinuousScope(nullifier, sourceSide, source)) continue;
                    if (nullifier.Predicate(this, sourceSide, source)) return false;
                }
            }
            finally
            {
                evaluatingSources.Remove(sourceId);
            }
            return true;
        }
        return true;
    }

    /// <summary>统一计算某张卡当前费用：基础 + 一次性修正 + 永续修正 + 持续光环，最低 0</summary>
    public int CurrentCostOf(int sideIdx, CardInstance card)
    {
        int raw = card.Info.Cost + card.CostModThisTurn + card.CostModPersistent
                  + ContinuousCostBonus(sideIdx, card);
        return raw < 0 ? 0 : raw;
    }

    /// <summary>
    /// 计算当前费用，但排除指定来源的持续费用修正。用于持续效果以当前费用筛选自身作用对象，
    /// 防止该效果在判断条件时再次递归求值自身。
    /// </summary>
    public int CurrentCostOfExcludingSource(int sideIdx, CardInstance card, string sourceCardId)
    {
        int raw = card.Info.Cost + card.CostModThisTurn + card.CostModPersistent
                  + ContinuousCostBonus(sideIdx, card, sourceCardId);
        return raw < 0 ? 0 : raw;
    }

    /// <summary>
    /// 按卡自动定位其所属一方再计算当前费用（含持续光环）。
    /// 卡须在场上（角色区/领袖/舞台）；不在场上时回退为自身费用（无光环）。
    /// </summary>
    public int CurrentCostOf(CardInstance card)
    {
        for (int s = 0; s < 2; s++)
        {
            var pl = Players[s];
            if (pl.Characters.Contains(card)
                || ReferenceEquals(pl.Leader, card)
                || ReferenceEquals(pl.StageCard, card))
                return CurrentCostOf(s, card);
        }
        return card.CurrentCost();
    }

    /// <summary>双方都完成 Mulligan 后此值变 true，进入第一回合</summary>
    public bool MulliganBothDone => Players[0].MulliganDone && Players[1].MulliganDone;

    /// <summary>调度手牌阶段的服务端权威截止时间；null 表示当前不在调度阶段。</summary>
    public DateTime? MulliganDeadlineUtc { get; set; }

    public PlayerState Me(int idx)  => Players[idx];
    public PlayerState Op(int idx)  => Players[1 - idx];
    public PlayerState Turn        => Players[CurrentTurnPlayer];
    public PlayerState NonTurn     => Players[1 - CurrentTurnPlayer];
}

/// <summary>延迟到回合结束执行的任务</summary>
public class EndTurnTask
{
    public required string Kind { get; init; }      // 如 "TrashFilm"、"RefreshOwnDon"、"ReturnSelfToHand"
    public string? SourceCardId { get; init; }
    public int Owner { get; init; }
    public int Count { get; init; } = 1;
}

/// <summary>延迟到"下个对方主要阶段开始时"执行的任务（PRB02-005 路飞）</summary>
public class NextOppMainPhaseTask
{
    public required string Kind { get; init; }      // 如 "RestOneActiveDon"
    public int Owner { get; init; }                 // 登记方
    public string? SourceCardId { get; init; }
}

/// <summary>一次性"下次从手牌登场满足条件的角色减费"（OP02-025）。本回合内首个匹配的登场消费一次。</summary>
public class OneShotPlayDiscount
{
    public int Owner { get; init; }            // 受益玩家
    public int Amount { get; init; }           // 减费量（正数）
    public int MinCost { get; init; }          // 原本费用≥MinCost 才适用
    public string? Keyword { get; init; }      // 需含的特征（null=不限）
    public string? Kind { get; init; }         // 需为某类（"Character" 等，null=不限）
    public string? NameContains { get; init; } // 卡名需包含此子串（null=不限；用于"下次登场的某名角色-N"，如OP12-061）

    public bool Matches(int playerIdx, CardInstance card)
    {
        if (playerIdx != Owner) return false;
        if (card.Info.Cost < MinCost) return false;
        if (Keyword is not null && !card.Info.HasKeyword(Keyword)) return false;
        if (Kind == "Character" && card.Info.Kind != Cards.CardKind.Character) return false;
        if (NameContains is not null && !card.Info.NameContains(NameContains)) return false;  // 含"视为卡名"别名(EB04-038)
        return true;
    }
}

/// <summary>待派发的反应式 watcher 事件</summary>
public class PendingWatcher
{
    public required Effects.EffectTrigger Trigger { get; init; }
    public Dictionary<string, object?> Payload { get; init; } = new();
}

/// <summary>待定向结算的【KO时】效果及其原始效果 KO 上下文。</summary>
public class PendingKOEffect
{
    public required int Owner { get; init; }
    public required CardInstance Card { get; init; }
    public required int ActingSide { get; init; }
    public Guid? SourceCardId { get; init; }
}

/// <summary>待触发【登场时】的"被效果登场"卡牌</summary>
public class PendingEnterField
{
    public required int Owner { get; init; }
    public required Guid CardId { get; init; }
    /// <summary>来源区("hand"/"trash"/"deck"/"life")，供 OnAllyCharEnter 监听卡区分来源</summary>
    public string? From { get; init; }
    /// <summary>使其登场的效果源类型；普通从手牌打出时为空。</summary>
    public Cards.CardKind? EffectSourceKind { get; init; }
    /// <summary>使其登场的效果源卡号；普通从手牌打出时为空。</summary>
    public string? EffectSourceNumber { get; init; }
}

/// <summary>检索/公开牌的瞬时信息：哪一方公开了哪些卡号</summary>
public class RevealInfo
{
    public required int OwnerIndex { get; init; }
    public List<string> CardNumbers { get; init; } = new();
}

public class PendingPrompt
{
    public required string PromptId      { get; init; }
    public required int    PlayerIndex   { get; init; }   // 等待哪一方响应
    public required string Kind          { get; init; }
    /// <summary>合法选项的 ID 列表（卡 GUID 字符串等）</summary>
    public List<string> ValidChoices     { get; init; } = new();
    public int    MinChoose              { get; init; }
    public int    MaxChoose              { get; init; } = 1;
    public string PromptText             { get; init; } = "";
    /// <summary>用于服务端续接逻辑的回调标识（不下发客户端）</summary>
    public string ResumeKey              { get; init; } = "";
    /// <summary>额外参数（如选项列表的文本描述）</summary>
    public Dictionary<string, object?> Extra { get; init; } = new();
}

public class BattleContext
{
    public required int AttackerPlayerIndex { get; init; }
    /// <summary>攻击者卡实例 ID</summary>
    public required Guid AttackerCardId { get; init; }
    /// <summary>目标：领袖 → null（targetIsLeader=true），角色 → 该卡 ID</summary>
    public Guid? TargetCardId { get; set; }
    public bool TargetIsLeader { get; set; }
    public int DefenderPlayerIndex { get; init; }

    /// <summary>被【阻挡者】替换后的攻击目标 ID（若发生）</summary>
    public Guid? ReplacedByBlockerCardId { get; set; }

    /// <summary>本次战斗已使用过的反击（手牌中事件卡）和反击触发次数</summary>
    public List<Guid> CountersUsed { get; } = new();

    /// <summary>是否已宣言【阻挡者】（每次战斗仅 1 次）</summary>
    public bool BlockerDeclared { get; set; }

    /// <summary>当前战斗的临时威力修正（双方都用此场地）</summary>
    public int AttackerBattleBonus { get; set; }
    public int DefenderBattleBonus { get; set; }
}
