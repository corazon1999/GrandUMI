using System.Runtime.CompilerServices;

namespace GrandUMI.Game;

/// <summary>卡组耗尽胜负规则。用弱引用记录是否已进入正式对局，避免测试布场的空卡组被误判。</summary>
public static class DeckOutRules
{
    private sealed class ArmedState
    {
        public bool IsArmed { get; set; }
    }

    private static readonly ConditionalWeakTable<GameState, ArmedState> ArmedStates = new();

    public static void Arm(GameState state)
        => ArmedStates.GetOrCreateValue(state).IsArmed = true;

    /// <summary>
    /// 通常卡组变为 0 张便立即败北；奈美类规则替换为胜利；OP15-022 布鲁克延后到该回合结束时败北。
    /// </summary>
    public static void EvaluateDeckOut(this GameState state, bool endOfTurn = false)
    {
        if (state.IsGameOver
            || !ArmedStates.TryGetValue(state, out var armed)
            || !armed.IsArmed)
            return;

        for (int playerIdx = 0; playerIdx < state.Players.Length; playerIdx++)
        {
            var player = state.Players[playerIdx];
            if (player.Deck.Count > 0) continue;

            if (state.DeckOutVictoryPlayers.Contains(playerIdx))
            {
                state.WinnerIndex = playerIdx;
                state.GameOverReason = $"{player.VisibleName} 卡组耗尽（规则替换：胜利）";
                return;
            }

            if (player.Leader.Info.Number == "OP15-022" && !endOfTurn) continue;

            state.WinnerIndex = 1 - playerIdx;
            state.GameOverReason = $"{player.VisibleName} 卡组耗尽";
            return;
        }
    }
}
