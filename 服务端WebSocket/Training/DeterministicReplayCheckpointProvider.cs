using System.Reflection;
using System.Text.Json;
using GrandUMI.Game;
using GrandUMI.Game.Hex;

namespace GrandUMI.Training;

/// <summary>
/// 当前工件冻结的确定性 checkpoint provider。原始投影只在受控进程内短暂存在；
/// 对局日志只能持久化本类返回的 digest/count。
/// </summary>
public sealed class DeterministicReplayCheckpointProvider : IReplayCheckpointProvider
{
    public const string FullStateSchema = "grandumi.replay_full_state.v1";
    public const string PublicStateSchema = "grandumi.replay_public_state.v1";
    public const string RandomTraceSchema = "grandumi.replay_random_trace.v1";

    public static DeterministicReplayCheckpointProvider Current { get; } = new();

    private DeterministicReplayCheckpointProvider()
    {
    }

    public ReplayCheckpointDigest Capture(
        GameEngine engine,
        ReplayCheckpointContext context,
        IReadOnlyList<ReplayRandomTraceEvent> randomTrace)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(randomTrace);
        var fullState = BuildFullState(engine.State);
        var publicState = BuildPublicState(engine.State);
        var randomState = JsonSerializer.SerializeToElement(new
        {
            schema = RandomTraceSchema,
            events = randomTrace.Select((entry, index) => new
            {
                index,
                actor = entry.Actor,
                payload = entry.Payload,
            }).ToArray(),
        });
        return new ReplayCheckpointDigest(
            CanonicalJson.Hash(fullState),
            CanonicalJson.Hash(publicState),
            CanonicalJson.Hash(randomState),
            randomTrace.Count);
    }

    /// <summary>仅供同程序集验证与隐私测试；生产日志不得序列化返回值。</summary>
    internal static JsonElement BuildFullState(GameState state)
        => JsonSerializer.SerializeToElement(new
        {
            schema = FullStateSchema,
            state.RulesetId,
            state.RngSeed,
            state.RandomSeq,
            state.CurrentTurnPlayer,
            state.FirstPlayer,
            openingStage = state.OpeningStage.ToString(),
            state.StartingPlayerChooser,
            startingDiceRounds = state.StartingDiceRounds.Select(round => new
            {
                round.Player0,
                round.Player1,
            }).ToArray(),
            state.TurnCount,
            phase = PhaseLabels.Of(state.Phase),
            state.AttachDonOperationSequence,
            attachDonUndoStack = state.AttachDonUndoStack.Select(entry => new
            {
                entry.OperationSequence,
                entry.PlayerIndex,
                entry.TargetId,
                targetCardId = Id(entry.TargetCardId),
                donIds = entry.DonIds.Select(Id).ToArray(),
            }).ToArray(),
            pendingPrompt = FullPrompt(state.PendingPrompt),
            pendingReveal = state.PendingReveal is null ? null : new
            {
                state.PendingReveal.OwnerIndex,
                cardNumbers = state.PendingReveal.CardNumbers.ToArray(),
            },
            currentBattle = Battle(state.CurrentBattle),
            state.KOReason,
            state.KOActingSide,
            koSourceCardId = state.KOSourceCardId is null ? null : Id(state.KOSourceCardId.Value),
            state.WinnerIndex,
            state.IsDraw,
            gameOverReasonCategory = ReplayTerminalSemantics.ReasonCategory(
                state.GameOverReason,
                state.IsDraw),
            state.PendingDrawRequester,
            pendingDrawDescriptionDigest = state.PendingDrawRequestDescription is null
                ? null
                : CanonicalJson.Sha256Utf8(state.PendingDrawRequestDescription),
            drawRequestRejectionCounts = state.DrawRequestRejectionCounts.ToArray(),
            matchKind = state.MatchKind.ToString(),
            hexState = FullHexState(state),
            players = state.Players.Select((player, index) => FullPlayer(player, index)).ToArray(),
            preventKoCardIds = SortedIds(state.PreventKOCardIds),
            simultaneousKoVictimIds = SortedIds(state.SimultaneousKOVictimIds),
            simultaneousLeaveVictimIds = SortedIds(state.SimultaneousLeaveVictimIds),
            preventLeaveCardIds = SortedIds(state.PreventLeaveCardIds),
            continuousEffects = state.ContinuousEffects.Select(ContinuousEffect).ToArray(),
            pendingWatchers = state.PendingWatchers.Select(watcher => new
            {
                trigger = watcher.Trigger.ToString(),
                watcher.Payload,
            }).ToArray(),
            pendingKoEffects = state.PendingKOEffects.Select(effect => new
            {
                effect.Owner,
                card = FullCard(effect.Card),
                effect.ActingSide,
                sourceCardId = effect.SourceCardId is null ? null : Id(effect.SourceCardId.Value),
            }).ToArray(),
            pendingEnterFields = state.PendingEnterFields.Select(entry => new
            {
                entry.Owner,
                cardId = Id(entry.CardId),
                entry.From,
                effectSourceKind = entry.EffectSourceKind?.ToString(),
                entry.EffectSourceNumber,
                entry.LifeTriggerOrigin,
            }).ToArray(),
            state.ExtraTurnPending,
            noPlayCharacterThisTurn = state.NoPlayCharacterThisTurn.Order().ToArray(),
            noPlayCharacterOriginalCostGteThisTurn = state.NoPlayCharacterOriginalCostGteThisTurn
                .OrderBy(pair => pair.Key)
                .Select(pair => new { playerIndex = pair.Key, threshold = pair.Value })
                .ToArray(),
            noEffectLifeToHandThisTurn = state.NoEffectLifeToHandThisTurn.Order().ToArray(),
            noActivateDonByCharacterEffectThisTurn = state.NoActivateDonByCharacterEffectThisTurn.Order().ToArray(),
            lifeLeftThisTurn = state.LifeLeftThisTurn.Order().ToArray(),
            attackTaxDiscard = state.AttackTaxDiscard.ToArray(),
            endOfTurnTasks = state.EndOfTurnTasks.Select(task => new
            {
                task.Kind,
                task.SourceCardId,
                task.Owner,
                task.Count,
            }).ToArray(),
            nextOppMainPhaseTasks = state.NextOppMainPhaseTasks.Select(task => new
            {
                task.Kind,
                task.Owner,
                task.SourceCardId,
            }).ToArray(),
            deckOutVictoryPlayers = state.DeckOutVictoryPlayers.Order().ToArray(),
            oneShotPlayDiscounts = state.OneShotPlayDiscounts.Select(discount => new
            {
                discount.Owner,
                discount.Amount,
                discount.MinCost,
                discount.Keyword,
                discount.Kind,
                discount.NameContains,
            }).ToArray(),
        });

    /// <summary>公开投影不含任何一方手牌内容、暗生命、牌库内容或完整卡组。</summary>
    internal static JsonElement BuildPublicState(GameState state)
        => JsonSerializer.SerializeToElement(new
        {
            schema = PublicStateSchema,
            state.RulesetId,
            state.RandomSeq,
            state.CurrentTurnPlayer,
            state.FirstPlayer,
            openingStage = state.OpeningStage.ToString(),
            state.StartingPlayerChooser,
            startingDiceRounds = state.StartingDiceRounds.Select(round => new
            {
                round.Player0,
                round.Player1,
            }).ToArray(),
            state.TurnCount,
            phase = PhaseLabels.Of(state.Phase),
            state.AttachDonOperationSequence,
            attachDonUndoDepth = state.AttachDonUndoStack.Count,
            pendingPrompt = PublicPrompt(state.PendingPrompt),
            currentBattle = Battle(state.CurrentBattle),
            state.WinnerIndex,
            state.IsDraw,
            gameOverReasonCategory = ReplayTerminalSemantics.ReasonCategory(
                state.GameOverReason,
                state.IsDraw),
            state.PendingDrawRequester,
            drawRequestRejectionCounts = state.DrawRequestRejectionCounts.ToArray(),
            matchKind = state.MatchKind.ToString(),
            hexState = PublicHexState(state),
            players = state.Players.Select((player, index) => PublicPlayer(player, index)).ToArray(),
            continuousEffects = state.ContinuousEffects.Select(ContinuousEffect).ToArray(),
            state.ExtraTurnPending,
            noPlayCharacterThisTurn = state.NoPlayCharacterThisTurn.Order().ToArray(),
            noPlayCharacterOriginalCostGteThisTurn = state.NoPlayCharacterOriginalCostGteThisTurn
                .OrderBy(pair => pair.Key)
                .Select(pair => new { playerIndex = pair.Key, threshold = pair.Value })
                .ToArray(),
            noEffectLifeToHandThisTurn = state.NoEffectLifeToHandThisTurn.Order().ToArray(),
            noActivateDonByCharacterEffectThisTurn = state.NoActivateDonByCharacterEffectThisTurn.Order().ToArray(),
            lifeLeftThisTurn = state.LifeLeftThisTurn.Order().ToArray(),
            attackTaxDiscard = state.AttackTaxDiscard.ToArray(),
            endOfTurnTasks = state.EndOfTurnTasks.Select(task => new
            {
                task.Kind,
                task.SourceCardId,
                task.Owner,
                task.Count,
            }).ToArray(),
            nextOppMainPhaseTasks = state.NextOppMainPhaseTasks.Select(task => new
            {
                task.Kind,
                task.Owner,
                task.SourceCardId,
            }).ToArray(),
            deckOutVictoryPlayers = state.DeckOutVictoryPlayers.Order().ToArray(),
            oneShotPlayDiscounts = state.OneShotPlayDiscounts.Select(discount => new
            {
                discount.Owner,
                discount.Amount,
                discount.MinCost,
                discount.Keyword,
                discount.Kind,
                discount.NameContains,
            }).ToArray(),
        });

    private static object FullHexState(GameState state)
    {
        if (state.HexState.RulesRevision < HexRules.PerSlotRefreshRulesRevision)
            return LegacyFullHexState(state);

        return new
        {
            state.HexState.Enabled,
            state.HexState.RulesRevision,
            state.HexState.DraftSequence,
            draftTierSequence = state.HexState.DraftTierSequence.Select(tier => tier.ToString()).ToArray(),
            state.HexState.DraftResolving,
            resumePoint = state.HexState.ResumePoint.ToString(),
            owned = state.HexState.Owned.Select(items => items.ToArray()).ToArray(),
            appeared = state.HexState.Appeared
                .Select(items => items.Order().ToArray())
                .ToArray(),
            completedOwnTurns = state.HexState.CompletedOwnTurns.ToArray(),
            runtime = state.HexState.Runtime.Select(runtime => new
            {
                runtime.CardsPlayedThisTurn,
                runtime.SoulSiphonUsedThisTurn,
                runtime.FirstLeaderAttackSeenThisTurn,
                runtime.FirstCharacterAttackSeenThisTurn,
                runtime.FirstEnterEffectCopiedThisTurn,
                runtime.FirstKoEffectCopiedThisTurn,
                runtime.AttacksDeclaredThisTurn,
                runtime.RestingCharacterAttacksThisGame,
                runtime.SteelHeartUsedThisGame,
                runtime.UltimateRefreshUsedThisTurn,
                runtime.FinalFormUsedThisTurn,
                runtime.CriticalHealSucceededThisTurn,
                runtime.EventDrawConvertedThisTurn,
                runtime.CharacterDrawConvertedThisTurn,
                runtime.SlapUsedThisTurn,
                runtime.SoulConsumeUsedThisTurn,
                runtime.TankEngineUsedThisTurn,
                runtime.TankEngineOpponentTurnPower,
                runtime.NavyCarnivalUsedThisTurn,
                runtime.KingUsedThisGame,
                runtime.TranscendentEvilOwnTurnPower,
                inventorFirstUseKeys = runtime.InventorFirstUseKeys.Order(StringComparer.Ordinal).ToArray(),
            }).ToArray(),
            activeDraft = state.HexState.ActiveDraft is { } draft
                ? new
                {
                    draft.RoundId,
                    draft.PlayerIndex,
                    draft.OwnTurnNumber,
                    tier = draft.Tier.ToString(),
                    candidates = draft.Candidates.ToArray(),
                    draft.LockedChoice,
                    draft.Locked,
                    draft.RefreshUsed,
                    draft.RefreshedCandidateIndex,
                    draft.ReplacedHexId,
                    draft.ReplacementHexId,
                    refreshes = draft.Refreshes.Select(refresh => new
                    {
                        refresh.CandidateIndex,
                        refresh.ReplacedHexId,
                        refresh.ReplacementHexId,
                    }).ToArray(),
                }
                : null,
            pendingSettlement = state.HexState.PendingSettlement is { } settlement
                ? new
                {
                    settlement.RoundId,
                    tier = settlement.Tier.ToString(),
                    settlement.PlayerIndex,
                    settlement.OwnTurnNumber,
                    settlement.Choice,
                    resumePoint = settlement.ResumePoint.ToString(),
                    settlement.RootOwnershipCommitted,
                    settlement.NextGrantIndex,
                    grants = settlement.Grants.Select(grant => new
                    {
                        grant.GrantKey,
                        grant.PlayerIndex,
                        grant.HexId,
                        grant.NextStep,
                        grant.PlannedStepCount,
                        plannedChildHexIds = grant.PlannedChildHexIds.ToArray(),
                        grant.Completed,
                    }).ToArray(),
                }
                : null,
            resolvedDrafts = state.HexState.ResolvedDrafts.Select(draft => new
            {
                draft.RoundId,
                tier = draft.Tier.ToString(),
                draft.PlayerIndex,
                draft.OwnTurnNumber,
                draft.Choice,
            }).ToArray(),
        };
    }

    private static object PublicHexState(GameState state)
    {
        if (state.HexState.RulesRevision < HexRules.PerSlotRefreshRulesRevision)
            return LegacyPublicHexState(state);

        return new
        {
            state.HexState.Enabled,
            state.HexState.RulesRevision,
            draftTierSequence = state.HexState.DraftTierSequence.Select(tier => tier.ToString()).ToArray(),
            draftResolving = state.HexState.DraftResolving || state.HexState.PendingSettlement is not null,
            owned = state.HexState.Owned.Select(items => items.ToArray()).ToArray(),
            appearedCounts = state.HexState.Appeared.Select(items => items.Count).ToArray(),
            completedOwnTurns = state.HexState.CompletedOwnTurns.ToArray(),
            activeDraft = state.HexState.ActiveDraft is { } draft
                ? new
                {
                    draft.RoundId,
                    draft.PlayerIndex,
                    draft.OwnTurnNumber,
                    tier = draft.Tier.ToString(),
                    draft.Locked,
                    draft.RefreshUsed,
                    refreshedCandidateIndices = HexRules.RefreshedCandidateIndices(
                        draft,
                        state.HexState.RulesRevision),
                    refreshRemaining = HexRules.RefreshRemaining(
                        draft,
                        state.HexState.RulesRevision),
                }
                : null,
            settlingRound = state.HexState.PendingSettlement is { } settlement
                ? new
                {
                    settlement.RoundId,
                    settlement.PlayerIndex,
                    settlement.OwnTurnNumber,
                    tier = settlement.Tier.ToString(),
                }
                : null,
            resolvedDrafts = state.HexState.ResolvedDrafts.Select(draft => new
            {
                draft.RoundId,
                tier = draft.Tier.ToString(),
                draft.PlayerIndex,
                draft.OwnTurnNumber,
                draft.Choice,
            }).ToArray(),
        };
    }

    /// <summary>规则修订版 1/2 的冻结投影；保持升级前 checkpoint 摘要可重放验证。</summary>
    private static object LegacyFullHexState(GameState state)
        => new
        {
            state.HexState.Enabled,
            state.HexState.RulesRevision,
            state.HexState.DraftSequence,
            draftTierSequence = state.HexState.DraftTierSequence.Select(tier => tier.ToString()).ToArray(),
            state.HexState.DraftResolving,
            resumePoint = state.HexState.ResumePoint.ToString(),
            owned = state.HexState.Owned.Select(items => items.ToArray()).ToArray(),
            completedOwnTurns = state.HexState.CompletedOwnTurns.ToArray(),
            runtime = state.HexState.Runtime.Select(runtime => new
            {
                runtime.CardsPlayedThisTurn,
                runtime.SoulSiphonUsedThisTurn,
                runtime.FirstLeaderAttackSeenThisTurn,
                runtime.FirstCharacterAttackSeenThisTurn,
                runtime.FirstEnterEffectCopiedThisTurn,
                runtime.FirstKoEffectCopiedThisTurn,
                runtime.AttacksDeclaredThisTurn,
                runtime.RestingCharacterAttacksThisGame,
                runtime.SteelHeartUsedThisGame,
                runtime.UltimateRefreshUsedThisTurn,
                runtime.FinalFormUsedThisTurn,
                runtime.CriticalHealSucceededThisTurn,
                runtime.EventDrawConvertedThisTurn,
                runtime.CharacterDrawConvertedThisTurn,
                runtime.SlapUsedThisTurn,
                runtime.SoulConsumeUsedThisTurn,
                runtime.TankEngineUsedThisTurn,
                runtime.TankEngineOpponentTurnPower,
                runtime.NavyCarnivalUsedThisTurn,
                runtime.KingUsedThisGame,
                runtime.TranscendentEvilOwnTurnPower,
                inventorFirstUseKeys = runtime.InventorFirstUseKeys.Order(StringComparer.Ordinal).ToArray(),
            }).ToArray(),
            activeDraft = state.HexState.ActiveDraft is { } draft
                ? new
                {
                    draft.RoundId,
                    draft.PlayerIndex,
                    draft.OwnTurnNumber,
                    tier = draft.Tier.ToString(),
                    candidates = draft.Candidates.ToArray(),
                    draft.LockedChoice,
                    draft.Locked,
                    draft.RefreshUsed,
                    draft.RefreshedCandidateIndex,
                    draft.ReplacedHexId,
                    draft.ReplacementHexId,
                }
                : null,
            pendingSettlement = state.HexState.PendingSettlement is { } settlement
                ? new
                {
                    settlement.RoundId,
                    tier = settlement.Tier.ToString(),
                    settlement.PlayerIndex,
                    settlement.OwnTurnNumber,
                    settlement.Choice,
                    resumePoint = settlement.ResumePoint.ToString(),
                    settlement.RootOwnershipCommitted,
                    settlement.NextGrantIndex,
                    grants = settlement.Grants.Select(grant => new
                    {
                        grant.GrantKey,
                        grant.PlayerIndex,
                        grant.HexId,
                        grant.NextStep,
                        grant.PlannedStepCount,
                        plannedChildHexIds = grant.PlannedChildHexIds.ToArray(),
                        grant.Completed,
                    }).ToArray(),
                }
                : null,
            resolvedDrafts = state.HexState.ResolvedDrafts.Select(draft => new
            {
                draft.RoundId,
                tier = draft.Tier.ToString(),
                draft.PlayerIndex,
                draft.OwnTurnNumber,
                draft.Choice,
            }).ToArray(),
        };

    /// <summary>规则修订版 1/2 的冻结公开投影；不新增字段以保持旧工件摘要稳定。</summary>
    private static object LegacyPublicHexState(GameState state)
        => new
        {
            state.HexState.Enabled,
            state.HexState.RulesRevision,
            draftTierSequence = state.HexState.DraftTierSequence.Select(tier => tier.ToString()).ToArray(),
            draftResolving = state.HexState.DraftResolving || state.HexState.PendingSettlement is not null,
            owned = state.HexState.Owned.Select(items => items.ToArray()).ToArray(),
            completedOwnTurns = state.HexState.CompletedOwnTurns.ToArray(),
            activeDraft = state.HexState.ActiveDraft is { } draft
                ? new
                {
                    draft.RoundId,
                    draft.PlayerIndex,
                    draft.OwnTurnNumber,
                    tier = draft.Tier.ToString(),
                    draft.Locked,
                    draft.RefreshUsed,
                }
                : null,
            settlingRound = state.HexState.PendingSettlement is { } settlement
                ? new
                {
                    settlement.RoundId,
                    settlement.PlayerIndex,
                    settlement.OwnTurnNumber,
                    tier = settlement.Tier.ToString(),
                }
                : null,
            resolvedDrafts = state.HexState.ResolvedDrafts.Select(draft => new
            {
                draft.RoundId,
                tier = draft.Tier.ToString(),
                draft.PlayerIndex,
                draft.OwnTurnNumber,
                draft.Choice,
            }).ToArray(),
        };

    private static object FullPlayer(PlayerState player, int playerIndex)
        => new
        {
            playerIndex,
            leader = FullCard(player.Leader),
            hand = player.Hand.Select(FullCard).ToArray(),
            characters = player.Characters.Select(FullCard).ToArray(),
            stage = player.StageCard is null ? null : FullCard(player.StageCard),
            extraStage = player.ExtraStageCard is null ? null : FullCard(player.ExtraStageCard),
            trash = player.Trash.Select(FullCard).ToArray(),
            deck = player.Deck.Select(FullCard).ToArray(),
            life = player.LifeArea.Select(FullCard).ToArray(),
            donDeck = player.DonDeck.Select(Don).ToArray(),
            costArea = player.CostArea.Select(Don).ToArray(),
            player.HasReDraw,
            player.MulliganDone,
            player.AlwaysPromptOnLifeReveal,
            turnOnceUsed = player.TurnOnceUsed.Order(StringComparer.Ordinal).ToArray(),
            oncePerTurnEffectUsedCardIds = SortedIds(player.OncePerTurnEffectUsedCardIds),
            player.HandDiscardedByEffectThisTurn,
            player.HasActivatedBaseCost3PlusEventThisTurn,
        };

    private static object PublicPlayer(PlayerState player, int playerIndex)
        => new
        {
            playerIndex,
            leader = FullCard(player.Leader),
            handCount = player.Hand.Count,
            characters = player.Characters.Select(FullCard).ToArray(),
            stage = player.StageCard is null ? null : FullCard(player.StageCard),
            extraStage = player.ExtraStageCard is null ? null : FullCard(player.ExtraStageCard),
            trash = player.Trash.Select(FullCard).ToArray(),
            deckCount = player.Deck.Count,
            life = player.LifeArea.Select(card => card.IsLifeFaceUp
                    ? (object)new { faceUp = true, card = FullCard(card) }
                    : new { faceUp = false, card = (object?)null })
                .ToArray(),
            donDeckCount = player.DonDeck.Count,
            costArea = player.CostArea.Select(Don).ToArray(),
            player.HasReDraw,
            player.MulliganDone,
            turnOnceUsed = player.TurnOnceUsed.Order(StringComparer.Ordinal).ToArray(),
            oncePerTurnEffectUsedCardIds = SortedIds(player.OncePerTurnEffectUsedCardIds),
            player.HandDiscardedByEffectThisTurn,
            player.HasActivatedBaseCost3PlusEventThisTurn,
        };

    private static object FullCard(CardInstance card)
        => new
        {
            id = Id(card.Id),
            number = card.Info.Number,
            card.IsTapped,
            card.PowerModThisTurn,
            card.PowerModThisBattle,
            card.PowerModPersistent,
            powerModsUntilOppEnd = card.PowerModsUntilOppEnd
                .Select(modifier => new { modifier.Delta, modifier.AppliedBySide, modifier.EndPhasesSeen })
                .OrderBy(value => value.Delta)
                .ThenBy(value => value.AppliedBySide)
                .ThenBy(value => value.EndPhasesSeen)
                .ToArray(),
            gainedKeywords = card.GainedKeywords
                .Select(keyword => new
                {
                    keyword.Keyword,
                    duration = keyword.Duration.ToString(),
                    keyword.AppliedBySide,
                    keyword.EndPhasesSeen,
                })
                .OrderBy(value => value.Keyword, StringComparer.Ordinal)
                .ThenBy(value => value.duration, StringComparer.Ordinal)
                .ThenBy(value => value.AppliedBySide)
                .ThenBy(value => value.EndPhasesSeen)
                .ToArray(),
            card.CannotActivateNextReset,
            card.IsLifeFaceUp,
            card.TurnPlayed,
            oncePerTurnUsedKeys = card.OncePerTurnUsedKeys.Order(StringComparer.Ordinal).ToArray(),
            card.CostModThisTurn,
            card.CostModPersistent,
            costModsUntilOppEnd = card.CostModsUntilOppEnd
                .Select(modifier => new { modifier.Delta, modifier.AppliedBySide, modifier.EndPhasesSeen })
                .OrderBy(value => value.Delta)
                .ThenBy(value => value.AppliedBySide)
                .ThenBy(value => value.EndPhasesSeen)
                .ToArray(),
            card.OriginalPowerOverride,
            originalPowerOverridesUntilOppEnd = card.OriginalPowerOverridesUntilOppEnd
                .Select(overrideValue => new
                {
                    overrideValue.Value,
                    overrideValue.AppliedBySide,
                    overrideValue.EndPhasesSeen,
                })
                .OrderBy(value => value.Value)
                .ThenBy(value => value.AppliedBySide)
                .ThenBy(value => value.EndPhasesSeen)
                .ToArray(),
            card.IsEffectsNullified,
            restrictions = card.Restrictions
                .Select(restriction => new
                {
                    kind = restriction.Kind.ToString(),
                    duration = restriction.Duration.ToString(),
                    restriction.AppliedBySide,
                    restriction.EndPhasesSeen,
                })
                .OrderBy(value => value.kind, StringComparer.Ordinal)
                .ThenBy(value => value.duration, StringComparer.Ordinal)
                .ThenBy(value => value.AppliedBySide)
                .ThenBy(value => value.EndPhasesSeen)
                .ToArray(),
            card.NoAttackCostLeThisTurn,
            card.BattledOpponentCharacterThisTurn,
            nameAliases = card.NameAliases.Order(StringComparer.Ordinal).ToArray(),
            gainedPropertiesThisTurn = card.GainedPropertiesThisTurn.Order(StringComparer.Ordinal).ToArray(),
            fieldSnapshotSourceIds = SortedIds(card.FieldSnapshotSourceIds),
        };

    private static object Don(DonCard don)
        => new
        {
            id = Id(don.Id),
            state = don.State.ToString(),
            attachedToCardId = don.AttachedToCardId is null ? null : Id(don.AttachedToCardId.Value),
            don.CannotActivateNextReset,
        };

    private static object? FullPrompt(PendingPrompt? prompt)
        => prompt is null ? null : new
        {
            prompt.PromptId,
            prompt.PlayerIndex,
            prompt.Kind,
            validChoices = prompt.ValidChoices.ToArray(),
            prompt.MinChoose,
            prompt.MaxChoose,
            prompt.ResumeKey,
            prompt.Extra,
        };

    private static object? PublicPrompt(PendingPrompt? prompt)
        => prompt is null ? null : new
        {
            prompt.PlayerIndex,
            prompt.Kind,
            prompt.MinChoose,
            prompt.MaxChoose,
            validChoiceCount = prompt.ValidChoices.Count,
        };

    private static object? Battle(BattleContext? battle)
        => battle is null ? null : new
        {
            battle.AttackerPlayerIndex,
            attackerCardId = Id(battle.AttackerCardId),
            targetCardId = battle.TargetCardId is null ? null : Id(battle.TargetCardId.Value),
            battle.TargetIsLeader,
            battle.DefenderPlayerIndex,
            replacedByBlockerCardId = battle.ReplacedByBlockerCardId is null
                ? null
                : Id(battle.ReplacedByBlockerCardId.Value),
            countersUsed = battle.CountersUsed.Select(Id).ToArray(),
            battle.BlockerDeclared,
            battle.AttackerBattleBonus,
            battle.DefenderBattleBonus,
        };

    private static object ContinuousEffect(ContinuousEffect effect)
        => new
        {
            effect.SourceCardId,
            effect.SourceCardNumber,
            effect.ExpiresAtEndOfTurnForSide,
            scope = new
            {
                effect.Scope.Side,
                effect.Scope.IncludeLeader,
                effect.Scope.IncludeCharacters,
                effect.Scope.IncludeHand,
                filter = DelegateIdentity(effect.Scope.Filter),
            },
            effect.PowerDelta,
            powerDeltaResolver = DelegateIdentity(effect.PowerDeltaResolver),
            effect.OriginalPowerOverride,
            effect.CostDelta,
            costDeltaResolver = DelegateIdentity(effect.CostDeltaResolver),
            effect.GrantKeyword,
            effect.KoGuard,
            effect.DiscardHandKoReplacement,
            effect.LeaveGuard,
            effect.NullifyEffect,
            nullifyOnlyTrigger = effect.NullifyOnlyTrigger?.ToString(),
            effect.PreventReset,
            grantRestriction = effect.GrantRestriction?.ToString(),
            predicate = DelegateIdentity(effect.Predicate),
        };

    private static string? DelegateIdentity(Delegate? value)
    {
        if (value is null) return null;
        var method = value.GetMethodInfo();
        return $"{method.DeclaringType?.FullName ?? "<global>"}:{method.Name}";
    }

    private static string Id(Guid id) => id.ToString("D");

    private static string[] SortedIds(IEnumerable<Guid>? ids)
        => ids?.Select(Id).Order(StringComparer.Ordinal).ToArray() ?? [];
}

public sealed record ReplayTerminalSemantics(
    int? WinnerIndex,
    bool IsDraw,
    string Reason,
    int TurnCount,
    string StableHash)
{
    public static ReplayTerminalSemantics Capture(GameState state)
    {
        var reason = state.GameOverReason ?? string.Empty;
        var reasonCategory = ReasonCategory(reason, state.IsDraw);
        var canonical = JsonSerializer.SerializeToElement(new
        {
            state.WinnerIndex,
            state.IsDraw,
            reasonCategory,
            state.TurnCount,
        });
        return new ReplayTerminalSemantics(
            state.WinnerIndex,
            state.IsDraw,
            reason,
            state.TurnCount,
            CanonicalJson.Hash(canonical));
    }

    /// <summary>
    /// 将包含玩家展示名的终局文案收敛为身份无关的规则类别。未知类别必须由 verifier
    /// 退回原文精确比较，避免新增终局路径被静默视为等价。
    /// </summary>
    public static string ReasonCategory(string? reason, bool isDraw)
    {
        if (isDraw) return "draw";
        if (string.IsNullOrWhiteSpace(reason)) return "unspecified";

        var value = reason.Trim();
        if (KnownReasonCategories.Contains(value)) return value;
        if (value.Contains("卡组耗尽（规则替换：胜利）", StringComparison.Ordinal))
            return "deck_out_replacement_win";
        if (value.Contains("卡组耗尽", StringComparison.Ordinal)) return "deck_out";
        if (value.Contains("生命耗尽", StringComparison.Ordinal)) return "life_out";
        if (value.Contains("投降", StringComparison.Ordinal)) return "surrender";
        if (value.Contains("总操作时间耗尽", StringComparison.Ordinal))
            return "total_operation_timeout";
        if (value.Contains("本回合操作时间耗尽", StringComparison.Ordinal))
            return "turn_operation_timeout";
        if (value.Contains("连续 4 分钟没有操作", StringComparison.Ordinal))
            return "inactivity_timeout";
        if (value.Contains("断线，对手确认结束对局", StringComparison.Ordinal))
            return "disconnect_confirmed_end";
        if (value.Contains("断线超时", StringComparison.Ordinal))
            return "disconnect_timeout";
        return "unclassified";
    }

    private static readonly HashSet<string> KnownReasonCategories = new(StringComparer.Ordinal)
    {
        "draw",
        "unspecified",
        "deck_out_replacement_win",
        "deck_out",
        "life_out",
        "surrender",
        "total_operation_timeout",
        "turn_operation_timeout",
        "inactivity_timeout",
        "disconnect_confirmed_end",
        "disconnect_timeout",
        "unclassified",
    };
}
