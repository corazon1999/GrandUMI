using System.Text.Json;
using GrandUMI.Game.Stats;

namespace GrandUMI.Game.Snapshot;

/// <summary>把 GameState 编码为按视角脱敏的客户端快照。</summary>
public static class StateSnapshotBuilder
{
    /// <summary>
    /// 回放专用的双方手牌与生命区变化帧。只保存牌号与发生变化的 Tick，
    /// 对局结束前不得下发给客户端。
    /// </summary>
    public sealed record ReplayHandFrame(
        int Tick,
        string[] Player0CardNumbers,
        string[] Player1CardNumbers,
        string[] Player0LifeCardNumbers,
        string[] Player1LifeCardNumbers);

    public sealed record SnapshotSet(object Player0, object Player1, object Spectator)
    {
        public object? SpectatorPlayer1 { get; init; }
        public object? SpectatorPlayer0Hand { get; init; }
        public object? SpectatorPlayer1Hand { get; init; }
    }

    /// <summary>单视角构建，供重连和单个观战者加入时使用。</summary>
    public static object Build(GameState state, int viewerIndex, string? lastAction = null, object? actionPayload = null,
        IReadOnlyList<ActionLogEvent>? queuedLogEvents = null, string? requestId = null,
        IReadOnlyList<EffectActivationEvent>? effectActivations = null, int spectatorPlayerIndex = 0,
        bool revealSpectatorMainHand = false, DateTime? serverNowUtc = null)
    {
        var snapshotServerNowUtc = serverNowUtc ?? DateTime.UtcNow;
        var boards = new[] { ComputePlayerBoard(state, 0), ComputePlayerBoard(state, 1) };
        return BuildForViewer(
            state,
            viewerIndex,
            lastAction,
            ComputePayload(actionPayload),
            boards,
            queuedLogEvents,
            requestId,
            effectActivations,
            spectatorPlayerIndex,
            snapshotServerNowUtc,
            revealSpectatorMainHand: revealSpectatorMainHand);
    }

    /// <summary>
    /// 一次构建双方玩家和观战三种视角。双方公开牌桌、力量、关键词和攻击合法性
    /// 每个玩家只计算一次，避免原先三份快照各自重复遍历。
    /// </summary>
    public static SnapshotSet BuildAll(GameState state, string? lastAction = null, object? actionPayload = null,
        IReadOnlyList<ActionLogEvent>? queuedLogEvents = null, string? requestId = null,
        IReadOnlyList<EffectActivationEvent>? effectActivations = null, bool includePlayer1Spectator = false,
        bool includePlayer0SpectatorHand = false, bool includePlayer1SpectatorHand = false,
        IReadOnlyList<ReplayHandFrame>? replayHandTimeline = null, DateTime? serverNowUtc = null)
    {
        var snapshotServerNowUtc = serverNowUtc ?? DateTime.UtcNow;
        var boards = new[] { ComputePlayerBoard(state, 0), ComputePlayerBoard(state, 1) };
        var payload = ComputePayload(actionPayload);
        return new SnapshotSet(
            BuildForViewer(state, 0, lastAction, payload, boards, queuedLogEvents, requestId, effectActivations, 0, snapshotServerNowUtc, replayHandTimeline),
            BuildForViewer(state, 1, lastAction, payload, boards, queuedLogEvents, requestId, effectActivations, 0, snapshotServerNowUtc, replayHandTimeline),
            BuildForViewer(state, -1, lastAction, payload, boards, queuedLogEvents, requestId, effectActivations, 0, snapshotServerNowUtc, replayHandTimeline))
        {
            SpectatorPlayer1 = includePlayer1Spectator
                ? BuildForViewer(state, -1, lastAction, payload, boards, queuedLogEvents, requestId, effectActivations, 1, snapshotServerNowUtc, replayHandTimeline)
                : null,
            SpectatorPlayer0Hand = includePlayer0SpectatorHand
                ? BuildForViewer(state, -1, lastAction, payload, boards, queuedLogEvents, requestId, effectActivations, 0, snapshotServerNowUtc, replayHandTimeline, true)
                : null,
            SpectatorPlayer1Hand = includePlayer1SpectatorHand
                ? BuildForViewer(state, -1, lastAction, payload, boards, queuedLogEvents, requestId, effectActivations, 1, snapshotServerNowUtc, replayHandTimeline, true)
                : null,
        };
    }

    private static object BuildForViewer(GameState state, int viewerIndex, string? lastAction,
        PayloadComputed payload, PlayerBoardComputed[] boards, IReadOnlyList<ActionLogEvent>? queuedLogEvents,
        string? requestId, IReadOnlyList<EffectActivationEvent>? effectActivations, int spectatorPlayerIndex,
        DateTime serverNowUtc,
        IReadOnlyList<ReplayHandFrame>? replayHandTimeline = null,
        bool revealSpectatorMainHand = false)
    {
        var isSpectator = viewerIndex < 0;
        var myIdx = isSpectator ? Math.Clamp(spectatorPlayerIndex, 0, 1) : viewerIndex;
        var oppIdx = 1 - myIdx;
        var my = BuildPlayerSnapshot(
            state,
            myIdx,
            asSelf: !isSpectator,
            revealHand: state.IsGameOver || (isSpectator && revealSpectatorMainHand),
            board: boards[myIdx]);
        var opponent = BuildPlayerSnapshot(
            state,
            oppIdx,
            asSelf: false,
            revealHand: state.IsGameOver,
            board: boards[oppIdx]);

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
            serverNowUtc,
            rulesetId = state.RulesetId,
            phase = PhaseLabels.Of(state.Phase),
            currentTurn = !isSpectator && state.CurrentTurnPlayer == myIdx,
            turnCount = state.TurnCount,
            firstPlayer = state.FirstPlayer,
            firstPlayerChosen = state.StartingPlayerChosen,
            openingStage = state.PendingPrompt is not null
                && state.OpeningStage == OpeningStage.ResolvingOpeningEffects
                    ? "WaitingOpeningPrompt"
                    : state.OpeningStage.ToString(),
            isFirstPlayer = !isSpectator && state.StartingPlayerChosen && state.FirstPlayer == myIdx,
            canChooseFirstPlayer = !isSpectator && !state.StartingPlayerChosen && state.StartingPlayerChooser == myIdx,
            diceWinnerIsMe = !isSpectator && state.StartingPlayerChooser == myIdx,
            startingPlayerChoiceDeadlineUtc = state.StartingPlayerChoiceDeadlineUtc,
            startingDiceRolls = state.StartingDiceRounds.Select(round => new
            {
                my = myIdx == 0 ? round.Player0 : round.Player1,
                opponent = myIdx == 0 ? round.Player1 : round.Player0,
                tie = round.Player0 == round.Player1,
            }).ToArray(),
            mulliganBothDone = state.MulliganBothDone,
            mulliganDeadlineUtc = state.MulliganDeadlineUtc,
            operationClockEnabled = state.OperationClockEnabled,
            myOperationTimeMs = state.OperationClockRemainingMs[myIdx],
            opponentOperationTimeMs = state.OperationClockRemainingMs[oppIdx],
            myTurnOperationTimeMs = state.OperationTurnClockRemainingMs[myIdx],
            opponentTurnOperationTimeMs = state.OperationTurnClockRemainingMs[oppIdx],
            myTurnExtensionUsed = state.OperationTurnExtensionUsed[myIdx],
            opponentTurnExtensionUsed = state.OperationTurnExtensionUsed[oppIdx],
            inactivityActive = state.InactivityActivePlayer < 0
                ? null
                : state.InactivityActivePlayer == myIdx ? "my" : "opponent",
            inactivityWarningActive = state.InactivityWarningActive,
            inactivityLossRemainingMs = state.InactivityLossRemainingMs,
            inactivitySyncUtc = state.InactivitySyncUtc,
            operationClockActive = state.OperationClockActivePlayer < 0
                ? null
                : state.OperationClockActivePlayer == myIdx ? "my" : "opponent",
            operationClockSyncUtc = state.OperationClockSyncUtc,
            operationClockPaused = state.OperationClockPaused,
            matchKind = state.MatchKind.ToString(),
            isGameOver = state.IsGameOver,
            isDraw = state.IsDraw,
            winnerIsMe = !isSpectator && state.WinnerIndex == myIdx,
            gameOverReason = state.GameOverReason,
            drawRequestPendingFromMe = !isSpectator && state.PendingDrawRequester == myIdx,
            drawRequestPendingFromOpponent = !isSpectator && state.PendingDrawRequester == oppIdx,
            // Bug 描述只属于协商双方，不通过观战快照泄露。
            drawRequestDescription = isSpectator ? null : state.PendingDrawRequestDescription,
            drawRequestRejectionCount = isSpectator ? 0 : state.DrawRequestRejectionCounts[myIdx],
            drawRequestRejectionLimit = GameState.DrawRequestRejectionLimit,
            viewerKind = isSpectator ? "spectator" : "player",
            spectatorHandVisible = isSpectator && (state.IsGameOver || revealSpectatorMainHand),
            requestId,
            lastAction = lastAction ?? "",
            actionPayload = payload.Json,
            logLine,
            logLines = logLines.ToArray(),
            effectActivations = effectActivations?.Select(item => new
            {
                sourceId = item.SourceId.ToString(),
                cardNumber = item.CardNumber,
                trigger = item.Trigger,
                side = item.OwnerIndex == myIdx ? "my" : "opponent",
            }).ToArray() ?? [],
            // 仅在胜负已分后向参战玩家下发回放手牌时间线；实时对局与观战都保持隐藏区脱敏。
            replayHands = !isSpectator && state.IsGameOver && replayHandTimeline is not null
                ? replayHandTimeline.Select(frame => new
                {
                    tick = frame.Tick,
                    myCardNumbers = myIdx == 0 ? frame.Player0CardNumbers : frame.Player1CardNumbers,
                    opponentCardNumbers = myIdx == 0 ? frame.Player1CardNumbers : frame.Player0CardNumbers,
                    myLifeCardNumbers = myIdx == 0 ? frame.Player0LifeCardNumbers : frame.Player1LifeCardNumbers,
                    opponentLifeCardNumbers = myIdx == 0 ? frame.Player1LifeCardNumbers : frame.Player0LifeCardNumbers,
                }).ToArray()
                : null,
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
                    side = rv.OwnerIndex == myIdx ? "my" : "opponent",
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
    {
        if (actionPayload is null) return new PayloadComputed(default, "");
        var element = JsonSerializer.SerializeToElement(actionPayload);
        return new PayloadComputed(element, element.GetRawText());
    }

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
            gainedKeywords = GrantedKeywords(state, c),
            effectsNullified = c.IsEffectsNullified || state.IsContinuouslyNullified(c),
            cannotActivateNextReset = c.CannotActivateNextReset,
            cannotBeRested = c.HasRestriction(RestrictionKind.CannotBeRested)
                || state.HasContinuousRestriction(c, RestrictionKind.CannotBeRested),
            turnPlayed = c.TurnPlayed,
            canAttack = CanInitiateAttack(state, idx, c.Id),
            cannotAttack = HasCannotAttackStatus(state, c),
            activatedUsedThisTurn = ActivatedUsedThisTurn(p, c),
            oncePerTurnEffectAvailable = OncePerTurnEffectAvailable(state, p, c),
        }).ToArray();

        return new PlayerBoardComputed(
            p.VisibleName,
            p.CardBackId,
            p.Hand.Count,
            fieldCards,
            p.StageCard?.Info.Number,
            p.StageCard?.Id.ToString(),
            p.StageCard?.IsTapped ?? false,
            p.StageCard is not null && ActivatedUsedThisTurn(p, p.StageCard),
            p.StageCard is not null && OncePerTurnEffectAvailable(state, p, p.StageCard),
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
            GrantedKeywords(state, p.Leader),
            CanInitiateAttack(state, idx, p.Leader.Id),
            HasCannotAttackStatus(state, p.Leader),
            state.IsTriggerNullified(p.Leader, Effects.EffectTrigger.OnEnterField),
            ActivatedUsedThisTurn(p, p.Leader),
            OncePerTurnEffectAvailable(state, p, p.Leader),
            p.ActiveDonCount,
            p.RestDonCount,
            p.CostArea.Count(d => d.State == DonState.Attached),
            p.DonDeck.Count,
            p.HasReDraw,
            p.MulliganDone);
    }

    private static object BuildPlayerSnapshot(
        GameState state,
        int idx,
        bool asSelf,
        bool revealHand,
        PlayerBoardComputed board)
    {
        var p = state.Players[idx];
        return new
        {
            name = board.Name,
            rankIdentity = state.MatchKind is (MatchKind.Ranked or MatchKind.RankedWild) && p.RankIdentity is { } rank
                ? new
                {
                    faction = rank.Faction,
                    tier = rank.Tier,
                    division = rank.Division,
                    placementGames = rank.PlacementGames,
                    placementRequired = rank.PlacementRequired,
                }
                : null,
            cardBackId = board.CardBackId,
            spriteMap = p.SpriteMap,
            handCardIds = asSelf || revealHand
                ? p.Hand.Select(c => c.Id.ToString()).ToArray()
                : Array.Empty<string>(),
            handCardNumbers = asSelf || revealHand
                ? p.Hand.Select(c => c.Info.Number).ToArray()
                : Array.Empty<string>(),
            handCardCosts = asSelf ? p.Hand.Select(c => state.HandPlayCost(idx, c)).ToArray() : Array.Empty<int>(),
            handCardCounters = asSelf ? p.Hand.Select(c => Effects.HandStaticCounter.Value(state, idx, c)).ToArray() : Array.Empty<int>(),
            handCount = board.HandCount,
            fieldCards = board.FieldCards,
            stageNumber = board.StageNumber,
            stageId = board.StageId,
            stageTapped = board.StageTapped,
            stageActivatedUsedThisTurn = board.StageActivatedUsedThisTurn,
            stageOncePerTurnEffectAvailable = board.StageOncePerTurnEffectAvailable,
            trashNumbers = board.TrashNumbers,
            deckCount = board.DeckCount,
            lifeCount = board.LifeCount,
            lifeNumbers = Array.Empty<string>(),
            lifeFaceUp = board.LifeFaceUp,
            leaderId = board.LeaderId,
            leaderNumber = board.LeaderNumber,
            championLeaderNumber = LeaderChampionStore.Default.IsChampion(p.AccountName, board.LeaderNumber)
                ? board.LeaderNumber
                : null,
            leaderTapped = board.LeaderTapped,
            leaderPower = board.LeaderPower,
            leaderAttachedDon = board.LeaderAttachedDon,
            leaderGainedKeywords = board.LeaderGainedKeywords,
            leaderCanAttack = board.LeaderCanAttack,
            leaderCannotAttack = board.LeaderCannotAttack,
            leaderEnterEffectNullified = board.LeaderEnterEffectNullified,
            leaderActivatedUsedThisTurn = board.LeaderActivatedUsedThisTurn,
            leaderOncePerTurnEffectAvailable = board.LeaderOncePerTurnEffectAvailable,
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
        string CardBackId,
        int HandCount,
        object[] FieldCards,
        string? StageNumber,
        string? StageId,
        bool StageTapped,
        bool StageActivatedUsedThisTurn,
        bool StageOncePerTurnEffectAvailable,
        string[] TrashNumbers,
        int DeckCount,
        int LifeCount,
        object[] LifeFaceUp,
        string LeaderId,
        string LeaderNumber,
        bool LeaderTapped,
        int LeaderPower,
        int LeaderAttachedDon,
        string[] LeaderGainedKeywords,
        bool LeaderCanAttack,
        bool LeaderCannotAttack,
        bool LeaderEnterEffectNullified,
        bool LeaderActivatedUsedThisTurn,
        bool LeaderOncePerTurnEffectAvailable,
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

    private static bool OncePerTurnEffectAvailable(GameState state, PlayerState p, CardInstance c)
    {
        int owner = ReferenceEquals(p, state.Players[0]) ? 0 : 1;
        return Effects.OncePerTurnEffectCatalog.Contains(c.Info.Number, state)
            && !p.OncePerTurnEffectUsedCardIds.Contains(c.Id)
            && Validation.ActionValidator.HasMetCardSpecificActivationTiming(state, owner, c);
    }

    /// <summary>
    /// 客户端的攻击按钮表示“至少存在一个合法攻击对象”，不能只用对方领袖做探测。
    /// 例如【速攻：角色】在登场回合只能攻击角色，OP17-044 也可能暂时把目标限制为约翰。
    /// </summary>
    private static bool CanInitiateAttack(GameState state, int attackerIndex, Guid attackerId)
    {
        if (Validation.ActionValidator.CanAttack(state, attackerIndex, attackerId, true, null).Ok)
            return true;

        int defenderIndex = 1 - attackerIndex;
        return state.Players[defenderIndex].Characters.Any(target =>
            Validation.ActionValidator.CanAttack(state, attackerIndex, attackerId, false, target.Id).Ok);
    }

    /// <summary>
    /// 是否存在明确的“无法攻击”状态。只统计卡牌限制、持续限制和卡牌自带禁攻，
    /// 不把非当前回合、已休息、新登场等普通攻击条件误判为禁攻状态。
    /// </summary>
    private static bool HasCannotAttackStatus(GameState state, CardInstance card)
        => card.HasRestriction(RestrictionKind.CannotAttack)
           || (!card.IsEffectsNullified
               && (state.HasContinuousRestriction(card, RestrictionKind.CannotAttack)
                   || Array.IndexOf(card.Info.Abilities, "此角色无法攻击") >= 0));

    private static readonly string[] GrantableKeywords =
        { "阻挡者", "速攻", "速攻：角色", "双重攻击", "不可阻挡", "流放", "可攻击活跃" };

    private static IEnumerable<string> ContinuousGrantedKeywords(GameState state, CardInstance c)
    {
        foreach (var keyword in GrantableKeywords)
            if (state.HasContinuousKeyword(c, keyword)) yield return keyword;

        // 旧脚本仍使用语义化内部名，向客户端统一输出正式词条。
        if (state.HasContinuousKeyword(c, "登场回合可攻击角色")) yield return "速攻：角色";
    }

    private static string[] GrantedKeywords(GameState state, CardInstance c)
        => c.GainedKeywords.Select(k => k.Keyword == "登场回合可攻击角色" ? "速攻：角色" : k.Keyword)
            .Concat(ContinuousGrantedKeywords(state, c))
            .Distinct()
            .ToArray();
}
