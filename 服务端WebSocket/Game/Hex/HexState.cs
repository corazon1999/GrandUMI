namespace GrandUMI.Game.Hex;

public enum HexDraftResumePoint
{
    None,
    StartFirstTurn,
    AdvanceToNextTurn,
}

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
    public bool Completed { get; set; }
}

/// <summary>
/// 一轮私密选秀从本人锁定到全部“获得时”效果完成的持久化前向结算记录。
/// 根海克斯先写入 Owned，再按 Grants 顺序推进；H47 产生的子授予也追加到同一队列。
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
    public int DraftSequence { get; set; }
    /// <summary>双方共享的第 1/3/6 个自己回合选秀品质；长度恒为 3，允许重复。</summary>
    public List<HexTier> DraftTierSequence { get; } = new();
    public HexDraftRound? ActiveDraft { get; set; }
    public bool DraftResolving { get; set; }
    public HexDraftSettlement? PendingSettlement { get; set; }
    public HexDraftResumePoint ResumePoint { get; set; }
    public List<int>[] Owned { get; } = [new(), new()];
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
