namespace GrandUMI.Game.Snapshot;

public static class PrivateStateSnapshotBuilder
{
    public static object Build(GameState state)
    {
        return new
        {
            roomId = state.RoomId,
            tick = state.Tick,
            phase = PhaseLabels.Of(state.Phase),
            currentTurnPlayer = state.CurrentTurnPlayer,
            turnCount = state.TurnCount,
            firstPlayer = state.FirstPlayer,
            firstPlayerChosen = state.StartingPlayerChosen,
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
            isGameOver = state.IsGameOver,
            winnerIndex = state.WinnerIndex,
            gameOverReason = state.GameOverReason,
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
            property = card.Info.Property,
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
