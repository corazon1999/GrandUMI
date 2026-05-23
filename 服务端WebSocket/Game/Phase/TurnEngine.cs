namespace GrandUMI.Game.PhaseFlow;

/// <summary>
/// 5 阶段流程驱动：Reset → Draw → Don → Main → End
/// 各阶段进入时执行强制规则处理（恢复活跃、抽牌、加咚等），
/// 主要阶段的玩家自主操作由 GameEngine 处理。
/// </summary>
public static class TurnEngine
{
    public const int MaxDonInCostArea = 10;

    /// <summary>开局后双方都完成 Mulligan 时，进入第一回合的 Reset 阶段</summary>
    public static void StartFirstTurn(GameState state)
    {
        state.TurnCount = 1;
        state.CurrentTurnPlayer = state.FirstPlayer;
        state.Phase = Phase.Reset;
        EnterResetPhase(state); // 重置阶段是空操作（角色都还没登场），紧接 Draw / Don
        EnterDrawPhase(state);
        EnterDonPhase(state);
        state.Phase = Phase.Main;
    }

    public static void EnterResetPhase(GameState state)
    {
        state.Phase = Phase.Reset;
        var p = state.Turn;

        // 1. 赋予中的咚 → 休息状态放回费用区
        foreach (var d in p.CostArea)
        {
            if (d.State == DonState.Attached)
            {
                d.State = DonState.Rest;
                d.AttachedToCardId = null;
            }
        }

        // 2. 领袖/角色/舞台/费用区中的休息卡牌 → 活跃
        if (p.Leader.IsTapped) p.Leader.IsTapped = false;
        foreach (var c in p.Characters)
        {
            if (c.CannotActivateNextReset)
            {
                c.CannotActivateNextReset = false; // 一次性，使用后清除
                continue;                          // 跳过本次激活
            }
            if (c.IsTapped) c.IsTapped = false;
        }
        // 舞台卡通常不会休息，但留个处理点
        if (p.StageCard is { IsTapped: true }) p.StageCard.IsTapped = false;
        foreach (var d in p.CostArea)
            if (d.State == DonState.Rest) d.State = DonState.Active;

        // 清除本回合"每回合 1 次"使用记录（在 EnterResetPhase 阶段，回合玩家自己的清掉，
        // 实际官方规则是在结束阶段清除，统一在 EnterEndPhase 做）
    }

    public static void EnterDrawPhase(GameState state)
    {
        state.Phase = Phase.Draw;
        // 第一回合先手不抽
        bool firstTurnFirstPlayer = state.TurnCount == 1 && state.CurrentTurnPlayer == state.FirstPlayer;
        if (!firstTurnFirstPlayer)
            DrawCard(state, state.CurrentTurnPlayer, 1);
    }

    public static void EnterDonPhase(GameState state)
    {
        state.Phase = Phase.Don;
        bool firstTurnFirstPlayer = state.TurnCount == 1 && state.CurrentTurnPlayer == state.FirstPlayer;
        int add = firstTurnFirstPlayer ? 1 : 2;
        AddDonFromDeck(state, state.CurrentTurnPlayer, add);
    }

    public static void EnterEndPhase(GameState state)
    {
        state.Phase = Phase.End;
        // 清除本回合内的修正 + 期限关键字（含"直到下个对方的结束阶段结束时为止"）
        foreach (var p in state.Players)
        {
            foreach (var c in p.Characters)
            {
                ClearTurnScopedState(c);
            }
            ClearTurnScopedState(p.Leader);
            p.TurnOnceUsed.Clear();
        }
        // 清除来源已不在场上的 ContinuousEffect（防止僵尸效果）
        state.ContinuousEffects.RemoveAll(eff => !IsSourceCardOnField(state, eff.SourceCardId));
    }

    private static void ClearTurnScopedState(CardInstance c)
    {
        c.PowerModThisTurn = 0;
        c.PowerModThisBattle = 0;
        c.CostModThisTurn = 0;
        c.OriginalPowerOverride = null;
        c.IsEffectsNullified = false;
        c.GainedKeywords.RemoveAll(k =>
            k.Duration == KeywordDuration.ThisTurn ||
            k.Duration == KeywordDuration.ThisBattle ||
            k.Duration == KeywordDuration.UntilNextOpponentEndPhase);
        c.Restrictions.RemoveAll(r =>
            r.Duration == KeywordDuration.ThisTurn ||
            r.Duration == KeywordDuration.ThisBattle ||
            r.Duration == KeywordDuration.UntilNextOpponentEndPhase);
        c.OncePerTurnUsedKeys.Clear();
    }

    private static bool IsSourceCardOnField(GameState s, string sourceId)
    {
        if (!Guid.TryParse(sourceId, out var gid)) return false;
        foreach (var p in s.Players)
        {
            if (p.Leader.Id == gid) return true;
            if (p.StageCard?.Id == gid) return true;
            foreach (var c in p.Characters) if (c.Id == gid) return true;
        }
        return false;
    }

    /// <summary>由主要阶段宣言结束回合时调用：执行结束阶段，然后切到对方的 Reset</summary>
    public static void AdvanceTurn(GameState state)
    {
        EnterEndPhase(state);
        // 切换回合玩家
        state.CurrentTurnPlayer = 1 - state.CurrentTurnPlayer;
        state.TurnCount += 1;
        EnterResetPhase(state);
        EnterDrawPhase(state);
        EnterDonPhase(state);
        state.Phase = Phase.Main;
    }

    /// <summary>从卡组抽 n 张</summary>
    public static int DrawCard(GameState state, int playerIdx, int n)
    {
        var p = state.Players[playerIdx];
        int actual = 0;
        for (int i = 0; i < n; i++)
        {
            if (p.Deck.Count == 0)
            {
                // 卡组抽空 → 在规则处理时点判负
                if (!state.IsGameOver)
                {
                    state.WinnerIndex = 1 - playerIdx;
                    state.GameOverReason = $"{p.AccountName} 卡组耗尽";
                }
                break;
            }
            var top = p.Deck[0];
            p.Deck.RemoveAt(0);
            p.Hand.Add(top);
            actual++;
        }
        return actual;
    }

    /// <summary>从咚卡组取 n 张到费用区（活跃状态）</summary>
    public static int AddDonFromDeck(GameState state, int playerIdx, int n)
    {
        var p = state.Players[playerIdx];
        int actual = 0;
        int budget = MaxDonInCostArea - p.CostArea.Count;
        n = Math.Min(n, budget);
        for (int i = 0; i < n; i++)
        {
            if (p.DonDeck.Count == 0) break;
            var d = p.DonDeck[0];
            p.DonDeck.RemoveAt(0);
            d.State = DonState.Active;
            d.AttachedToCardId = null;
            p.CostArea.Add(d);
            actual++;
        }
        return actual;
    }
}
