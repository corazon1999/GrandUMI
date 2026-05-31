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
            rngSeed = state.RngSeed,
            randomSeq = state.RandomSeq,
            mulliganBothDone = state.MulliganBothDone,
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
            }).ToArray(),
        };
    }

    private static object SnapshotPlayer(GameState state, PlayerState player, int playerIndex)
    {
        return new
        {
            index = playerIndex,
            accountName = player.AccountName,
            leader = SnapshotCard(player.Leader),
            hand = player.Hand.Select(SnapshotCard).ToArray(),
            characters = player.Characters.Select(SnapshotCard).ToArray(),
            stage = player.StageCard is null ? null : SnapshotCard(player.StageCard),
            trash = player.Trash.Select(SnapshotCard).ToArray(),
            deck = player.Deck.Select(SnapshotCard).ToArray(),
            life = player.LifeArea.Select(SnapshotCard).ToArray(),
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

    private static object SnapshotCard(CardInstance card)
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
            counter = card.Info.Counter,
            keywords = card.Info.Keywords,
            isTapped = card.IsTapped,
            turnPlayed = card.TurnPlayed,
            powerModThisTurn = card.PowerModThisTurn,
            powerModThisBattle = card.PowerModThisBattle,
            powerModPersistent = card.PowerModPersistent,
            costModThisTurn = card.CostModThisTurn,
            costModPersistent = card.CostModPersistent,
            originalPowerOverride = card.OriginalPowerOverride,
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
