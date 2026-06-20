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

        // 操作日志（按观看者视角生成一行中文；不可记录的动作返回空串）
        var payloadElem = actionPayload is null
            ? default
            : System.Text.Json.JsonSerializer.SerializeToElement(actionPayload);
        var logLine = ActionLogFormatter.Format(state, viewerIndex, lastAction ?? "", payloadElem);

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
            logLine       = logLine,
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
            // 检索/公开牌的瞬时展示：side 按当前视角换算（自己公开 → my，对手公开 → opponent）
            reveal = state.PendingReveal is { } rv
                ? new {
                    side = (!isSpectator && rv.OwnerIndex == myIdx) ? "my" : "opponent",
                    cardNumbers = rv.CardNumbers,
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
            // 每张手牌的有效费用（含手牌静态减费/持续光环），仅下发给己方供 UI 显示；对手为空
            handCardCosts  = asSelf ? p.Hand.Select(c => state.HandPlayCost(idx, c)).ToArray() : Array.Empty<int>(),
            handCount      = p.Hand.Count,
            fieldCards     = p.Characters.Select(c => new
            {
                id           = c.Id.ToString(),
                number       = c.Info.Number,
                isTapped     = c.IsTapped,
                powerCurrent = state.CurrentPowerOf(idx, c),
                cost         = state.CurrentCostOf(idx, c),
                attachedDon  = p.AttachedDonCount(c.Id),
                gainedKeywords = c.GainedKeywords.Select(k => k.Keyword).ToArray(),
                cannotActivateNextReset = c.CannotActivateNextReset,
                // 无法被效果转为休息状态：综合瞬时(AddRestriction)与持续(GrantRestriction)两种来源
                cannotBeRested = c.HasRestriction(GrandUMI.Game.RestrictionKind.CannotBeRested)
                              || state.HasContinuousRestriction(c, GrandUMI.Game.RestrictionKind.CannotBeRested),
                turnPlayed   = c.TurnPlayed,
                canAttack    = GrandUMI.Game.Validation.ActionValidator.CanAttack(state, idx, c.Id, true, null).Ok,
                // 本回合【启动主要】【每回合1次】是否已用 → 供客户端用完隐藏"启动效果"按钮
                activatedUsedThisTurn = ActivatedUsedThisTurn(p, c),
            }).ToArray(),
            stageNumber    = p.StageCard?.Info.Number,
            stageId        = p.StageCard?.Id.ToString(),
            stageTapped    = p.StageCard?.IsTapped ?? false,
            stageActivatedUsedThisTurn = p.StageCard is not null && ActivatedUsedThisTurn(p, p.StageCard),
            trashNumbers   = p.Trash.Select(c => c.Info.Number).ToArray(),
            deckCount      = p.DeckCount,
            lifeCount      = p.LifeCount,
            // 生命牌内容：永远不发给对手；自己也只在加入手牌前不知道（生命区背面朝上规则）
            // ↓ 故 lifeNumbers 始终为空数组，触发流程时单独通过 prompt 公开
            lifeNumbers    = Array.Empty<string>(),
            // 正面朝上的生命牌为公开信息（双方可见番号）；背面者仅下发占位（faceUp=false, number=null）
            lifeFaceUp     = p.LifeArea.Select(c => new { faceUp = c.IsLifeFaceUp, number = c.IsLifeFaceUp ? c.Info.Number : null }).ToArray(),
            leaderId       = p.Leader.Id.ToString(),
            leaderNumber   = p.Leader.Info.Number,
            leaderTapped   = p.Leader.IsTapped,
            leaderPower    = state.CurrentPowerOf(idx, p.Leader),
            leaderAttachedDon = p.AttachedDonCount(p.Leader.Id),
            leaderCanAttack = GrandUMI.Game.Validation.ActionValidator.CanAttack(state, idx, p.Leader.Id, true, null).Ok,
            leaderActivatedUsedThisTurn = ActivatedUsedThisTurn(p, p.Leader),
            costActive     = p.ActiveDonCount,
            costRest       = p.RestDonCount,
            costAttached   = p.CostArea.Count(d => d.State == DonState.Attached),
            donDeckCount   = p.DonDeck.Count,
            hasReDraw      = p.HasReDraw,
            mulliganDone   = p.MulliganDone,
        };
    }

    /// <summary>该卡本回合的【启动主要】【每回合1次】是否已用。
    /// DSL 启动效果 key = "{id}-Activated"；脚本约定 key = "{番号}-act:{id}"。
    /// 仅 oncePerTurn 卡会写入 TurnOnceUsed，故可多次发动的启动效果天然恒 false（不会误隐藏按钮）。</summary>
    static bool ActivatedUsedThisTurn(PlayerState p, CardInstance c)
        => p.TurnOnceUsed.Contains($"{c.Id}-Activated")
        || p.TurnOnceUsed.Contains($"{c.Info.Number}-act:{c.Id}");
}
