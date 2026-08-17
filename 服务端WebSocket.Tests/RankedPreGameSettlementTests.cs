using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class RankedPreGameSettlementTests
{
    [Fact]
    public void 排位双方完成起手调度后才允许结算()
    {
        var state = TestScene.New().Build();
        state.WinnerIndex = 1;

        Assert.False(GameRoomManager.IsRankedSettlementEligible(MatchKind.Ranked, state));

        state.Players[0].MulliganDone = true;
        state.Players[1].MulliganDone = true;

        Assert.True(GameRoomManager.IsRankedSettlementEligible(MatchKind.Ranked, state));
        Assert.False(GameRoomManager.IsRankedSettlementEligible(MatchKind.Casual, state));
    }
}
