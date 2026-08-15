using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class GameDisplayNameTests
{
    [Fact]
    public void 对局快照只公开展示名而不公开登录账号()
    {
        TestScene.New();
        var suffix = Guid.NewGuid().ToString("N");
        var account0 = $"login-account-a-{suffix}";
        var account1 = $"login-account-b-{suffix}";
        var room = GameRoomManager.CreateRoom(
            $"display-s0-{suffix}", account0, BuildLegalDeck("OP15-001"),
            $"display-s1-{suffix}", account1, BuildLegalDeck("OP15-001"),
            p0First: true,
            broadcastInitialState: false,
            p0DisplayName: "海风玩家",
            p1DisplayName: "草帽伙伴");

        try
        {
            var snapshot = JsonSerializer.SerializeToElement(
                StateSnapshotBuilder.Build(room.Engine.State, viewerIndex: 0));
            var json = snapshot.GetRawText();

            Assert.Equal("海风玩家", snapshot.GetProperty("my").GetProperty("name").GetString());
            Assert.Equal("草帽伙伴", snapshot.GetProperty("opponent").GetProperty("name").GetString());
            Assert.DoesNotContain(account0, json, StringComparison.Ordinal);
            Assert.DoesNotContain(account1, json, StringComparison.Ordinal);

            Assert.True(room.Engine.HandleAction(0, "Surrender", JsonSerializer.SerializeToElement(new { })));
            Assert.Equal("海风玩家 投降", room.Engine.State.GameOverReason);
        }
        finally
        {
            GameRoomManager.CleanupRoom(room.RoomId);
            if (!string.IsNullOrWhiteSpace(room.MatchLogPath))
            {
                try { File.Delete(room.MatchLogPath); } catch { }
            }
        }
    }

    [Fact]
    public void 旧对局缺少展示名时兼容回退账号名()
    {
        var state = TestScene.New().Build();

        var snapshot = JsonSerializer.SerializeToElement(
            StateSnapshotBuilder.Build(state, viewerIndex: 0));

        Assert.Equal("p0", snapshot.GetProperty("my").GetProperty("name").GetString());
        Assert.Equal("p1", snapshot.GetProperty("opponent").GetProperty("name").GetString());
    }

    private static string BuildLegalDeck(string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = CardDatabase.GetBySet("OP15")
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leaderNumber };
        var counts = new Dictionary<string, int>();
        var index = 0;
        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            if (counts.GetValueOrDefault(card.Number) >= 4) continue;
            lines.Add(card.Number);
            counts[card.Number] = counts.GetValueOrDefault(card.Number) + 1;
        }
        return string.Join('\n', lines);
    }
}
