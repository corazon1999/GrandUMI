using GrandUMI.Cards;

namespace GrandUMI.Game.Snapshot;

/// <summary>
/// 把 GameState 编码为客户端可消费的 MsgGameState（双方各自一份，按视角脱敏）
/// </summary>
public static class StateSnapshotBuilder
{
    /// <summary>
    /// 构建发给 viewerIndex 的快照。viewerIndex = -1 表示观战视角（双方手牌/生命都脱敏）
    /// </summary>
    public static object Build(GameState state, int viewerIndex, string? lastAction = null, object? actionPayload = null)
    {
        bool isSpectator = viewerIndex < 0;
        int myIdx  = isSpectator ? 0 : viewerIndex;
        int oppIdx = isSpectator ? 1 : 1 - viewerIndex;

        var my  = SnapshotForPlayer(state, myIdx,  asSelf: !isSpectator);
        var opp = SnapshotForPlayer(state, oppIdx, asSelf: false);

        return new
        {
            proto         = "MsgGameState",
            tick          = state.Tick,
            phase         = PhaseLabels.Of(state.Phase),
            currentTurn   = !isSpectator && state.CurrentTurnPlayer == myIdx,
            turnCount     = state.TurnCount,
            firstPlayer   = state.FirstPlayer,
            mulliganBothDone = state.MulliganBothDone,
            isGameOver    = state.IsGameOver,
            winnerIsMe    = !isSpectator && state.WinnerIndex == myIdx,
            gameOverReason = state.GameOverReason,
            viewerKind    = isSpectator ? "spectator" : "player",
            lastAction    = lastAction ?? "",
            actionPayload = actionPayload is null ? "" : System.Text.Json.JsonSerializer.Serialize(actionPayload),
            my            = my,
            opponent      = opp,
            pendingPrompt = state.PendingPrompt is { } p && p.PlayerIndex == myIdx
                ? new {
                    promptId   = p.PromptId,
                    kind       = p.Kind,
                    text       = p.PromptText,
                    validChoices = p.ValidChoices,
                    minChoose  = p.MinChoose,
                    maxChoose  = p.MaxChoose,
                    extra      = p.Extra,
                  }
                : null,
            battle = state.CurrentBattle is { } b
                ? new {
                    attackerPlayer = b.AttackerPlayerIndex,
                    attackerCardId = b.AttackerCardId.ToString(),
                    targetIsLeader = b.TargetIsLeader,
                    targetCardId   = b.TargetCardId?.ToString(),
                    blockerCardId  = b.ReplacedByBlockerCardId?.ToString(),
                    attackerBonus  = b.AttackerBattleBonus,
                    defenderBonus  = b.DefenderBattleBonus,
                  }
                : null,
        };
    }

    private static object SnapshotForPlayer(GameState state, int idx, bool asSelf)
    {
        var p = state.Players[idx];
        var ownerTurn = state.CurrentTurnPlayer == idx;

        return new
        {
            name           = p.AccountName,
            handCardNumbers = asSelf ? p.Hand.Select(c => c.Info.Number).ToArray() : Array.Empty<string>(),
            handCount      = p.Hand.Count,
            fieldCards     = p.Characters.Select(c => new
            {
                id           = c.Id.ToString(),
                number       = c.Info.Number,
                isTapped     = c.IsTapped,
                powerCurrent = c.CurrentPower(p.AttachedDonCount(c.Id), ownerTurn),
                attachedDon  = p.AttachedDonCount(c.Id),
                gainedKeywords = c.GainedKeywords.Select(k => k.Keyword).ToArray(),
                cannotActivateNextReset = c.CannotActivateNextReset,
                turnPlayed   = c.TurnPlayed,
            }).ToArray(),
            stageNumber    = p.StageCard?.Info.Number,
            stageId        = p.StageCard?.Id.ToString(),
            trashNumbers   = p.Trash.Select(c => c.Info.Number).ToArray(),
            deckCount      = p.DeckCount,
            lifeCount      = p.LifeCount,
            // 生命牌内容：永远不发给对手；自己也只在加入手牌前不知道（生命区背面朝上规则）
            // ↓ 故 lifeNumbers 始终为空数组，触发流程时单独通过 prompt 公开
            lifeNumbers    = Array.Empty<string>(),
            leaderId       = p.Leader.Id.ToString(),
            leaderNumber   = p.Leader.Info.Number,
            leaderTapped   = p.Leader.IsTapped,
            leaderPower    = p.Leader.CurrentPower(p.AttachedDonCount(p.Leader.Id), ownerTurn),
            leaderAttachedDon = p.AttachedDonCount(p.Leader.Id),
            costActive     = p.ActiveDonCount,
            costRest       = p.RestDonCount,
            costAttached   = p.CostArea.Count(d => d.State == DonState.Attached),
            donDeckCount   = p.DonDeck.Count,
            hasReDraw      = p.HasReDraw,
            mulliganDone   = p.MulliganDone,
        };
    }
}
