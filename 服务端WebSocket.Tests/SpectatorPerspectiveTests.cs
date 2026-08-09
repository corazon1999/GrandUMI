using System.Text.Json;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class SpectatorPerspectiveTests
{
    [Fact]
    public void 观战快照_指定一号座位时该玩家保持主视角且双方手牌脱敏()
    {
        var state = TestScene.MaxScenario();

        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(
            state,
            viewerIndex: -1,
            spectatorPlayerIndex: 1));

        Assert.Equal("spectator", snapshot.GetProperty("viewerKind").GetString());
        Assert.Equal(
            state.Players[1].AccountName,
            snapshot.GetProperty("my").GetProperty("name").GetString());
        Assert.Equal(
            state.Players[0].AccountName,
            snapshot.GetProperty("opponent").GetProperty("name").GetString());
        Assert.Empty(snapshot.GetProperty("my").GetProperty("handCardNumbers").EnumerateArray());
        Assert.Empty(snapshot.GetProperty("opponent").GetProperty("handCardNumbers").EnumerateArray());
    }

    [Fact]
    public void 批量快照_仅按需生成一号座位观战视角()
    {
        var state = TestScene.MaxScenario();

        var defaultSnapshots = StateSnapshotBuilder.BuildAll(state);
        var dualSnapshots = StateSnapshotBuilder.BuildAll(state, includePlayer1Spectator: true);

        Assert.Null(defaultSnapshots.SpectatorPlayer1);
        Assert.NotNull(dualSnapshots.SpectatorPlayer1);

        var alternate = JsonSerializer.SerializeToElement(dualSnapshots.SpectatorPlayer1!);
        Assert.Equal(
            state.Players[1].AccountName,
            alternate.GetProperty("my").GetProperty("name").GetString());
    }

    [Fact]
    public void 观战快照_按主视角同步双方各自异画选择()
    {
        var state = TestScene.MaxScenario();
        state.Players[0].SpriteMap["OP01-001"] = "/sprites/OP01/OP01-001_p0.png";
        state.Players[1].SpriteMap["OP01-001"] = "/sprites/OP01/OP01-001_p1.png";

        var playerSnapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, viewerIndex: 1));
        var spectatorSnapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(
            state,
            viewerIndex: -1,
            spectatorPlayerIndex: 1));

        Assert.Equal(
            playerSnapshot.GetProperty("my").GetProperty("spriteMap").GetProperty("OP01-001").GetString(),
            spectatorSnapshot.GetProperty("my").GetProperty("spriteMap").GetProperty("OP01-001").GetString());
        Assert.Equal(
            playerSnapshot.GetProperty("opponent").GetProperty("spriteMap").GetProperty("OP01-001").GetString(),
            spectatorSnapshot.GetProperty("opponent").GetProperty("spriteMap").GetProperty("OP01-001").GetString());
        Assert.NotEqual(
            spectatorSnapshot.GetProperty("my").GetProperty("spriteMap").GetProperty("OP01-001").GetString(),
            spectatorSnapshot.GetProperty("opponent").GetProperty("spriteMap").GetProperty("OP01-001").GetString());
    }
}
