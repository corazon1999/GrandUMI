using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game.PhaseFlow;

namespace GrandUMI.Game.Hex;

public enum HexChoiceStatus
{
    Locked,
    Duplicate,
    Ready,
    Rejected,
}

public sealed record HexChoiceResult(HexChoiceStatus Status, int? Choice, string? Reason = null);

public enum HexRefreshStatus
{
    Refreshed,
    Duplicate,
    Rejected,
}

public sealed record HexRefreshResult(
    HexRefreshStatus Status,
    int? CandidateIndex,
    int? ReplacedHexId,
    int? ReplacementHexId,
    string? Reason = null);

/// <summary>
/// 海克斯玩法的唯一规则入口。选秀、获取时效果、全局修正和回合计数均在此聚合，
/// 卡牌 DSL 不感知也不得修改海克斯状态。
/// </summary>
public static class HexRules
{
    public const int LegacyRulesRevision = 1;
    /// <summary>品质池、备选池与海克斯效果语义调整所在的规则修订版。</summary>
    public const int BalanceRulesRevision = 2;
    /// <summary>每槽一次刷新及按玩家整局候选去重所在的规则修订版。</summary>
    public const int PerSlotRefreshRulesRevision = 3;
    /// <summary>质变来源公开投影与黄金阶削弱所在的规则修订版。</summary>
    public const int TransmutationPresentationRulesRevision = 4;
    /// <summary>对局锁定管理员发布的完整品质目录所在的规则修订版。</summary>
    public const int CatalogConfigurationRulesRevision = 5;
    /// <summary>星界躯体改为手牌补1张生命后，再从卡组顶补1张生命所在的规则修订版。</summary>
    public const int AstralBodyRulesRevision = 6;
    /// <summary>登舰礼炮改为每回合第2个实际发动的登场时效果额外结算所在的规则修订版。</summary>
    public const int BoardingSalvoRulesRevision = 7;
    /// <summary>万用瞄准镜改为角色攻击时获得本次战斗力量，强化版退出常规池所在的规则修订版。</summary>
    public const int ScopeReworkRulesRevision = 8;
    /// <summary>终极刷新改为打出原本费用 10 的卡后最多活跃 8 张休息咚所在的规则修订版。</summary>
    public const int UltimateRefreshRulesRevision = 9;
    public const int CurrentRulesRevision = UltimateRefreshRulesRevision;
    public const int DraftTimeoutSeconds = 60;
    public static readonly int[] DraftOwnTurns = [1, 3, 6];
    private static readonly HexTier[] AvailableTiers = [HexTier.Silver, HexTier.Gold, HexTier.Rainbow];
    private static readonly RandomGrantPlan[] ChaosGrantPlans =
    [
        new(null, "hex_chaos_grant", "chaos"),
        new(null, "hex_chaos_grant", "chaos"),
    ];
    private static readonly RandomGrantPlan[] LegacyGoldenTierGrantPlans =
    [
        new(HexTier.Silver, "hex_golden_tier_grant", "golden-tier"),
        new(HexTier.Gold, "hex_golden_tier_grant", "golden-tier"),
    ];
    private static readonly RandomGrantPlan[] GoldenTierGrantPlans =
    [
        new(HexTier.Gold, "hex_golden_tier_grant", "golden-tier"),
    ];
    private static readonly RandomGrantPlan[] PrismaticTierGrantPlans =
    [
        new(HexTier.Rainbow, "hex_prismatic_tier_grant", "prismatic-tier"),
    ];

    public static void Initialize(GameState state, bool hexMode = false)
    {
        state.HexState.Enabled = hexMode || state.MatchKind == MatchKind.Hex;
        state.HexState.RulesRevision = CurrentRulesRevision;
        if (state.HexState.Enabled)
            ApplyCatalogSnapshot(state.HexState, HexCatalogRuntime.SnapshotForNewRoom());
    }

    /// <summary>仅恢复/重放入口可覆盖对局创建时锁定的规则版本。</summary>
    internal static void SetRulesRevisionForReplay(GameState state, int rulesRevision)
    {
        if (rulesRevision is < LegacyRulesRevision or > CurrentRulesRevision)
            throw new InvalidDataException($"不支持的海克斯规则版本：{rulesRevision}");
        state.HexState.RulesRevision = rulesRevision;
        state.HexState.CatalogRevision = 0;
        state.HexState.CatalogDigest = string.Empty;
        state.HexState.CatalogTiers.Clear();
        if (state.HexState.Enabled && rulesRevision >= CatalogConfigurationRulesRevision)
            ApplyCatalogSnapshot(state.HexState, HexCatalogConfiguration.BuiltIn);
    }

    /// <summary>恢复入口在重放任何随机事件之前覆盖建局时锁定的完整目录。</summary>
    internal static void SetCatalogForReplay(GameState state, HexCatalogConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!state.HexState.Enabled) return;
        if (state.HexState.RulesRevision < CatalogConfigurationRulesRevision)
            throw new InvalidDataException("旧版海克斯房间不能附加动态目录配置。");
        ApplyCatalogSnapshot(state.HexState, configuration);
    }

    private static void ApplyCatalogSnapshot(HexState state, HexCatalogConfiguration configuration)
    {
        state.CatalogRevision = configuration.Revision;
        state.CatalogDigest = configuration.Digest;
        state.CatalogTiers.Clear();
        foreach (var assignment in configuration.Assignments)
            state.CatalogTiers.Add(assignment.Id, assignment.Tier);
    }

    /// <summary>一次性生成双方共享的三段品质。使用本局 RNG，故重放和进程恢复会逐字重建。</summary>
    public static void EnsureDraftTierSequence(GameState state)
    {
        if (!state.HexState.Enabled) return;
        while (state.HexState.DraftTierSequence.Count < DraftOwnTurns.Length)
        {
            int slot = state.HexState.DraftTierSequence.Count;
            int index = state.NextRecordedRandom(
                AvailableTiers.Length,
                "hex_draft_tier",
                -1,
                new { slot, ownTurn = DraftOwnTurns[slot] });
            state.HexState.DraftTierSequence.Add(AvailableTiers[index]);
        }
    }

    public static bool Has(GameState state, int playerIndex, int hexId)
        => state.HexState.Enabled
           && playerIndex is 0 or 1
           && state.HexState.Owned[playerIndex].Contains(hexId);

    public static bool IsVisibleOwnedHex(GameState state, int hexId)
        => state.HexState.RulesRevision < TransmutationPresentationRulesRevision
           || !HexCatalog.IsTransmutation(hexId);

    public static bool WasGrantedByTransmutation(GameState state, int playerIndex, int hexId)
        => state.HexState.RulesRevision >= TransmutationPresentationRulesRevision
           && playerIndex is 0 or 1
           && state.HexState.GrantedByTransmutation[playerIndex].Contains(hexId);

    public static HexDraftRound StartDraft(
        GameState state,
        int playerIndex,
        int ownTurnNumber,
        HexDraftResumePoint resumePoint,
        DateTime? deadlineUtc = null)
    {
        if (!state.HexState.Enabled) throw new InvalidOperationException("当前不是海克斯模式");
        if (playerIndex is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(playerIndex));
        if (state.HexState.ActiveDraft is not null
            || state.HexState.DraftResolving
            || state.HexState.PendingSettlement is not null)
            throw new InvalidOperationException("已有海克斯选秀正在进行");

        EnsureDraftTierSequence(state);
        int draftSlot = Array.IndexOf(DraftOwnTurns, ownTurnNumber);
        if (draftSlot < 0) throw new InvalidOperationException($"玩家第 {ownTurnNumber} 回合不应触发海克斯选秀");
        if (state.HexState.ResolvedDrafts.Any(item =>
                item.PlayerIndex == playerIndex && item.OwnTurnNumber == ownTurnNumber))
            throw new InvalidOperationException("该玩家本回合前的海克斯选秀已经完成");
        var tier = state.HexState.DraftTierSequence[draftSlot];

        // 先完整生成候选再提交轮次编号与活动选秀，候选不足时不留下半成品轮次。
        var candidates = DrawCandidates(state, playerIndex, tier, 3);

        int sequence = ++state.HexState.DraftSequence;
        var round = new HexDraftRound
        {
            RoundId = $"hex-{sequence}-p{playerIndex}-t{ownTurnNumber}-{tier.ToString().ToLowerInvariant()}",
            PlayerIndex = playerIndex,
            OwnTurnNumber = ownTurnNumber,
            Tier = tier,
            DeadlineUtc = deadlineUtc ?? DateTime.UtcNow.AddSeconds(DraftTimeoutSeconds),
        };
        round.Candidates.AddRange(candidates);

        state.HexState.ActiveDraft = round;
        state.HexState.ResumePoint = resumePoint;
        state.OnDeterministicRandomEvent?.Invoke("hex_draft_candidates", -1, new
        {
            roundId = round.RoundId,
            playerIndex,
            ownTurnNumber,
            tier = tier.ToString(),
            candidates = round.Candidates.ToArray(),
            deadlineUtc = round.DeadlineUtc,
        });
        return round;
    }

    public static HexChoiceResult LockChoice(
        GameState state,
        int playerIndex,
        string roundId,
        int? requestedChoice,
        bool automatic)
    {
        if (!state.HexState.Enabled || playerIndex is < 0 or > 1)
            return new(HexChoiceStatus.Rejected, null, "当前对局不支持海克斯选择");

        var round = state.HexState.ActiveDraft;
        if (round is null || !string.Equals(round.RoundId, roundId, StringComparison.Ordinal))
        {
            var resolved = state.HexState.ResolvedDrafts.LastOrDefault(item =>
                item.RoundId == roundId && item.PlayerIndex == playerIndex);
            int? previous = resolved?.Choice;
            return previous.HasValue && requestedChoice == previous
                ? new(HexChoiceStatus.Duplicate, previous)
                : new(HexChoiceStatus.Rejected, null, "海克斯选秀轮次已过期");
        }

        if (round.PlayerIndex != playerIndex)
            return new(HexChoiceStatus.Rejected, null, "这不是你的私密海克斯选秀");

        if (round.Locked)
        {
            int previous = round.LockedChoice!.Value;
            return (!automatic && requestedChoice == previous)
                ? new(HexChoiceStatus.Duplicate, previous)
                : new(HexChoiceStatus.Rejected, null, "本轮海克斯已经锁定");
        }

        int choice;
        if (automatic)
        {
            int candidateIndex = state.NextRecordedRandom(
                round.Candidates.Count,
                "hex_timeout_auto_choice",
                playerIndex,
                new { roundId, tier = round.Tier.ToString() });
            choice = round.Candidates[candidateIndex];
        }
        else if (requestedChoice is not int requested
                 || !round.Candidates.Contains(requested))
        {
            return new(HexChoiceStatus.Rejected, null, "所选海克斯不在本轮私密候选中");
        }
        else
        {
            choice = requested;
        }

        round.LockedChoice = choice;
        round.Locked = true;
        return new(HexChoiceStatus.Ready, choice);
    }

    public static HexRefreshResult RefreshCandidate(
        GameState state,
        int playerIndex,
        string roundId,
        int candidateIndex,
        int expectedHexId)
    {
        if (!state.HexState.Enabled || playerIndex is < 0 or > 1)
            return new(HexRefreshStatus.Rejected, null, null, null, "当前对局不支持海克斯刷新");

        var round = state.HexState.ActiveDraft;
        if (round is null || !string.Equals(round.RoundId, roundId, StringComparison.Ordinal))
            return new(HexRefreshStatus.Rejected, null, null, null, "海克斯选秀轮次已过期");
        if (round.PlayerIndex != playerIndex)
            return new(HexRefreshStatus.Rejected, null, null, null, "这不是你的私密海克斯选秀");

        bool perSlotRefresh = state.HexState.RulesRevision >= PerSlotRefreshRulesRevision;
        if (perSlotRefresh)
        {
            if (candidateIndex < 0 || candidateIndex >= round.Candidates.Count)
                return new(HexRefreshStatus.Rejected, null, null, null, "刷新候选位置无效");

            var completed = round.Refreshes.FirstOrDefault(item => item.CandidateIndex == candidateIndex);
            if (completed is not null)
            {
                return completed.ReplacedHexId == expectedHexId
                    ? new(
                        HexRefreshStatus.Duplicate,
                        completed.CandidateIndex,
                        completed.ReplacedHexId,
                        completed.ReplacementHexId)
                    : new(HexRefreshStatus.Rejected, null, null, null, "该候选槽位的刷新机会已经使用");
            }

            if (round.Locked)
                return new(HexRefreshStatus.Rejected, null, null, null, "本轮海克斯已经锁定");
        }
        else
        {
            if (round.Locked)
                return new(HexRefreshStatus.Rejected, null, null, null, "本轮海克斯已经锁定");

            if (round.RefreshUsed)
            {
                bool sameRequest = round.RefreshedCandidateIndex == candidateIndex
                    && round.ReplacedHexId == expectedHexId;
                return sameRequest
                    ? new(HexRefreshStatus.Duplicate, candidateIndex, round.ReplacedHexId, round.ReplacementHexId)
                    : new(HexRefreshStatus.Rejected, null, null, null, "本轮唯一一次刷新机会已经使用");
            }
        }

        if (candidateIndex < 0 || candidateIndex >= round.Candidates.Count)
            return new(HexRefreshStatus.Rejected, null, null, null, "刷新候选位置无效");
        if (round.Candidates[candidateIndex] != expectedHexId)
            return new(HexRefreshStatus.Rejected, null, null, null, "待刷新候选已变化，请以最新快照为准");

        var pool = HexCatalog.ForTier(round.Tier, state.HexState)
            .Select(item => item.Id)
            .Where(id => !state.HexState.Owned[playerIndex].Contains(id))
            .Where(id => !round.Candidates.Contains(id))
            .Where(id => !perSlotRefresh || !state.HexState.Appeared[playerIndex].Contains(id))
            .ToList();
        if (pool.Count == 0)
            return new(HexRefreshStatus.Rejected, null, null, null, "当前品质没有未出现过的可用替换候选");

        int replacementIndex = state.NextRecordedRandom(
            pool.Count,
            "hex_draft_refresh_candidate",
            playerIndex,
            new { roundId, candidateIndex, replacedHexId = expectedHexId });
        int replacementHexId = pool[replacementIndex];
        round.Candidates[candidateIndex] = replacementHexId;
        round.RefreshUsed = true;
        round.RefreshedCandidateIndex = candidateIndex;
        round.ReplacedHexId = expectedHexId;
        round.ReplacementHexId = replacementHexId;
        if (perSlotRefresh)
        {
            round.Refreshes.Add(new HexDraftRefresh(candidateIndex, expectedHexId, replacementHexId));
            state.HexState.Appeared[playerIndex].Add(replacementHexId);
        }
        return new(HexRefreshStatus.Refreshed, candidateIndex, expectedHexId, replacementHexId);
    }

    public static IReadOnlyList<int> RefreshedCandidateIndices(HexDraftRound round, int rulesRevision)
        => rulesRevision >= PerSlotRefreshRulesRevision
            ? round.Refreshes.Select(item => item.CandidateIndex).Order().ToArray()
            : round.RefreshedCandidateIndex is int index ? [index] : [];

    public static int RefreshRemaining(HexDraftRound round, int rulesRevision)
    {
        if (round.Locked) return 0;
        return rulesRevision >= PerSlotRefreshRulesRevision
            ? Math.Max(0, round.Candidates.Count - round.Refreshes.Count)
            : round.RefreshUsed ? 0 : 1;
    }

    public static bool RefreshAvailableForCandidate(
        HexDraftRound round,
        int candidateIndex,
        int rulesRevision)
    {
        if (round.Locked || candidateIndex < 0 || candidateIndex >= round.Candidates.Count) return false;
        return rulesRevision >= PerSlotRefreshRulesRevision
            ? round.Refreshes.All(item => item.CandidateIndex != candidateIndex)
            : !round.RefreshUsed;
    }

    /// <summary>把当前玩家已锁定结果转为已拥有，并用持久化步骤游标结算获取时效果。</summary>
    public static async Task<(ResolvedHexDraft Draft, HexDraftResumePoint Resume)> ResolveDraftAsync(GameEngine engine)
    {
        var state = engine.State;
        var settlement = state.HexState.PendingSettlement;
        if (settlement is null)
        {
            var round = state.HexState.ActiveDraft;
            if (round is null || !round.IsComplete)
                throw new InvalidOperationException("海克斯选秀尚未完成");

            settlement = new HexDraftSettlement
            {
                RoundId = round.RoundId,
                Tier = round.Tier,
                PlayerIndex = round.PlayerIndex,
                OwnTurnNumber = round.OwnTurnNumber,
                Choice = round.LockedChoice!.Value,
                ResumePoint = state.HexState.ResumePoint,
            };
            settlement.Grants.Add(new HexGrantProgress
            {
                GrantKey = $"{round.RoundId}:root:{round.PlayerIndex}",
                PlayerIndex = round.PlayerIndex,
                HexId = settlement.Choice,
            });
            state.HexState.PendingSettlement = settlement;
            state.HexState.ActiveDraft = null;

            var resolvedRecord = new ResolvedHexDraft(
                settlement.RoundId,
                settlement.Tier,
                settlement.PlayerIndex,
                settlement.OwnTurnNumber,
                settlement.Choice);
            if (state.HexState.ResolvedDrafts.All(item => item.RoundId != settlement.RoundId))
                state.HexState.ResolvedDrafts.Add(resolvedRecord);
        }

        state.HexState.DraftResolving = true;
        try
        {
            if (!settlement.RootOwnershipCommitted)
            {
                AddOwned(state, settlement.PlayerIndex, settlement.Choice);
                settlement.RootOwnershipCommitted = true;
            }

            while (settlement.NextGrantIndex < settlement.Grants.Count && !state.IsGameOver)
            {
                var grant = settlement.Grants[settlement.NextGrantIndex];
                AddOwned(state, grant.PlayerIndex, grant.HexId);
                await ApplyDraftGrantAsync(engine, settlement, grant);
                if (!grant.Completed) continue;
                settlement.NextGrantIndex++;
            }

            var resolved = new ResolvedHexDraft(
                settlement.RoundId,
                settlement.Tier,
                settlement.PlayerIndex,
                settlement.OwnTurnNumber,
                settlement.Choice);
            var resume = settlement.ResumePoint;
            state.HexState.PendingSettlement = null;
            state.HexState.DraftResolving = false;
            state.HexState.ResumePoint = HexDraftResumePoint.None;
            engine.RecordMatchLog("hex_draft_resolved", -1, new
            {
                roundId = resolved.RoundId,
                tier = resolved.Tier.ToString(),
                player = resolved.PlayerIndex,
                ownTurnNumber = resolved.OwnTurnNumber,
                choice = resolved.Choice,
            });
            return (resolved, resume);
        }
        catch
        {
            // 结算记录与步骤游标保留供同进程重试或重启重放；运行标志不得永久卡死。
            state.HexState.DraftResolving = false;
            throw;
        }
    }

    public static int PowerBonus(GameState state, int side, CardInstance card)
    {
        if (!state.HexState.Enabled || side is < 0 or > 1) return 0;
        var player = state.Players[side];
        bool leader = ReferenceEquals(player.Leader, card);
        bool character = player.Characters.Contains(card);
        if (!leader && !character) return 0;

        int bonus = 0;
        if (character && Has(state, side, 1) && card.Info.Power >= 8000) bonus += 2000;
        if (Has(state, side, 11) && state.CurrentTurnPlayer == side)
            bonus += player.AttachedDonCount(card.Id) * 1000;
        if (Has(state, side, 12)) bonus += 1000;
        if (character && Has(state, side, 18)
            && player.Characters.Count(other => other.Info.Number == card.Info.Number) == 2)
            bonus += 3000;
        if (leader && Has(state, side, 20) && state.CurrentTurnPlayer == side) bonus += 2000;
        if (leader
            && state.HexState.RulesRevision >= BalanceRulesRevision
            && Has(state, side, 22)
            && state.CurrentTurnPlayer == side)
            bonus += state.HexState.Runtime[side].TranscendentEvilOwnTurnPower;
        if (leader && Has(state, side, 34)) bonus += 2000;
        if (character && Has(state, side, 38)) bonus += 1000;
        if (leader && Has(state, side, 44) && state.CurrentTurnPlayer == 1 - side)
            bonus += state.HexState.Runtime[side].TankEngineOpponentTurnPower;
        return bonus;
    }

    public static int HandCostDelta(GameState state, int playerIndex, CardInstance card)
    {
        if (!state.HexState.Enabled) return 0;
        int delta = 0;
        if (card.Info.Kind == CardKind.Character && Has(state, playerIndex, 36)) delta--;
        if (card.Info.Kind == CardKind.Event && Has(state, playerIndex, 37)) delta--;
        if (card.Info.Kind == CardKind.Event && Has(state, playerIndex, 39)) delta -= 2;
        return delta;
    }

    public static int AdjustFinalHandCost(GameState state, int playerIndex, CardInstance card, int cost)
    {
        int normalized = Math.Max(0, cost);
        return card.Info.Kind == CardKind.Event && Has(state, playerIndex, 46)
            ? checked(normalized * 2)
            : normalized;
    }

    public static int CounterBonus(GameState state, int playerIndex, CardInstance card)
    {
        if (card.Info.Kind == CardKind.Character && Has(state, playerIndex, 12)) return 1000;
        if (card.Info.Kind == CardKind.Event && Has(state, playerIndex, 51)) return 2000;
        return 0;
    }

    public static bool CanRest(GameState state, CardInstance card, int? prospectiveOwner = null)
        => !Has(state, 0, 19) && !Has(state, 1, 19)
           || card.Info.Kind != CardKind.Character
           || (prospectiveOwner is int owner
               ? state.CurrentPowerOf(owner, card)
               : state.CurrentPowerOf(card)) > 5000;

    public static void OnTurnStarted(GameState state, int playerIndex)
    {
        if (!state.HexState.Enabled) return;
        // “每回合1次”按整场玩家回合边界刷新，防守方在对方回合触发的海克斯也必须获得新次数。
        foreach (var runtime in state.HexState.Runtime) runtime.ResetTurn();

        if (Has(state, playerIndex, 33))
        {
            var targets = state.Players[1 - playerIndex].Characters;
            if (targets.Count > 0)
            {
                int index = state.NextRecordedRandom(targets.Count, "hex_boomerang_target", playerIndex,
                    new { turnCount = state.TurnCount });
                targets[index].PowerModThisTurn -= 2000;
            }
        }
    }

    public static void OnTurnEnding(GameState state, int playerIndex)
    {
        if (!state.HexState.Enabled) return;
        if (Has(state, playerIndex, 40))
            foreach (var card in state.Players[1 - playerIndex].Characters.Where(card => !card.IsTapped))
                card.PowerModPersistent -= 1000;
        state.HexState.CompletedOwnTurns[playerIndex]++;
    }

    public static bool ShouldStartDraftBeforeTurn(
        GameState state,
        int playerIndex,
        out int ownTurnNumber,
        out HexTier tier)
    {
        ownTurnNumber = 0;
        tier = default;
        if (!state.HexState.Enabled
            || playerIndex is < 0 or > 1
            || state.HexState.ActiveDraft is not null
            || state.HexState.DraftResolving
            || state.HexState.PendingSettlement is not null)
            return false;
        EnsureDraftTierSequence(state);
        int nextOwnTurnNumber = state.HexState.CompletedOwnTurns[playerIndex] + 1;
        ownTurnNumber = nextOwnTurnNumber;
        int slot = Array.IndexOf(DraftOwnTurns, nextOwnTurnNumber);
        if (slot < 0
            || state.HexState.ResolvedDrafts.Any(round =>
                round.PlayerIndex == playerIndex && round.OwnTurnNumber == nextOwnTurnNumber))
            return false;
        tier = state.HexState.DraftTierSequence[slot];
        return true;
    }

    public static async Task OnAttackDeclaredAsync(GameEngine engine, int attackerSide)
    {
        var state = engine.State;
        if (!state.HexState.Enabled || state.CurrentBattle is not { } battle) return;
        var player = state.Players[attackerSide];
        var opponent = state.Players[1 - attackerSide];
        var attacker = player.Leader.Id == battle.AttackerCardId
            ? player.Leader
            : player.Characters.FirstOrDefault(card => card.Id == battle.AttackerCardId);
        if (attacker is null) return;
        var runtime = state.HexState.Runtime[attackerSide];
        runtime.AttacksDeclaredThisTurn++;

        if (state.HexState.RulesRevision >= ScopeReworkRulesRevision
            && Has(state, attackerSide, 26)
            && attacker.Info.Kind == CardKind.Character)
            attacker.PowerModThisBattle += 1000;

        if (Has(state, attackerSide, 14))
            foreach (var card in player.Hand.Where(card => card.Info.Kind == CardKind.Event))
                card.CostModThisTurn--;

        if (!battle.TargetIsLeader && battle.TargetCardId is { } targetId
            && opponent.Characters.FirstOrDefault(card => card.Id == targetId) is { } target)
        {
            if (target.IsTapped)
            {
                runtime.RestingCharacterAttacksThisGame++;
                if (Has(state, attackerSide, 25)
                    && !runtime.SteelHeartUsedThisGame
                    && runtime.RestingCharacterAttacksThisGame >= 10)
                {
                    runtime.SteelHeartUsedThisGame = true;
                    await AddLifeFromDeckAsync(engine, attackerSide, player.LifeArea.Count, "steel_heart");
                }
            }
            bool giantSlayerTargetEligible = state.HexState.RulesRevision >= BalanceRulesRevision
                ? state.CurrentCostOf(1 - attackerSide, target) >= 8
                : target.Info.Cost >= 8;
            if (Has(state, attackerSide, 24)
                && attacker.Info.Kind == CardKind.Character
                && giantSlayerTargetEligible)
                battle.AttackerBattleBonus += 3000;
            if (Has(state, attackerSide, 50)) target.PowerModThisTurn -= 1000;
        }

        if (Has(state, attackerSide, 45))
            battle.AttackerBattleBonus += player.Characters.Count * 1000;

        if (Has(state, attackerSide, 10))
        {
            if (ReferenceEquals(attacker, player.Leader) && !runtime.FirstLeaderAttackSeenThisTurn)
            {
                runtime.FirstLeaderAttackSeenThisTurn = true;
                var rested = player.Characters.Where(card => card.IsTapped).ToList();
                if (rested.Count > 0)
                {
                    var chosen = await engine.Prompts.ChooseCards(attackerSide, "HexArchmageRefresh",
                        "大法师：选择1个己方角色转为活跃",
                        rested.Select(card => card.Id.ToString()).ToList(), 1, 1,
                        new Dictionary<string, object?>
                        {
                            ["choiceCards"] = rested.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToArray(),
                        });
                    var selected = rested.FirstOrDefault(card => chosen.Contains(card.Id.ToString()));
                    if (selected is not null) selected.IsTapped = false;
                }
            }
            else if (!ReferenceEquals(attacker, player.Leader) && !runtime.FirstCharacterAttackSeenThisTurn)
            {
                runtime.FirstCharacterAttackSeenThisTurn = true;
                player.Leader.IsTapped = false;
            }
        }
    }

    public static async Task OnCardPlayedAsync(GameEngine engine, int playerIndex, PlayResult result)
    {
        var state = engine.State;
        if (!state.HexState.Enabled) return;
        var runtime = state.HexState.Runtime[playerIndex];
        var player = state.Players[playerIndex];
        runtime.CardsPlayedThisTurn++;
        if (Has(state, playerIndex, 2) && runtime.CardsPlayedThisTurn == 3)
            TurnEngine.DrawCard(state, playerIndex, 1);

        if (result.Kind == PlayKind.Event)
        {
            if (Has(state, playerIndex, 3)) player.Leader.PowerModThisTurn += 1000;
            if (Has(state, playerIndex, 35))
                foreach (var card in player.Hand.Where(card => card.Info.Kind == CardKind.Event))
                    card.CostModThisTurn--;
        }

        if (result.Card.Info.Cost == 10)
        {
            // OnCardPlayedAsync 只由 CardPlayer.Play 成功移除手牌后调用；效果登场不会进入此钩子。
            // 终极刷新只看卡面原本费用，不看 PaidCost 或任何费用修正；每个全局回合统一重置次数。
            if (Has(state, playerIndex, 28)
                && !runtime.UltimateRefreshUsedThisTurn)
            {
                runtime.UltimateRefreshUsedThisTurn = true;
                int refreshLimit = state.HexState.RulesRevision >= UltimateRefreshRulesRevision
                    ? 8
                    : int.MaxValue;
                // Attached 是独立状态，不属于休息；AttachedToCardId 判空同时防止异常旧状态被转成非法活跃咚。
                foreach (var don in player.CostArea
                             .Where(don => don.State == DonState.Rest && don.AttachedToCardId is null)
                             .Take(refreshLimit))
                    don.State = DonState.Active;
            }
            if (Has(state, playerIndex, 29) && !runtime.FinalFormUsedThisTurn)
            {
                runtime.FinalFormUsedThisTurn = true;
                player.Leader.PowerModsUntilOppEnd.Add(new CardPowerMod { Delta = 2000, AppliedBySide = playerIndex });
                foreach (var card in player.Characters)
                    card.PowerModsUntilOppEnd.Add(new CardPowerMod { Delta = 1000, AppliedBySide = playerIndex });
            }
        }

        if (result.Kind == PlayKind.Event && result.Card.Info.Cost >= 3
            && Has(state, playerIndex, 32))
        {
            // 老练狙神与最终形态使用不同语义，复用布尔会互相污染；以专用 TurnOnce key 持久化。
            const string key = "hex-32-veteran-sniper";
            if (!player.TurnOnceUsed.Contains(key))
            {
                player.TurnOnceUsed.Add(key);
                int refresh = result.PaidCost;
                foreach (var don in player.CostArea.Where(don => don.State == DonState.Rest && don.AttachedToCardId is null).Take(refresh))
                    don.State = DonState.Active;
            }
        }
        await Task.CompletedTask;
    }

    public static async Task OnLeaderDamagedAsync(GameEngine engine, int defender, int damage, CardInstance? attacker)
    {
        if (damage <= 0 || !engine.State.HexState.Enabled) return;
        var state = engine.State;
        int attackerSide = 1 - defender;
        state.HexState.Runtime[defender].TankEngineOpponentTurnPower = 0;

        if (attacker is not null && Has(state, attackerSide, 4))
            TurnEngine.DrawCard(state, attackerSide, 1);
        if (attacker is not null && Has(state, attackerSide, 8)
            && !state.HexState.Runtime[attackerSide].SoulSiphonUsedThisTurn
            && state.CurrentPowerOf(attackerSide, attacker) >= 12000)
        {
            state.HexState.Runtime[attackerSide].SoulSiphonUsedThisTurn = true;
            await AddLifeFromDeckAsync(engine, attackerSide, 1, "soul_siphon");
        }

    }

    public static Task OnEnemyLifeReachedOneAsync(GameEngine engine, int attackerSide)
        => TryTriggerKingAsync(engine, attackerSide);

    public static async Task OnCharacterKoAsync(
        GameEngine engine,
        int victimOwner,
        string reason,
        Guid? attackerId,
        int actingSide)
    {
        var state = engine.State;
        if (!state.HexState.Enabled) return;
        // “己方 KO 敌方”只归属实际对立方。自己效果 KO 自己的角色、规则清理或来源不明的 KO
        // 不能误触发坦克引擎/海军狂欢；战斗 KO 的攻击方可由受害方唯一反推。
        int koSide = actingSide is 0 or 1 && actingSide != victimOwner
            ? actingSide
            : reason == "battle"
                ? 1 - victimOwner
                : -1;
        if (koSide is 0 or 1 && reason == "battle" && attackerId is { } id)
        {
            var attacker = state.Players[koSide].Leader.Id == id
                ? state.Players[koSide].Leader
                : state.Players[koSide].Characters.FirstOrDefault(card => card.Id == id);
            if (attacker is not null && ReferenceEquals(attacker, state.Players[koSide].Leader)
                && Has(state, koSide, 22))
            {
                if (state.HexState.RulesRevision >= BalanceRulesRevision)
                    state.HexState.Runtime[koSide].TranscendentEvilOwnTurnPower += 500;
                else
                    attacker.PowerModThisTurn += 500;
            }
        }

        if (Has(state, victimOwner, 23))
            foreach (var card in state.Players[1 - victimOwner].Characters)
                card.PowerModThisTurn -= 1000;

        if (koSide is < 0 or > 1) return;
        var runtime = state.HexState.Runtime[koSide];
        if (Has(state, koSide, 44) && !runtime.TankEngineUsedThisTurn)
        {
            runtime.TankEngineUsedThisTurn = true;
            runtime.TankEngineOpponentTurnPower += 1000;
        }
        if (Has(state, koSide, 54) && !runtime.NavyCarnivalUsedThisTurn)
        {
            runtime.NavyCarnivalUsedThisTurn = true;
            state.Players[koSide].Leader.PowerModThisTurn += 1000;
            foreach (var card in state.Players[koSide].Characters) card.PowerModThisTurn += 1000;
        }
        await Task.CompletedTask;
    }

    public static async Task OnEnemyAffectedByOwnEffectAsync(
        GameEngine engine,
        int actingSide,
        int affectedOwner,
        CardInstance? affected,
        bool wasActiveRested,
        bool leftField)
    {
        var state = engine.State;
        if (!state.HexState.Enabled || actingSide is < 0 or > 1 || affectedOwner == actingSide) return;
        var runtime = state.HexState.Runtime[actingSide];
        if (wasActiveRested && affected is not null && Has(state, actingSide, 21))
            affected.PowerModThisTurn -= 3000;

        if (!wasActiveRested && !leftField) return;
        if (Has(state, actingSide, 41) && !runtime.SlapUsedThisTurn)
        {
            runtime.SlapUsedThisTurn = true;
            var player = state.Players[actingSide];
            TurnEngine.DrawCard(state, actingSide, 1);
            if (player.Hand.Count > 0)
            {
                var chosen = await engine.Prompts.ChooseCards(actingSide, "HexSlapDiscard",
                    "扇巴掌：抽1张后丢弃1张手牌",
                    player.Hand.Select(card => card.Id.ToString()).ToList(), 1, 1,
                    new Dictionary<string, object?>
                    {
                        ["choiceCards"] = player.Hand.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToArray(),
                    });
                var discard = player.Hand.FirstOrDefault(card => chosen.Contains(card.Id.ToString())) ?? player.Hand[0];
                player.Hand.Remove(discard);
                player.Trash.Add(discard);
            }
        }
        if (Has(state, actingSide, 42) && !runtime.SoulConsumeUsedThisTurn)
        {
            runtime.SoulConsumeUsedThisTurn = true;
            state.Players[actingSide].Leader.PowerModThisTurn += 2000;
        }
    }

    public static async Task OnLifeAddedAsync(GameEngine engine, int owner, int actualAdded, bool allowCriticalHeal = true)
    {
        if (actualAdded <= 0 || !engine.State.HexState.Enabled) return;
        var state = engine.State;
        if (Has(state, owner, 43))
            state.Players[1 - owner].Leader.PowerModThisTurn -= 1000 * actualAdded;

        var runtime = state.HexState.Runtime[owner];
        if (!allowCriticalHeal || !Has(state, owner, 31) || runtime.CriticalHealSucceededThisTurn) return;
        for (int i = 0; i < actualAdded && !runtime.CriticalHealSucceededThisTurn; i++)
        {
            int roll = state.NextRecordedRandom(4, "hex_critical_heal", owner, new { turnCount = state.TurnCount });
            if (roll != 0) continue;
            runtime.CriticalHealSucceededThisTurn = true;
            await AddLifeFromDeckAsync(engine, owner, 1, "critical_heal", allowCriticalHeal: false);
        }
    }

    public static bool CanDeclareAnotherAttack(GameState state, int playerIndex)
        => !Has(state, playerIndex, 45) || state.HexState.Runtime[playerIndex].AttacksDeclaredThisTurn == 0;

    public static int AttackSuccessDeficit(GameState state, int attackerSide)
        => state.HexState.RulesRevision < ScopeReworkRulesRevision
            ? Has(state, attackerSide, 27) ? 2000 : Has(state, attackerSide, 26) ? 1000 : 0
            : 0;

    public static bool LeaderMayAttackLeader(GameState state, int attackerSide)
        => !Has(state, attackerSide, 34);

    public static bool MayAttackProtectedZeroLifeLeader(GameState state, int defenderSide)
        => !(Has(state, defenderSide, 53)
             && state.Players[defenderSide].LifeArea.Count == 0
             && state.Players[defenderSide].Characters.Any(card => card.IsTapped));

    public static bool CanCopyEffect(GameState state, int owner, EffectTrigger trigger, bool alreadyCopied)
    {
        if (alreadyCopied || !state.HexState.Enabled) return false;
        var runtime = state.HexState.Runtime[owner];
        if (trigger == EffectTrigger.OnAttackDeclare && Has(state, owner, 13)) return true;
        if (trigger == EffectTrigger.OnEnterField && Has(state, owner, 16))
            return state.HexState.RulesRevision >= BoardingSalvoRulesRevision
                ? runtime.ActivatedEnterEffectsThisTurn < 2
                : !runtime.FirstEnterEffectCopiedThisTurn;
        if (trigger == EffectTrigger.OnKO && Has(state, owner, 17))
            return !runtime.FirstKoEffectCopiedThisTurn;
        if (trigger is EffectTrigger.EventMain or EffectTrigger.EventCounter && Has(state, owner, 46)) return true;
        return false;
    }

    public static bool ShouldCopyEffect(GameState state, int owner, EffectTrigger trigger, bool alreadyCopied)
    {
        if (!CanCopyEffect(state, owner, trigger, alreadyCopied)) return false;
        var runtime = state.HexState.Runtime[owner];
        if (trigger == EffectTrigger.OnEnterField)
        {
            if (state.HexState.RulesRevision >= BoardingSalvoRulesRevision)
                return ++runtime.ActivatedEnterEffectsThisTurn == 2;
            runtime.FirstEnterEffectCopiedThisTurn = true;
        }
        if (trigger == EffectTrigger.OnKO) runtime.FirstKoEffectCopiedThisTurn = true;
        return true;
    }

    public static bool ShouldTriggerAttackEffectOnEntry(GameState state, int owner, CardInstance card)
        => Has(state, owner, 15) && EffectRuntime.HasEffectForTrigger(card, EffectTrigger.OnAttackDeclare);

    /// <summary>
    /// 尖端发明家：第一次成功消耗某个【每回合1次】内部键时移除该键，第二次才保留。
    /// 同时覆盖 PlayerState 与 CardInstance 两套历史限次实现。
    /// </summary>
    public static void ApplyInventorSecondUse(
        GameState state,
        int owner,
        CardInstance source,
        IReadOnlySet<string> playerKeysBefore,
        IReadOnlySet<string> cardKeysBefore)
    {
        if (!Has(state, owner, 5)) return;
        var runtime = state.HexState.Runtime[owner];
        var player = state.Players[owner];

        foreach (var key in player.TurnOnceUsed.Except(playerKeysBefore).ToArray())
        {
            var token = $"player:{key}";
            if (runtime.InventorFirstUseKeys.Add(token))
                player.TurnOnceUsed.Remove(key);
        }
        foreach (var key in source.OncePerTurnUsedKeys.Except(cardKeysBefore).ToArray())
        {
            var token = $"card:{source.Id}:{key}";
            if (runtime.InventorFirstUseKeys.Add(token))
                source.OncePerTurnUsedKeys.Remove(key);
        }
        if (!player.TurnOnceUsed.Except(playerKeysBefore).Any()
            && !source.OncePerTurnUsedKeys.Except(cardKeysBefore).Any())
            player.OncePerTurnEffectUsedCardIds.Remove(source.Id);
    }

    /// <summary>统一消费角色休息、离场和 KO watcher，避免各原子移动入口各自复制海克斯逻辑。</summary>
    public static async Task OnGameEventAsync(
        GameState state,
        EffectTrigger trigger,
        IPromptService prompts,
        Dictionary<string, object?>? payload)
    {
        if (!state.HexState.Enabled || payload is null || (prompts as PromptSystem)?.Engine is not { } engine)
            return;

        static int Int(Dictionary<string, object?> values, string key, int fallback = -1)
            => values.TryGetValue(key, out var value) && value is not null
                ? Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)
                : fallback;
        static string? Text(Dictionary<string, object?> values, string key)
            => values.TryGetValue(key, out var value) ? value?.ToString() : null;

        if (trigger == EffectTrigger.OnCharRested && Text(payload, "reason") == "effect")
        {
            var card = FindCardAnywhere(state, Text(payload, "restedCardId"));
            if (card is null || card.Info.Kind != CardKind.Character) return;
            int owner = Int(payload, "owner", state.SideOf(card));
            int acting = Int(payload, "actingSide", EffectRuntime.CurrentActingSide);
            await OnEnemyAffectedByOwnEffectAsync(engine, acting, owner, card, true, false);
            return;
        }

        if (trigger == EffectTrigger.OnCharLeaveField)
        {
            var card = FindCardAnywhere(state, Text(payload, "cardId"));
            if (card is null || card.Info.Kind != CardKind.Character) return;
            int owner = Int(payload, "owner", state.SideOf(card));
            int acting = Int(payload, "actingSide", EffectRuntime.CurrentActingSide);
            await OnEnemyAffectedByOwnEffectAsync(engine, acting, owner, card, false, true);
            return;
        }

        if (trigger == EffectTrigger.OnAnyCharKOd)
        {
            int victimOwner = Int(payload, "owner");
            if (victimOwner is < 0 or > 1) return;
            var victim = FindCardAnywhere(state, Text(payload, "cardId"));
            if (victim is null || victim.Info.Kind != CardKind.Character) return;
            string reason = Text(payload, "reason") ?? "effect";
            int acting = Int(payload, "actingSide", reason == "battle" ? 1 - victimOwner : -1);
            Guid? attackerId = Guid.TryParse(Text(payload, "attackerId"), out var parsed) ? parsed : null;
            await OnCharacterKoAsync(engine, victimOwner, reason, attackerId, acting);
        }
    }

    private static CardInstance? FindCardAnywhere(GameState state, string? idText)
    {
        if (!Guid.TryParse(idText, out var id)) return null;
        foreach (var player in state.Players)
        {
            if (player.Leader.Id == id) return player.Leader;
            var found = player.Characters
                .Concat(player.Hand)
                .Concat(player.Trash)
                .Concat(player.Deck)
                .Concat(player.LifeArea)
                .FirstOrDefault(card => card.Id == id);
            if (found is not null) return found;
            if (player.StageCard?.Id == id) return player.StageCard;
            if (player.ExtraStageCard?.Id == id) return player.ExtraStageCard;
        }
        return null;
    }

    private static IReadOnlyList<int> DrawCandidates(GameState state, int player, HexTier tier, int count)
    {
        bool excludeAppeared = state.HexState.RulesRevision >= PerSlotRefreshRulesRevision;
        var pool = HexCatalog.ForTier(tier, state.HexState)
            .Select(item => item.Id)
            .Where(id => !state.HexState.Owned[player].Contains(id))
            .Where(id => !excludeAppeared || !state.HexState.Appeared[player].Contains(id))
            .ToList();
        if (pool.Count < count)
            throw new InvalidOperationException(
                $"{HexCatalog.TierDisplayName(tier)}海克斯未出现候选不足：需要 {count} 个，实际 {pool.Count} 个");
        var result = new List<int>(count);
        while (result.Count < count)
        {
            int index = state.NextRecordedRandom(pool.Count, "hex_draft_candidate", player,
                new { tier = tier.ToString(), slot = result.Count });
            result.Add(pool[index]);
            pool.RemoveAt(index);
        }
        if (excludeAppeared)
        {
            foreach (int id in result)
                state.HexState.Appeared[player].Add(id);
        }
        return result;
    }

    private static void AddOwned(
        GameState state,
        int player,
        int hexId,
        bool grantedByTransmutation = false)
    {
        _ = HexCatalog.Get(hexId);
        if (!state.HexState.Owned[player].Contains(hexId)) state.HexState.Owned[player].Add(hexId);
        if (grantedByTransmutation
            && state.HexState.RulesRevision >= TransmutationPresentationRulesRevision)
            state.HexState.GrantedByTransmutation[player].Add(hexId);
    }

    private static async Task ApplyDraftGrantAsync(
        GameEngine engine,
        HexDraftSettlement settlement,
        HexGrantProgress grant)
    {
        if (grant.Completed) return;
        var state = engine.State;
        var player = state.Players[grant.PlayerIndex];

        switch (grant.HexId)
        {
            case 6:
            {
                if (state.HexState.RulesRevision < AstralBodyRulesRevision)
                {
                    if (grant.PlannedStepCount < 0)
                        grant.PlannedStepCount = Math.Min(2, player.Hand.Count);
                    while (grant.NextStep < grant.PlannedStepCount)
                    {
                        if (!await AddChosenHandCardToLifeAsync(
                                engine,
                                grant.PlayerIndex,
                                $"星界躯体：选择第 {grant.NextStep + 1} 张放入生命区的手牌",
                                grant.NextStep + 1))
                        {
                            grant.PlannedStepCount = grant.NextStep;
                            break;
                        }
                        CommitGrantStep(state, settlement, grant);
                    }
                    break;
                }

                if (grant.PlannedStepCount < 0)
                    grant.PlannedStepCount = 2;
                else if (grant.PlannedStepCount != 2)
                    throw new InvalidOperationException("星界躯体授予计划步数与规则版本不一致");

                if (grant.NextStep == 0)
                {
                    await AddChosenHandCardToLifeAsync(
                        engine,
                        grant.PlayerIndex,
                        "星界躯体：选择1张手牌放入生命区",
                        order: 1);
                    CommitGrantStep(state, settlement, grant);
                }
                if (grant.NextStep == 1)
                {
                    await AddLifeFromDeckAsync(engine, grant.PlayerIndex, 1, "astral_body");
                    CommitGrantStep(state, settlement, grant);
                }
                break;
            }
            case 9:
                if (grant.NextStep == 0)
                {
                    await AddLifeFromDeckAsync(engine, grant.PlayerIndex, 1, "goliath_giant");
                    CommitGrantStep(state, settlement, grant);
                }
                if (grant.NextStep == 1)
                {
                    player.Leader.PowerModPersistent += 1000;
                    CommitGrantStep(state, settlement, grant);
                }
                break;
            case 20:
                if (grant.NextStep == 0)
                {
                    if (player.LifeArea.Count > 0)
                    {
                        var top = player.LifeArea[0];
                        player.LifeArea.RemoveAt(0);
                        player.Hand.Add(top);
                        state.LifeLeftThisTurn.Add(grant.PlayerIndex);
                    }
                    CommitGrantStep(state, settlement, grant);
                }
                break;
            case 47:
                ApplyDraftRandomGrantPlan(state, settlement, grant, ChaosGrantPlans);
                break;
            case 55:
                ApplyDraftRandomGrantPlan(state, settlement, grant, GoldenGrantPlans(state));
                break;
            case 56:
                ApplyDraftRandomGrantPlan(state, settlement, grant, PrismaticTierGrantPlans);
                break;
            case 49:
                if (grant.NextStep == 0)
                {
                    TurnEngine.DrawCard(state, grant.PlayerIndex, 3);
                    CommitGrantStep(state, settlement, grant);
                }
                break;
            case 52:
                if (grant.NextStep == 0)
                {
                    player.DonDeck.Add(new DonCard());
                    player.DonDeck.Add(new DonCard());
                    CommitGrantStep(state, settlement, grant);
                }
                break;
        }

        grant.Completed = true;
    }

    private static void ApplyDraftRandomGrantPlan(
        GameState state,
        HexDraftSettlement settlement,
        HexGrantProgress grant,
        IReadOnlyList<RandomGrantPlan> plans)
    {
        if (grant.PlannedStepCount < 0)
            grant.PlannedStepCount = plans.Count;
        else if (grant.PlannedStepCount != plans.Count)
            throw new InvalidOperationException("海克斯随机授予计划步数与恢复状态不一致");

        while (grant.NextStep < plans.Count)
        {
            int slot = grant.NextStep;
            if (grant.PlannedChildHexIds.Count < slot)
                throw new InvalidOperationException("海克斯随机授予计划缺少已完成步骤");

            var plan = plans[slot];
            if (grant.PlannedChildHexIds.Count == slot)
            {
                var pool = RandomGrantPool(state, grant.PlayerIndex, grant.HexId, plan.Tier);
                int plannedHexId = 0;
                if (pool.Count > 0)
                {
                    int index = state.NextRecordedRandom(
                        pool.Count,
                        plan.RandomEventType,
                        grant.PlayerIndex,
                        RandomGrantContext(slot, plan.Tier));
                    plannedHexId = pool[index];
                }
                // 0 是明确的“该步骤池已耗尽”哨兵；仍提交步骤，避免恢复时反复尝试。
                grant.PlannedChildHexIds.Add(plannedHexId);
            }

            int childHexId = grant.PlannedChildHexIds[slot];
            if (childHexId > 0)
            {
                AddOwned(state, grant.PlayerIndex, childHexId, grantedByTransmutation: true);
                string childGrantKey = $"{grant.GrantKey}:{plan.GrantKeyGroup}:{slot}:{childHexId}";
                if (settlement.Grants.All(item =>
                        !string.Equals(item.GrantKey, childGrantKey, StringComparison.Ordinal)))
                {
                    string childPrefix = $"{grant.GrantKey}:{plan.GrantKeyGroup}:";
                    int insertAt = settlement.NextGrantIndex + 1
                        + settlement.Grants.Skip(settlement.NextGrantIndex + 1)
                            .TakeWhile(item => item.GrantKey.StartsWith(childPrefix, StringComparison.Ordinal))
                            .Count();
                    settlement.Grants.Insert(insertAt, new HexGrantProgress
                    {
                        GrantKey = childGrantKey,
                        PlayerIndex = grant.PlayerIndex,
                        HexId = childHexId,
                    });
                }
            }
            CommitGrantStep(state, settlement, grant);
        }
    }

    private static void CommitGrantStep(
        GameState state,
        HexDraftSettlement settlement,
        HexGrantProgress grant)
    {
        grant.NextStep++;
        state.HexState.GrantStepFaultInjector?.Invoke(new HexGrantStepBoundary(
            settlement.RoundId,
            grant.GrantKey,
            grant.PlayerIndex,
            grant.HexId,
            grant.NextStep));
    }

    private static async Task GrantAsync(
        GameEngine engine,
        int player,
        int hexId,
        bool grantedByTransmutation = false)
    {
        if (engine.State.HexState.Owned[player].Contains(hexId)) return;
        AddOwned(engine.State, player, hexId, grantedByTransmutation);
        await ApplyOnAcquireAsync(engine, player, hexId);
    }

    internal static async Task ApplyOnAcquireAsync(GameEngine engine, int playerIndex, int hexId)
    {
        var state = engine.State;
        var player = state.Players[playerIndex];
        switch (hexId)
        {
            case 6:
            {
                if (state.HexState.RulesRevision < AstralBodyRulesRevision)
                {
                    int count = Math.Min(2, player.Hand.Count);
                    for (int order = 0; order < count; order++)
                        await AddChosenHandCardToLifeAsync(
                            engine,
                            playerIndex,
                            $"星界躯体：选择第 {order + 1} 张放入生命区的手牌",
                            order + 1);
                    break;
                }

                await AddChosenHandCardToLifeAsync(
                    engine,
                    playerIndex,
                    "星界躯体：选择1张手牌放入生命区",
                    order: 1);
                await AddLifeFromDeckAsync(engine, playerIndex, 1, "astral_body");
                break;
            }
            case 9:
                await AddLifeFromDeckAsync(engine, playerIndex, 1, "goliath_giant");
                player.Leader.PowerModPersistent += 1000;
                break;
            case 20:
                if (player.LifeArea.Count > 0)
                {
                    var top = player.LifeArea[0];
                    player.LifeArea.RemoveAt(0);
                    player.Hand.Add(top);
                    state.LifeLeftThisTurn.Add(playerIndex);
                }
                break;
            case 47:
                await ApplyDirectRandomGrantPlanAsync(engine, playerIndex, hexId, ChaosGrantPlans);
                break;
            case 55:
                await ApplyDirectRandomGrantPlanAsync(engine, playerIndex, hexId, GoldenGrantPlans(state));
                break;
            case 56:
                await ApplyDirectRandomGrantPlanAsync(engine, playerIndex, hexId, PrismaticTierGrantPlans);
                break;
            case 49:
                TurnEngine.DrawCard(state, playerIndex, 3);
                break;
            case 52:
                player.DonDeck.Add(new DonCard());
                player.DonDeck.Add(new DonCard());
                break;
        }
    }

    private static async Task ApplyDirectRandomGrantPlanAsync(
        GameEngine engine,
        int playerIndex,
        int sourceHexId,
        IReadOnlyList<RandomGrantPlan> plans)
    {
        var state = engine.State;
        for (int slot = 0; slot < plans.Count; slot++)
        {
            var plan = plans[slot];
            var pool = RandomGrantPool(state, playerIndex, sourceHexId, plan.Tier);
            if (pool.Count == 0) continue;
            int index = state.NextRecordedRandom(
                pool.Count,
                plan.RandomEventType,
                playerIndex,
                RandomGrantContext(slot, plan.Tier));
            await GrantAsync(engine, playerIndex, pool[index], grantedByTransmutation: true);
            if (state.IsGameOver) break;
        }
    }

    private static List<int> RandomGrantPool(
        GameState state,
        int playerIndex,
        int sourceHexId,
        HexTier? tier)
    {
        var definitions = tier is { } requiredTier
            ? HexCatalog.ForTier(requiredTier, state.HexState)
            : HexCatalog.RegularForState(state.HexState);
        return definitions
            .Select(item => item.Id)
            .Where(id => id != sourceHexId)
            .Where(id => !state.HexState.Owned[playerIndex].Contains(id))
            .ToList();
    }

    private static object RandomGrantContext(int slot, HexTier? tier)
        => tier is { } requiredTier
            ? new { slot, tier = requiredTier.ToString() }
            : new { slot };

    private static IReadOnlyList<RandomGrantPlan> GoldenGrantPlans(GameState state)
        => state.HexState.RulesRevision >= TransmutationPresentationRulesRevision
            ? GoldenTierGrantPlans
            : LegacyGoldenTierGrantPlans;

    private static async Task TryTriggerKingAsync(GameEngine engine, int owner)
    {
        var state = engine.State;
        var runtime = state.HexState.Runtime[owner];
        if (!Has(state, owner, 48) || runtime.KingUsedThisGame) return;
        runtime.KingUsedThisGame = true;
        var pool = HexCatalog.ForTier(HexTier.Rainbow, state.HexState).Select(item => item.Id)
            .Where(id => !state.HexState.Owned[owner].Contains(id))
            .ToList();
        if (pool.Count > 0)
        {
            int index = state.NextRecordedRandom(pool.Count, "hex_king_rainbow_grant", owner);
            await GrantAsync(engine, owner, pool[index]);
        }
        TurnEngine.DrawCard(state, owner, 2);
    }

    private static async Task<int> AddLifeFromDeckAsync(
        GameEngine engine,
        int owner,
        int count,
        string reason,
        bool allowCriticalHeal = true)
    {
        var state = engine.State;
        var player = state.Players[owner];
        int added = 0;
        for (int i = 0; i < count; i++)
        {
            if (player.Deck.Count == 0)
            {
                state.EvaluateDeckOut();
                break;
            }
            var top = player.Deck[0];
            player.Deck.RemoveAt(0);
            player.LifeArea.Insert(0, top);
            added++;
            state.EvaluateDeckOut();
            if (state.IsGameOver) break;
        }
        if (added > 0)
        {
            engine.RecordMatchLog("hex_life_added", owner, new { reason, count = added });
            await OnLifeAddedAsync(engine, owner, added, allowCriticalHeal);
        }
        return added;
    }

    private static async Task<bool> AddChosenHandCardToLifeAsync(
        GameEngine engine,
        int playerIndex,
        string description,
        int order)
    {
        var player = engine.State.Players[playerIndex];
        var candidates = player.Hand.ToList();
        if (candidates.Count == 0) return false;

        var chosen = await engine.Prompts.ChooseCards(
            playerIndex,
            "HexAstralBody",
            description,
            candidates.Select(card => card.Id.ToString()).ToList(),
            1,
            1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = candidates.Select(card => new
                {
                    id = card.Id.ToString(),
                    number = card.Info.Number,
                }).ToArray(),
                ["order"] = order,
            });
        var card = candidates.FirstOrDefault(item => chosen.Contains(item.Id.ToString())) ?? candidates[0];
        player.Hand.Remove(card);
        player.LifeArea.Insert(0, card);
        await OnLifeAddedAsync(engine, playerIndex, 1);
        return true;
    }

    private sealed record RandomGrantPlan(
        HexTier? Tier,
        string RandomEventType,
        string GrantKeyGroup);
}
