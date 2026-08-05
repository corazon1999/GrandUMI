using System.Text.Json;

namespace GrandUMI.Game.Snapshot;

/// <summary>把 GameState 编码为按视角脱敏的客户端快照。</summary>
public static class StateSnapshotBuilder
{
    public sealed record SnapshotSet(object Player0, object Player1, object Spectator);

    /// <summary>单视角构建，供重连和单个观战者加入时使用。</summary>
    public static object Build(GameState state, int viewerIndex, string? lastAction = null, object? actionPayload = null,
        IReadOnlyList<ActionLogEvent>? queuedLogEvents = null)
    {
        var boards = new[] { ComputePlayerBoard(state, 0), ComputePlayerBoard(state, 1) };
        return BuildForViewer(state, viewerIndex, lastAction, ComputePayload(actionPayload), boards, queuedLogEvents);
    }

    /// <summary>
    /// 一次构建双方玩家和观战三种视角。双方公开牌桌、力量、关键词和攻击合法性
    /// 每个玩家只计算一次，避免原先三份快照各自重复遍历。
    /// </summary>
    public static SnapshotSet BuildAll(GameState state, string? lastAction = null, object? actionPayload = null,
        IReadOnlyList<ActionLogEvent>? queuedLogEvents = null)
    {
        var boards = new[] { ComputePlayerBoard(state, 0), ComputePlayerBoard(state, 1) };
        var payload = ComputePayload(actionPayload);
        return new SnapshotSet(
            BuildForViewer(state, 0, lastAction, payload, boards, queuedLogEvents),
            BuildForViewer(state, 1, lastAction, payload, boards, queuedLogEvents),
            BuildForViewer(state, -1, lastAction, payload, boards, queuedLogEvents));
    }

    private static object BuildForViewer(GameState state, int viewerIndex, string? lastAction,
        PayloadComputed payload, PlayerBoardComputed[] boards, IReadOnlyList<ActionLogEvent>? queuedLogEvents)
    {
        var isSpectator = viewerIndex < 0;
        var myIdx = isSpectator ? 0 : viewerIndex;
        var oppIdx = isSpectator ? 1 : 1 - viewerIndex;
        var my = BuildPlayerSnapshot(state, myIdx, asSelf: !isSpectator, boards[myIdx]);
        var opponent = BuildPlayerSnapshot(state, oppIdx, asSelf: false, boards[oppIdx]);

        var logLines = new List<string>();
        if (queuedLogEvents is not null)
        {
            foreach (var item in queuedLogEvents)
            {
                var eventPayload = ComputePayload(item.Payload);
                var text = ActionLogFormatter.Format(state, viewerIndex, item.Action, eventPayload.Element);
                if (!string.IsNullOrWhiteSpace(text)) logLines.Add(text);
            }
        }
        var currentLine = ActionLogFormatter.Format(state, viewerIndex, lastAction ?? "", payload.Element);
        if (!string.IsNullOrWhiteSpace(currentLine)) logLines.Add(currentLine);
        var logLine = logLines.LastOrDefault() ?? "";

        return new
        {
            proto = "MsgGameState",
            tick = state.Tick,
            phase = PhaseLabels.Of(state.Phase),
            currentTurn = !isSpectator && state.CurrentTurnPlayer == myIdx,
            turnCount = state.TurnCount,
            firstPlayer = state.FirstPlayer,
            mulliganBothDone = state.MulliganBothDone,
            isGameOver = state.IsGameOver,
            winnerIsMe = !isSpectator && state.WinnerIndex == myIdx,
            gameOverReason = state.GameOverReason,
            viewerKind = isSpectator ? "spectator" : "player",
            lastAction = lastAction ?? "",
            actionPayload = payload.Json,
            logLine,
            logLines = logLines.ToArray(),
            my,
            opponent,
            // 观战者永远不能拿到选择候选，避免隐藏区信息泄露。
            pendingPrompt = !isSpectator && state.PendingPrompt is { } p && p.PlayerIndex == myIdx
                ? new
                {
                    promptId = p.PromptId,
                    kind = p.Kind,
                    text = p.PromptText,
                    validChoices = p.ValidChoices,
                    minChoose = p.MinChoose,
                    maxChoose = p.MaxChoose,
                    extra = p.Extra,
                }
                : null,
            reveal = state.PendingReveal is { } rv
                ? new
                {
                    side = (!isSpectator && rv.OwnerIndex == myIdx) ? "my" : "opponent",
                    cardNumbers = rv.CardNumbers,
                }
                : null,
            battle = state.CurrentBattle is { } b
                ? new
                {
                    attackerPlayer = b.AttackerPlayerIndex,
                    attackerCardId = b.AttackerCardId.ToString(),
                    targetIsLeader = b.TargetIsLeader,
                    targetCardId = b.TargetCardId?.ToString(),
                    blockerCardId = b.ReplacedByBlockerCardId?.ToString(),
                    attackerBonus = b.AttackerBattleBonus,
                    defenderBonus = b.DefenderBattleBonus,
                }
                : null,
        };
    }

    private static PayloadComputed ComputePayload(object? actionPayload)
        => actionPayload is null
            ? new PayloadComputed(default, "")
            : new PayloadComputed(JsonSerializer.SerializeToElement(actionPayload), JsonSerializer.Serialize(actionPayload));

    private static PlayerBoardComputed ComputePlayerBoard(GameState state, int idx)
    {
        var p = state.Players[idx];
        var fieldCards = p.Characters.Select(c => (object)new
        {
            id = c.Id.ToString(),
            number = c.Info.Number,
            isTapped = c.IsTapped,
            powerCurrent = state.CurrentPowerOf(idx, c),
            cost = state.CurrentCostOf(idx, c),
            attachedDon = p.AttachedDonCount(c.Id),
            gainedKeywords = c.GainedKeywords.Select(k => k.Keyword)
                .Concat(ContinuousGrantedKeywords(state, c)).Distinct().ToArray(),
            cannotActivateNextReset = c.CannotActivateNextReset,
            cannotBeRested = c.HasRestriction(RestrictionKind.CannotBeRested)
                || state.HasContinuousRestriction(c, RestrictionKind.CannotBeRested),
            turnPlayed = c.TurnPlayed,
            canAttack = Validation.ActionValidator.CanAttack(state, idx, c.Id, true, null).Ok,
            activatedUsedThisTurn = ActivatedUsedThisTurn(p, c),
        }).ToArray();

        return new PlayerBoardComputed(
            p.AccountName,
            p.Hand.Count,
            fieldCards,
            p.StageCard?.Info.Number,
            p.StageCard?.Id.ToString(),
            p.StageCard?.IsTapped ?? false,
            p.StageCard is not null && ActivatedUsedThisTurn(p, p.StageCard),
            p.Trash.Select(c => c.Info.Number).ToArray(),
            p.DeckCount,
            p.LifeCount,
            p.LifeArea.Select(c => (object)new
            {
                faceUp = c.IsLifeFaceUp,
                number = c.IsLifeFaceUp ? c.Info.Number : null,
            }).ToArray(),
            p.Leader.Id.ToString(),
            p.Leader.Info.Number,
            p.Leader.IsTapped,
            state.CurrentPowerOf(idx, p.Leader),
            p.AttachedDonCount(p.Leader.Id),
            Validation.ActionValidator.CanAttack(state, idx, p.Leader.Id, true, null).Ok,
            ActivatedUsedThisTurn(p, p.Leader),
            p.ActiveDonCount,
            p.RestDonCount,
            p.CostArea.Count(d => d.State == DonState.Attached),
            p.DonDeck.Count,
            p.HasReDraw,
            p.MulliganDone);
    }

    private static object BuildPlayerSnapshot(GameState state, int idx, bool asSelf, PlayerBoardComputed board)
    {
        var p = state.Players[idx];
        return new
        {
            name = board.Name,
            handCardNumbers = asSelf ? p.Hand.Select(c => c.Info.Number).ToArray() : Array.Empty<string>(),
            handCardCosts = asSelf ? p.Hand.Select(c => state.HandPlayCost(idx, c)).ToArray() : Array.Empty<int>(),
            handCardCounters = asSelf ? p.Hand.Select(c => Effects.HandStaticCounter.Value(state, idx, c)).ToArray() : Array.Empty<int>(),
            handCount = board.HandCount,
            fieldCards = board.FieldCards,
            stageNumber = board.StageNumber,
            stageId = board.StageId,
            stageTapped = board.StageTapped,
            stageActivatedUsedThisTurn = board.StageActivatedUsedThisTurn,
            trashNumbers = board.TrashNumbers,
            deckCount = board.DeckCount,
            lifeCount = board.LifeCount,
            lifeNumbers = Array.Empty<string>(),
            lifeFaceUp = board.LifeFaceUp,
            leaderId = board.LeaderId,
            leaderNumber = board.LeaderNumber,
            leaderTapped = board.LeaderTapped,
            leaderPower = board.LeaderPower,
            leaderAttachedDon = board.LeaderAttachedDon,
            leaderCanAttack = board.LeaderCanAttack,
            leaderActivatedUsedThisTurn = board.LeaderActivatedUsedThisTurn,
            costActive = board.CostActive,
            costRest = board.CostRest,
            costAttached = board.CostAttached,
            donDeckCount = board.DonDeckCount,
            hasReDraw = board.HasReDraw,
            mulliganDone = board.MulliganDone,
        };
    }

    private sealed record PlayerBoardComputed(
        string Name,
        int HandCount,
        object[] FieldCards,
        string? StageNumber,
        string? StageId,
        bool StageTapped,
        bool StageActivatedUsedThisTurn,
        string[] TrashNumbers,
        int DeckCount,
        int LifeCount,
        object[] LifeFaceUp,
        string LeaderId,
        string LeaderNumber,
        bool LeaderTapped,
        int LeaderPower,
        int LeaderAttachedDon,
        bool LeaderCanAttack,
        bool LeaderActivatedUsedThisTurn,
        int CostActive,
        int CostRest,
        int CostAttached,
        int DonDeckCount,
        bool HasReDraw,
        bool MulliganDone);

    private sealed record PayloadComputed(JsonElement Element, string Json);

    private static bool ActivatedUsedThisTurn(PlayerState p, CardInstance c)
        => p.TurnOnceUsed.Contains($"{c.Id}-Activated")
           || p.TurnOnceUsed.Contains($"{c.Info.Number}-act:{c.Id}");

    private static readonly string[] GrantableKeywords =
        { "阻挡者", "速攻", "双重攻击", "不可阻挡", "流放", "可攻击活跃" };

    private static IEnumerable<string> ContinuousGrantedKeywords(GameState state, CardInstance c)
        => GrantableKeywords.Where(kw => state.HasContinuousKeyword(c, kw));
}
