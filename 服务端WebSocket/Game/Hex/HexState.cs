namespace GrandUMI.Game.Hex;

public enum HexDraftResumePoint
{
    None,
    StartFirstTurn,
    AdvanceToNextTurn,
}

/// <summary>单个候选槽位已经完成的权威刷新记录，用于幂等重试、断线恢复与动作重放。</summary>
public sealed record HexDraftRefresh(
    int CandidateIndex,
    int ReplacedHexId,
    int ReplacementHexId);

public sealed class HexDraftRound
{
    public required string RoundId { get; init; }
    public required int PlayerIndex { get; init; }
    public required int OwnTurnNumber { get; init; }
    public required HexTier Tier { get; init; }
    public required DateTime DeadlineUtc { get; set; }
    public List<int> Candidates { get; } = new();
    public int? LockedChoice { get; set; }
    public bool Locked { get; set; }
    public bool RefreshUsed { get; set; }
    public int? RefreshedCandidateIndex { get; set; }
    public int? ReplacedHexId { get; set; }
    public int? ReplacementHexId { get; set; }
    /// <summary>
    /// 修订版 3 起，每个候选槽位各自只能刷新一次；列表只在替换成功后追加，
    /// 因而既是每槽消费凭据，也是相同业务请求的幂等回执。
    /// </summary>
    public List<HexDraftRefresh> Refreshes { get; } = new();
    public bool IsComplete => Locked;
}

public sealed record ResolvedHexDraft(
    string RoundId,
    HexTier Tier,
    int PlayerIndex,
    int OwnTurnNumber,
    int Choice);

/// <summary>一项海克斯授予的可重入进度；NextStep 只在该步骤的状态变更完整完成后前移。</summary>
public sealed class HexGrantProgress
{
    public required string GrantKey { get; init; }
    public required int PlayerIndex { get; init; }
    public required int HexId { get; init; }
    public int NextStep { get; set; }
    public int PlannedStepCount { get; set; } = -1;
    /// <summary>
    /// 随机子授予按步骤预先锁定的海克斯编号；0 表示该步骤池已耗尽。
    /// 计划先于所有权和子队列写入，令同进程重试不会重新抽取或重复授予。
    /// </summary>
    public List<int> PlannedChildHexIds { get; } = new();
    public bool Completed { get; set; }
}

/// <summary>
/// 一轮私密选秀从本人锁定到全部“获得时”效果完成的持久化前向结算记录。
/// 根海克斯先写入 Owned，再按 Grants 顺序推进；随机质变产生的子授予也追加到同一队列。
/// </summary>
public sealed class HexDraftSettlement
{
    public required string RoundId { get; init; }
    public required HexTier Tier { get; init; }
    public required int PlayerIndex { get; init; }
    public required int OwnTurnNumber { get; init; }
    public required int Choice { get; init; }
    public required HexDraftResumePoint ResumePoint { get; init; }
    public bool RootOwnershipCommitted { get; set; }
    public int NextGrantIndex { get; set; }
    public List<HexGrantProgress> Grants { get; } = new();
}

internal sealed record HexGrantStepBoundary(
    string RoundId,
    string GrantKey,
    int PlayerIndex,
    int HexId,
    int CompletedStep);

/// <summary>单方海克斯运行态；所有计数均是权威状态并随动作重放恢复。</summary>
public sealed class PlayerHexRuntime
{
    /// <summary>尖端发明家已放宽过一次的“每回合1次”内部键；第二次使用后不再移除。</summary>
    public HashSet<string> InventorFirstUseKeys { get; } = new(StringComparer.Ordinal);
    public int CardsPlayedThisTurn { get; set; }
    public bool SoulSiphonUsedThisTurn { get; set; }
    public bool FirstLeaderAttackSeenThisTurn { get; set; }
    public bool FirstCharacterAttackSeenThisTurn { get; set; }
    public bool FirstEnterEffectCopiedThisTurn { get; set; }
    public bool FirstKoEffectCopiedThisTurn { get; set; }
    public int AttacksDeclaredThisTurn { get; set; }
    public int RestingCharacterAttacksThisGame { get; set; }
    public bool SteelHeartUsedThisGame { get; set; }
    public bool UltimateRefreshUsedThisTurn { get; set; }
    public bool FinalFormUsedThisTurn { get; set; }
    public bool CriticalHealSucceededThisTurn { get; set; }
    public bool EventDrawConvertedThisTurn { get; set; }
    public bool CharacterDrawConvertedThisTurn { get; set; }
    public bool SlapUsedThisTurn { get; set; }
    public bool SoulConsumeUsedThisTurn { get; set; }
    public bool TankEngineUsedThisTurn { get; set; }
    public int TankEngineOpponentTurnPower { get; set; }
    public bool NavyCarnivalUsedThisTurn { get; set; }
    public bool KingUsedThisGame { get; set; }
    /// <summary>超凡邪恶累计层数对应的己方回合领袖力量；跨回合持久且不在 ResetTurn 清除。</summary>
    public int TranscendentEvilOwnTurnPower { get; set; }

    public void ResetTurn()
    {
        InventorFirstUseKeys.Clear();
        CardsPlayedThisTurn = 0;
        SoulSiphonUsedThisTurn = false;
        FirstLeaderAttackSeenThisTurn = false;
        FirstCharacterAttackSeenThisTurn = false;
        FirstEnterEffectCopiedThisTurn = false;
        FirstKoEffectCopiedThisTurn = false;
        AttacksDeclaredThisTurn = 0;
        UltimateRefreshUsedThisTurn = false;
        FinalFormUsedThisTurn = false;
        CriticalHealSucceededThisTurn = false;
        EventDrawConvertedThisTurn = false;
        CharacterDrawConvertedThisTurn = false;
        SlapUsedThisTurn = false;
        SoulConsumeUsedThisTurn = false;
        TankEngineUsedThisTurn = false;
        NavyCarnivalUsedThisTurn = false;
    }
}

/// <summary>海克斯玩法的独立权威聚合，不依赖卡牌 DSL 或客户端本地状态。</summary>
public sealed class HexState
{
    public bool Enabled { get; set; }
    /// <summary>
    /// 对局创建时锁定的海克斯规则版本。旧房间恢复时继续使用旧池与旧效果语义，
    /// 避免部署后动作重放因候选变化或跨回合累计方式变化而分歧。
    /// </summary>
    public int RulesRevision { get; set; }
    public int DraftSequence { get; set; }
    /// <summary>双方共享的第 1/3/6 个自己回合选秀品质；长度恒为 3，允许重复。</summary>
    public List<HexTier> DraftTierSequence { get; } = new();
    public HexDraftRound? ActiveDraft { get; set; }
    public bool DraftResolving { get; set; }
    public HexDraftSettlement? PendingSettlement { get; set; }
    public HexDraftResumePoint ResumePoint { get; set; }
    public List<int>[] Owned { get; } = [new(), new()];
    /// <summary>
    /// 修订版 4 起记录由质变效果授予的真实海克斯。所有权仍由 Owned 唯一判定；
    /// 此集合只保存获得来源，供公开名称投影、断线恢复和确定性重放使用。
    /// </summary>
    public HashSet<int>[] GrantedByTransmutation { get; } = [new(), new()];
    /// <summary>
    /// 修订版 3 起按玩家独立记录整局曾向其展示过的全部候选；初始三张与刷新替换均立即写入。
    /// 不向客户端公开，只用于服务端确定性去重。
    /// </summary>
    public HashSet<int>[] Appeared { get; } = [new(), new()];
    public int[] CompletedOwnTurns { get; } = [0, 0];
    public PlayerHexRuntime[] Runtime { get; } = [new(), new()];
    public List<ResolvedHexDraft> ResolvedDrafts { get; } = new();

    /// <summary>仅测试使用的一次性故障注入点；不进入任何快照或重放投影。</summary>
    internal Action<HexGrantStepBoundary>? GrantStepFaultInjector { get; set; }

    /// <summary>私密选秀只冻结其拥有者；另一方仍由普通阶段/回合校验决定合法动作。</summary>
    public bool BlocksOrdinaryActionsFor(int playerIndex)
        => Enabled
           && playerIndex is 0 or 1
           && (ActiveDraft?.PlayerIndex == playerIndex
               || PendingSettlement?.PlayerIndex == playerIndex);
}
