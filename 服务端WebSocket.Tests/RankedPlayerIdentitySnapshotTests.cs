using System.Text.Json;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class RankedPlayerIdentitySnapshotTests
{
    [Fact]
    public void 称号佩戴和取消不改变对局私有状态或恢复哈希()
    {
        var state = TestScene.MaxScenario();
        var before = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        var expectedHash = RoomRecoverySnapshotStore.ComputeStateSha256(before);

        state.Players[0].EquippedSeasonTitle = "S1 海贼王";
        state.Players[1].EquippedSeasonTitle = "S1 海军元帅";
        var equipped = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        Assert.Equal(before.GetRawText(), equipped.GetRawText());
        Assert.Equal(expectedHash, RoomRecoverySnapshotStore.ComputeStateSha256(equipped));

        state.Players[0].EquippedSeasonTitle = null;
        state.Players[1].EquippedSeasonTitle = "S1 世界之王";
        var changed = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        Assert.Equal(before.GetRawText(), changed.GetRawText());
        Assert.Equal(expectedHash, RoomRecoverySnapshotStore.ComputeStateSha256(changed));
    }

    [Theory]
    [InlineData(MatchKind.Casual)]
    [InlineData(MatchKind.Hex)]
    [InlineData(MatchKind.Ranked)]
    [InlineData(MatchKind.RankedWild)]
    public void 各模式快照和观战视角均展示已佩戴称号且取消不残留(MatchKind kind)
    {
        var state = TestScene.MaxScenario();
        state.MatchKind = kind;
        state.Players[0].EquippedSeasonTitle = "S1 海贼王";
        state.Players[1].EquippedSeasonTitle = "S1 海军元帅";
        var own = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, viewerIndex: 0));
        var observer = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, viewerIndex: -1, spectatorPlayerIndex: 1));
        Assert.Equal("S1 海贼王", own.GetProperty("my").GetProperty("equippedSeasonTitle").GetString());
        Assert.Equal("S1 海军元帅", observer.GetProperty("my").GetProperty("equippedSeasonTitle").GetString());
        Assert.Equal("S1 海贼王", observer.GetProperty("opponent").GetProperty("equippedSeasonTitle").GetString());
        state.Players[0].EquippedSeasonTitle = null;
        var canceled = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, viewerIndex: 0));
        Assert.Equal(JsonValueKind.Null, canceled.GetProperty("my").GetProperty("equippedSeasonTitle").ValueKind);
    }

    [Theory]
    [InlineData(MatchKind.Ranked)]
    [InlineData(MatchKind.RankedWild)]
    public void 排位快照_按观看视角下发双方阵营和段位(MatchKind matchKind)
    {
        var state = TestScene.MaxScenario();
        state.MatchKind = matchKind;
        state.Players[0].RankIdentity = new PlayerRankIdentity("pirate", "船长", 2, 5, 5);
        state.Players[1].RankIdentity = new PlayerRankIdentity("marine", "海军少将", 1, 3, 5);

        var player0 = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, viewerIndex: 0));
        var player1 = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, viewerIndex: 1));
        var spectator = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(
            state,
            viewerIndex: -1,
            spectatorPlayerIndex: 1));

        AssertRank(player0.GetProperty("my").GetProperty("rankIdentity"), "pirate", "船长", 2, 5, 5);
        AssertRank(player0.GetProperty("opponent").GetProperty("rankIdentity"), "marine", "海军少将", 1, 3, 5);
        AssertRank(player1.GetProperty("my").GetProperty("rankIdentity"), "marine", "海军少将", 1, 3, 5);
        AssertRank(player1.GetProperty("opponent").GetProperty("rankIdentity"), "pirate", "船长", 2, 5, 5);
        AssertRank(spectator.GetProperty("my").GetProperty("rankIdentity"), "marine", "海军少将", 1, 3, 5);
        AssertRank(spectator.GetProperty("opponent").GetProperty("rankIdentity"), "pirate", "船长", 2, 5, 5);
    }

    [Fact]
    public void 非排位快照_不下发缓存的排位身份()
    {
        var state = TestScene.MaxScenario();
        state.MatchKind = MatchKind.Casual;
        state.Players[0].RankIdentity = new PlayerRankIdentity("government", "浅海契约", 3, 5, 5);

        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, viewerIndex: 0));

        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("my").GetProperty("rankIdentity").ValueKind);
        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("opponent").GetProperty("rankIdentity").ValueKind);
    }

    private static void AssertRank(
        JsonElement rank,
        string faction,
        string tier,
        int? division,
        int placementGames,
        int placementRequired)
    {
        Assert.Equal(faction, rank.GetProperty("faction").GetString());
        Assert.Equal(tier, rank.GetProperty("tier").GetString());
        Assert.Equal(division, rank.GetProperty("division").GetInt32());
        Assert.Equal(placementGames, rank.GetProperty("placementGames").GetInt32());
        Assert.Equal(placementRequired, rank.GetProperty("placementRequired").GetInt32());
    }
}
