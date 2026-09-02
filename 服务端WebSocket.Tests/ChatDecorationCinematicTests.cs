using System.Text.Json;
using System.Collections.Concurrent;
using System.Reflection;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class ChatDecorationCinematicTests
{
    [Fact]
    public async Task 权威终局_投降快照发出后无需客户端回执即刻清房()
    {
        // ServerCapacity 会在创建房间前验证持久化根目录；测试进程只创建调用方已显式配置的目录。
        var configuredDataDirectory = Environment.GetEnvironmentVariable("GRANDUMI_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configuredDataDirectory))
            Directory.CreateDirectory(configuredDataDirectory);
        TestScene.New();
        var deck = BuildLegalDeck("OP15-001");
        var suffix = Guid.NewGuid().ToString("N");
        var player0Account = $"cinematic-managed-a-{suffix}";
        var surrenderingSession = new WsSession { Account = player0Account };
        var player0Session = surrenderingSession.SessionId;
        var room = GameRoomManager.CreateRoom(
            player0Session,
            player0Account,
            deck,
            $"cinematic-managed-s1-{suffix}",
            $"cinematic-managed-b-{suffix}",
            deck,
            p0First: true,
            matchKind: MatchKind.Casual,
            broadcastInitialState: false);
        var snapshots = new ConcurrentQueue<JsonElement>();
        room.Engine.OnSendToPlayer = (_, payload) =>
            snapshots.Enqueue(JsonSerializer.SerializeToElement(payload));

        try
        {
            var legacySurrender = typeof(WebSocketBridge).GetMethod(
                "OnSurrender",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(legacySurrender);
            legacySurrender.Invoke(null, [surrenderingSession]);
            for (var attempt = 0; attempt < 500 && GameRoomManager.GetRoom(room.RoomId) is not null; attempt++)
                await Task.Delay(4);

            Assert.Null(GameRoomManager.GetRoom(room.RoomId));
            var terminals = snapshots.Where(snapshot => snapshot.GetProperty("isGameOver").GetBoolean()).ToArray();
            Assert.Equal(2, terminals.Length);
            var terminal = terminals[0];
            Assert.Equal("Surrender", terminal.GetProperty("lastAction").GetString());
            Assert.Equal(
                $"{room.RoomId}:terminal",
                terminal.GetProperty("cinematic").GetProperty("terminal").GetProperty("eventId").GetString());
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
    public void 权威演出_双调度后双方各触发一次且各视角稳定映射()
    {
        var room = BuildRoom(new string?[2, 2]
        {
            { "quote-pirate-king-man", "quote-winner-justice" },
            { "quote-fated-meeting", "quote-surpass-me" },
        });

        var beforeMulligan = Snapshot(room, viewerIndex: 0, perspective: 0);
        Assert.Empty(beforeMulligan.GetProperty("cinematic").GetProperty("openingEvents").EnumerateArray());

        room.Engine.State.Players[0].MulliganDone = true;
        room.Engine.State.Players[1].MulliganDone = true;
        var player0 = Snapshot(room, viewerIndex: 0, perspective: 0);
        var player0Again = Snapshot(room, viewerIndex: 0, perspective: 0);
        var player1 = Snapshot(room, viewerIndex: 1, perspective: 1);
        var spectatorPlayer1 = Snapshot(room, viewerIndex: -1, perspective: 1);

        var player0Events = player0.GetProperty("cinematic").GetProperty("openingEvents").EnumerateArray().ToArray();
        Assert.Equal(2, player0Events.Length);
        Assert.Equal("self", player0Events[0].GetProperty("displaySide").GetString());
        Assert.Equal("opponent", player0Events[1].GetProperty("displaySide").GetString());
        Assert.Equal("opponent", player1.GetProperty("cinematic").GetProperty("openingEvents")[0].GetProperty("displaySide").GetString());
        Assert.Equal("self", player1.GetProperty("cinematic").GetProperty("openingEvents")[1].GetProperty("displaySide").GetString());
        Assert.Equal(
            player1.GetProperty("cinematic").GetRawText(),
            spectatorPlayer1.GetProperty("cinematic").GetRawText());
        Assert.Equal(
            player0.GetProperty("cinematic").GetRawText(),
            player0Again.GetProperty("cinematic").GetRawText());
        Assert.Equal($"{room.RoomId}:opening:0", player0Events[0].GetProperty("eventId").GetString());
        Assert.Equal($"{room.RoomId}:opening:1", player0Events[1].GetProperty("eventId").GetString());
    }

    [Fact]
    public void 权威演出_终局固定输赢侧与胜利宣言且平局不伪造赢家()
    {
        var room = BuildRoom(new string?[2, 2]
        {
            { null, "quote-winner-justice" },
            { null, "quote-surpass-me" },
        });
        room.Engine.State.WinnerIndex = 0;
        room.Engine.State.GameOverReason = "测试终局";

        var winner = Snapshot(room, viewerIndex: 0, perspective: 0).GetProperty("cinematic").GetProperty("terminal");
        var loser = Snapshot(room, viewerIndex: 1, perspective: 1).GetProperty("cinematic").GetProperty("terminal");
        Assert.Equal($"{room.RoomId}:terminal", winner.GetProperty("eventId").GetString());
        Assert.Equal("self", winner.GetProperty("winnerSide").GetString());
        Assert.Equal("opponent", winner.GetProperty("loserSide").GetString());
        Assert.Equal("opponent", loser.GetProperty("winnerSide").GetString());
        Assert.Equal("self", loser.GetProperty("loserSide").GetString());
        Assert.Equal("唯有胜者才是正义！", winner.GetProperty("victory").GetProperty("text").GetString());

        var drawRoom = BuildRoom(new string?[2, 2]
        {
            { null, "quote-winner-justice" },
            { null, "quote-surpass-me" },
        });
        drawRoom.Engine.State.IsDraw = true;
        drawRoom.Engine.State.GameOverReason = "协商平局";
        var draw = Snapshot(drawRoom, viewerIndex: 0, perspective: 0).GetProperty("cinematic").GetProperty("terminal");
        Assert.Equal(JsonValueKind.Null, draw.GetProperty("winnerSeat").ValueKind);
        Assert.Equal(JsonValueKind.Null, draw.GetProperty("loserSeat").ValueKind);
        Assert.Equal(JsonValueKind.Null, draw.GetProperty("victory").ValueKind);
    }

    [Fact]
    public void 权威演出_装备锁定值写入日志并在重启恢复后原样重放()
    {
        var original = BuildRoom(new string?[2, 2]
        {
            { "quote-pirate-king-man", "quote-winner-justice" },
            { "greeting-straw-hat", "threat-cannon" },
        });
        var header = JsonSerializer.SerializeToElement(new
        {
            chatDecorations = original.ChatDecorationCinematics.ExportForJournal(),
        });
        var restoredLoadout = Assert.IsType<string?[,]>(
            GameRoomManager.ReadChatDecorationJournalLoadout(header));
        Assert.Equal("quote-pirate-king-man", restoredLoadout[0, 0]);
        Assert.Equal("quote-winner-justice", restoredLoadout[0, 1]);
        Assert.Equal("greeting-straw-hat", restoredLoadout[1, 0]);
        Assert.Equal("threat-cannon", restoredLoadout[1, 1]);

        var recovered = BuildRoom(restoredLoadout);
        recovered.Engine.State.Players[0].MulliganDone = true;
        recovered.Engine.State.Players[1].MulliganDone = true;
        var snapshot = Snapshot(recovered, viewerIndex: 0, perspective: 0).GetProperty("cinematic");
        var opening = snapshot.GetProperty("openingEvents").EnumerateArray().ToArray();
        Assert.Equal("我是要成为海贼王的男人!", opening[0].GetProperty("text").GetString());
        Assert.Equal("嘿！来场痛快的对决吧！", opening[1].GetProperty("text").GetString());

        recovered.Engine.State.WinnerIndex = 1;
        recovered.Engine.State.GameOverReason = "恢复后终局";
        var terminal = Snapshot(recovered, viewerIndex: 0, perspective: 0)
            .GetProperty("cinematic").GetProperty("terminal");
        Assert.Equal("当心了，下一轮炮火可不会留情！", terminal.GetProperty("victory").GetProperty("text").GetString());
    }

    [Fact]
    public void 权威演出_日志中的损坏装饰项只降级对应位置而不阻断恢复()
    {
        using var document = JsonDocument.Parse("""
            {
              "chatDecorations": [
                17,
                { "seat": 0, "opening": "quote-pirate-king-man", "victory": null },
                { "seat": "invalid", "opening": "quote-fated-meeting" },
                { "seat": 1, "opening": 123, "victory": "quote-winner-justice" }
              ]
            }
            """);

        var restored = Assert.IsType<string?[,]>(
            GameRoomManager.ReadChatDecorationJournalLoadout(document.RootElement));
        Assert.Equal("quote-pirate-king-man", restored[0, 0]);
        Assert.Null(restored[0, 1]);
        Assert.Null(restored[1, 0]);
        Assert.Equal("quote-winner-justice", restored[1, 1]);
    }

    private static JsonElement Snapshot(GameRoomManager.RoomEntry room, int viewerIndex, int perspective)
        => JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(
            room.Engine.State,
            viewerIndex,
            spectatorPlayerIndex: perspective,
            cinematic: room.ChatDecorationCinematics.BuildSnapshot(room, perspective)));

    private static GameRoomManager.RoomEntry BuildRoom(string?[,] loadout)
    {
        TestScene.New();
        var deck = BuildLegalDeck("OP15-001");
        var suffix = Guid.NewGuid().ToString("N");
        var engine = new GameEngine(
            $"cinematic-{suffix}",
            ($"cinematic-s0-{suffix}", $"cinematic-a-{suffix}", deck),
            ($"cinematic-s1-{suffix}", $"cinematic-b-{suffix}", deck),
            firstPlayer: 0);
        engine.State.Players[0].DisplayName = "爱丽丝";
        engine.State.Players[1].DisplayName = "鲍勃";
        var room = new GameRoomManager.RoomEntry
        {
            RoomId = engine.State.RoomId,
            Engine = engine,
            PlayerSessionIds = [engine.State.Players[0].SessionId, engine.State.Players[1].SessionId],
            PlayerAccounts = [engine.State.Players[0].AccountName, engine.State.Players[1].AccountName],
            PlayerDisplayNames = ["爱丽丝", "鲍勃"],
            MatchKind = MatchKind.Casual,
        };
        room.ChatDecorationCinematics.Initialize(room.PlayerAccounts, loadout);
        return room;
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
