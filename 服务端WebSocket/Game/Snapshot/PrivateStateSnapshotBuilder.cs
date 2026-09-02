namespace GrandUMI.Game.Snapshot;

public static class PrivateStateSnapshotBuilder
{
    public static object Build(GameState state)
    {
        return new
        {
            roomId = state.RoomId,
            rulesetId = state.RulesetId,
            tick = state.Tick,
            phase = PhaseLabels.Of(state.Phase),
            currentTurnPlayer = state.CurrentTurnPlayer,
            attachDonOperationSequence = state.AttachDonOperationSequence,
            attachDonUndoStack = state.AttachDonUndoStack.Select(entry => new
            {
                operationSequence = entry.OperationSequence,
                playerIndex = entry.PlayerIndex,
                targetId = entry.TargetId,
                targetCardId = entry.TargetCardId.ToString(),
                donIds = entry.DonIds.Select(id => id.ToString()).ToArray(),
            }).ToArray(),
            turnCount = state.TurnCount,
            firstPlayer = state.FirstPlayer,
            firstPlayerChosen = state.StartingPlayerChosen,
            openingStage = state.PendingPrompt is not null
                && state.OpeningStage == OpeningStage.ResolvingOpeningEffects
                    ? "WaitingOpeningPrompt"
                    : state.OpeningStage.ToString(),
            startingPlayerChooser = state.StartingPlayerChooser,
            startingPlayerChoiceDeadlineUtc = state.StartingPlayerChoiceDeadlineUtc,
            startingDiceRolls = state.StartingDiceRounds.Select(round => new
            {
                player0 = round.Player0,
                player1 = round.Player1,
            }).ToArray(),
            rngSeed = state.RngSeed,
            randomSeq = state.RandomSeq,
            mulliganBothDone = state.MulliganBothDone,
            mulliganDeadlineUtc = state.MulliganDeadlineUtc,
            operationClockEnabled = state.OperationClockEnabled,
            operationClockRemainingMs = state.OperationClockRemainingMs.ToArray(),
            operationTurnClockRemainingMs = state.OperationTurnClockRemainingMs.ToArray(),
            operationTurnClockTurnCount = state.OperationTurnClockTurnCount,
            operationTurnExtensionUsed = state.OperationTurnExtensionUsed.ToArray(),
            inactivityActivePlayer = state.InactivityActivePlayer,
            inactivityWarningActive = state.InactivityWarningActive,
            inactivityLossRemainingMs = state.InactivityLossRemainingMs,
            inactivitySyncUtc = state.InactivitySyncUtc,
            operationClockActivePlayer = state.OperationClockActivePlayer,
            operationClockSyncUtc = state.OperationClockSyncUtc,
            operationClockPaused = state.OperationClockPaused,
            matchKind = state.MatchKind.ToString(),
            hexState = new
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
                    inventorFirstUseKeys = runtime.InventorFirstUseKeys.Order().ToArray(),
                }).ToArray(),
                activeDraft = state.HexState.ActiveDraft is { } draft
                    ? new
                    {
                        draft.RoundId,
                        draft.PlayerIndex,
                        draft.OwnTurnNumber,
                        tier = draft.Tier.ToString(),
                        draft.DeadlineUtc,
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
            },
            isGameOver = state.IsGameOver,
            isDraw = state.IsDraw,
            winnerIndex = state.WinnerIndex,
            gameOverReason = state.GameOverReason,
            pendingDrawRequester = state.PendingDrawRequester,
            pendingDrawRequestDescription = state.PendingDrawRequestDescription,
            drawRequestRejectionCounts = state.DrawRequestRejectionCounts,
            players = state.Players.Select((p, idx) => SnapshotPlayer(state, p, idx)).ToArray(),
            pendingPrompt = state.PendingPrompt is { } prompt
                ? new
                {
                    promptId = prompt.PromptId,
                    playerIndex = prompt.PlayerIndex,
                    kind = prompt.Kind,
                    validChoices = prompt.ValidChoices,
                    minChoose = prompt.MinChoose,
                    maxChoose = prompt.MaxChoose,
                    text = prompt.PromptText,
                    resumeKey = prompt.ResumeKey,
                    extra = prompt.Extra,
                }
                : null,
            currentBattle = state.CurrentBattle is { } battle
                ? new
                {
                    attackerPlayerIndex = battle.AttackerPlayerIndex,
                    attackerCardId = battle.AttackerCardId.ToString(),
                    targetCardId = battle.TargetCardId?.ToString(),
                    battle.TargetIsLeader,
                    defenderPlayerIndex = battle.DefenderPlayerIndex,
                    replacedByBlockerCardId = battle.ReplacedByBlockerCardId?.ToString(),
                    countersUsed = battle.CountersUsed.Select(id => id.ToString()).ToArray(),
                    battle.BlockerDeclared,
                    battle.AttackerBattleBonus,
                    battle.DefenderBattleBonus,
                }
                : null,
            continuousEffects = state.ContinuousEffects.Select(e => new
            {
                e.SourceCardId,
                scope = new
                {
                    e.Scope.Side,
                    e.Scope.IncludeLeader,
                    e.Scope.IncludeCharacters,
                    hasFilter = e.Scope.Filter is not null,
                },
                e.PowerDelta,
                e.OriginalPowerOverride,
            }).ToArray(),
        };
    }

    private static object SnapshotPlayer(GameState state, PlayerState player, int playerIndex)
    {
        return new
        {
            index = playerIndex,
            accountName = player.AccountName,
            displayName = player.VisibleName,
            cardBackId = player.CardBackId,
            leader = SnapshotCard(state, playerIndex, player.Leader),
            hand = player.Hand.Select(c => SnapshotCard(state, playerIndex, c)).ToArray(),
            characters = player.Characters.Select(c => SnapshotCard(state, playerIndex, c)).ToArray(),
            stage = player.StageCard is null ? null : SnapshotCard(state, playerIndex, player.StageCard),
            extraStage = player.ExtraStageCard is null ? null : SnapshotCard(state, playerIndex, player.ExtraStageCard),
            trash = player.Trash.Select(c => SnapshotCard(state, playerIndex, c)).ToArray(),
            deck = player.Deck.Select(c => SnapshotCard(state, playerIndex, c)).ToArray(),
            life = player.LifeArea.Select(c => SnapshotCard(state, playerIndex, c)).ToArray(),
            donDeck = player.DonDeck.Select(SnapshotDon).ToArray(),
            costArea = player.CostArea.Select(SnapshotDon).ToArray(),
            activeDonCount = player.ActiveDonCount,
            restDonCount = player.RestDonCount,
            totalDonInCostArea = player.TotalDonInCostArea,
            hasReDraw = player.HasReDraw,
            mulliganDone = player.MulliganDone,
            alwaysPromptOnLifeReveal = player.AlwaysPromptOnLifeReveal,
            turnOnceUsed = player.TurnOnceUsed.ToArray(),
        };
    }

    private static object SnapshotCard(GameState state, int ownerIndex, CardInstance card)
    {
        return new
        {
            id = card.Id.ToString(),
            number = card.Info.Number,
            name = card.Info.Name,
            color = card.Info.Color,
            kind = card.Info.Kind.ToString(),
            property = card.CurrentProperty,
            basePower = card.Info.Power,
            baseCost = card.Info.Cost,
            currentCost = card.CurrentCost(),
            counter = Effects.HandStaticCounter.Value(state, ownerIndex, card),
            keywords = card.Info.Keywords,
            isTapped = card.IsTapped,
            turnPlayed = card.TurnPlayed,
            powerModThisTurn = card.PowerModThisTurn,
            powerModThisBattle = card.PowerModThisBattle,
            powerModPersistent = card.PowerModPersistent,
            costModThisTurn = card.CostModThisTurn,
            costModPersistent = card.CostModPersistent,
            originalPowerOverride = card.OriginalPowerOverride,
            originalPowerOverridesUntilOppEnd = card.OriginalPowerOverridesUntilOppEnd.Select(x => new
            {
                x.Value,
                x.AppliedBySide,
                x.EndPhasesSeen,
            }).ToArray(),
            isEffectsNullified = card.IsEffectsNullified,
            cannotActivateNextReset = card.CannotActivateNextReset,
            gainedKeywords = card.GainedKeywords.Select(k => new
            {
                k.Keyword,
                duration = k.Duration.ToString(),
            }).ToArray(),
            restrictions = card.Restrictions.Select(r => new
            {
                kind = r.Kind.ToString(),
                duration = r.Duration.ToString(),
            }).ToArray(),
            oncePerTurnUsedKeys = card.OncePerTurnUsedKeys.ToArray(),
            nameAliases = card.NameAliases.ToArray(),
            fieldSnapshotSourceIds = card.FieldSnapshotSourceIds
                .Select(id => id.ToString("D"))
                .Order(StringComparer.Ordinal)
                .ToArray(),
        };
    }

    private static object SnapshotDon(DonCard don)
    {
        return new
        {
            id = don.Id.ToString(),
            state = don.State.ToString(),
            attachedToCardId = don.AttachedToCardId?.ToString(),
        };
    }
}
