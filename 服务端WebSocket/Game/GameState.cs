namespace GrandUMI.Game;

/// <summary>
/// 完整对局状态。引擎所有操作都在此对象上进行。
/// </summary>
public class GameState
{
    public required string RoomId { get; init; }

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

    /// <summary>第一回合的先攻方索引</summary>
    public int FirstPlayer { get; init; }

    public int TurnCount { get; set; } = 1;

    public Phase Phase { get; set; } = Phase.Reset;

    /// <summary>等待玩家选择（响应 Prompt）时不为 null</summary>
    public PendingPrompt? PendingPrompt { get; set; }

    /// <summary>检索/公开牌时短暂下发给双方展示，仅在那一次公开广播时非空（即设即清，不入存档）</summary>
    public RevealInfo? PendingReveal { get; set; }

    /// <summary>当前正在进行的战斗（仅 BattleAttack/Block/Counter/Damage 阶段非 null）</summary>
    public BattleContext? CurrentBattle { get; set; }

    /// <summary>胜负已分时为非空，且 IsGameOver = true</summary>
    public int? WinnerIndex { get; set; }
    public string? GameOverReason { get; set; }
    public bool IsGameOver => WinnerIndex.HasValue;

    /// <summary>序号（每次状态变化 +1，便于客户端识别快照新旧）</summary>
    public int Tick { get; set; }

    /// <summary>
    /// PreKO 触发期间共享的"已拦截 KO"集合。
    /// BattleEngine.KOCardAsync 开始前清空，触发 PreKO 后检查；
    /// 置换效果脚本通过 ctx.State.PreventKO(card) 写入。
    /// </summary>
    public HashSet<Guid> PreventKOCardIds { get; } = new();

    public void MarkPreventKO(Guid cardId) => PreventKOCardIds.Add(cardId);

    /// <summary>"代替离场使其不离场"置换守护写入的卡集合：离场路径(KO/退手牌/回卡组/置入生命)检查后取消该卡本次离场。</summary>
    public HashSet<Guid> PreventLeaveCardIds { get; } = new();
    public void MarkPreventLeave(Guid cardId) => PreventLeaveCardIds.Add(cardId);

    /// <summary>永续效果列表（来源卡离场时由 ContinuousEffectRegistry 清理）</summary>
    public List<ContinuousEffect> ContinuousEffects { get; } = new();

    /// <summary>待派发的反应式 watcher 事件队列（AtomicOps 同步入队，EffectRuntime 在最外层效果结束后异步排空）</summary>
    public List<PendingWatcher> PendingWatchers { get; } = new();

    /// <summary>待触发【登场时】的"被效果登场"卡牌队列：AtomicOps.Play*Free 同步入队，
    /// EffectRuntime 在最外层效果结束后定向解析其【登场时】并派发 OnAllyCharEnter（修：卡效登场的角色登场时不发动）。</summary>
    public List<PendingEnterField> PendingEnterFields { get; } = new();

    /// <summary>true 时本回合结束不切换玩家（"在本回合之后追加获得我方的回合"）</summary>
    public bool ExtraTurnPending { get; set; }

    /// <summary>本回合无法登场角色卡牌的玩家集合（OP14-020）</summary>
    public HashSet<int> NoPlayCharacterThisTurn { get; } = new();

    /// <summary>本回合无法通过"我方的效果"将生命卡牌加入手牌的玩家集合（ST15-001）</summary>
    public HashSet<int> NoEffectLifeToHandThisTurn { get; } = new();

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

    /// <summary>入队一张"被效果登场"的卡牌（由 AtomicOps.Play*Free 调用），稍后定向触发其【登场时】效果。
    /// from = 来源区("hand"/"trash"/"deck"/"life")，供 OnAllyCharEnter 监听卡区分（如 OP16-079 仅废弃区登场赋速攻）。</summary>
    public void EnqueueEnterField(int owner, CardInstance card, string? from = null)
        => PendingEnterFields.Add(new PendingEnterField { Owner = owner, CardId = card.Id, From = from });

    /// <summary>评估指定卡当前从 ContinuousEffects 获得的总力量加成</summary>
    public int ContinuousPowerBonus(int sideIdx, CardInstance card)
    {
        int sum = 0;
        foreach (var eff in ContinuousEffects)
        {
            // 先按维度短路再调谓词：零力量增量的效果对力量无贡献，求值它的谓词不仅浪费、
            // 还会引发递归（如 OP16-017 力量谓词内查费用，若此处不跳过则费用查询又回调它）。
            if (eff.PowerDelta == 0) continue;
            if (!eff.Predicate(this, sideIdx, card)) continue;
            sum += eff.PowerDelta;
        }
        return sum;
    }

    /// <summary>统一计算某张卡当前力量：基础 + 咚 + 临时修正 + 永续修正</summary>
    public int CurrentPowerOf(int sideIdx, CardInstance card)
    {
        var p = Players[sideIdx];
        int donCount = p.AttachedDonCount(card.Id);
        bool ownerTurn = CurrentTurnPlayer == sideIdx;
        int basePower = card.CurrentPower(donCount, ownerTurn);
        return basePower + ContinuousPowerBonus(sideIdx, card);
    }

    /// <summary>评估指定卡当前从 ContinuousEffects 获得的总费用修正</summary>
    public int ContinuousCostBonus(int sideIdx, CardInstance card)
    {
        int sum = 0;
        foreach (var eff in ContinuousEffects)
        {
            // 先按维度短路再调谓词：零费用增量的效果对费用无贡献，跳过其谓词求值，
            // 同时切断"力量谓词 → 查费用 → 又求值该力量谓词"的递归（OP16-017）。
            if (eff.CostDelta == 0) continue;
            if (!eff.Predicate(this, sideIdx, card)) continue;
            sum += eff.CostDelta;
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
            if (eff.GrantKeyword == kw && eff.Predicate(this, side, card)) return true;
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
            if (eff.KoGuard != "any" && eff.KoGuard != context) continue;
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
            if (eff.LeaveGuard != "any" && eff.LeaveGuard != context) continue;
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
            if (eff.NullifyEffect && eff.Predicate(this, side, card)) return true;
        return false;
    }

    /// <summary>该卡的某一类触发当前是否被持续效果选择性无效化（如仅【登场时】）</summary>
    public bool IsTriggerNullified(CardInstance card, Effects.EffectTrigger trigger)
    {
        int side = SideOf(card);
        if (side < 0) return false;
        foreach (var eff in ContinuousEffects)
            if (eff.NullifyOnlyTrigger == trigger && eff.Predicate(this, side, card)) return true;
        return false;
    }

    /// <summary>该卡当前是否被持续效果阻止在重置阶段转为活跃</summary>
    public bool IsResetPrevented(CardInstance card)
    {
        int side = SideOf(card);
        if (side < 0) return false;
        foreach (var eff in ContinuousEffects)
            if (eff.PreventReset && eff.Predicate(this, side, card)) return true;
        return false;
    }

    /// <summary>该卡当前是否被持续效果条件性施加了某限制（如条件性 CannotAttack）</summary>
    public bool HasContinuousRestriction(CardInstance card, RestrictionKind kind)
    {
        int side = SideOf(card);
        if (side < 0) return false;
        foreach (var eff in ContinuousEffects)
            if (eff.GrantRestriction == kind && eff.Predicate(this, side, card)) return true;
        return false;
    }

    /// <summary>统一计算某张卡当前费用：基础 + 一次性修正 + 永续修正 + 持续光环，最低 0</summary>
    public int CurrentCostOf(int sideIdx, CardInstance card)
    {
        int raw = card.Info.Cost + card.CostModThisTurn + card.CostModPersistent
                  + ContinuousCostBonus(sideIdx, card);
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
        if (NameContains is not null && !card.Info.Name.Contains(NameContains)) return false;
        return true;
    }
}

/// <summary>待派发的反应式 watcher 事件</summary>
public class PendingWatcher
{
    public required Effects.EffectTrigger Trigger { get; init; }
    public Dictionary<string, object?> Payload { get; init; } = new();
}

/// <summary>待触发【登场时】的"被效果登场"卡牌</summary>
public class PendingEnterField
{
    public required int Owner { get; init; }
    public required Guid CardId { get; init; }
    /// <summary>来源区("hand"/"trash"/"deck"/"life")，供 OnAllyCharEnter 监听卡区分来源</summary>
    public string? From { get; init; }
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
