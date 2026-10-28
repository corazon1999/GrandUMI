using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using GrandUMI.Game;
using GrandUMI.Game.Tournaments;
using GrandUMI.Persistence;
using Xunit;

namespace GrandUMI.Tests;

[Collection("观战列表广播")]
public sealed class TournamentChatChannelTests
{
    private static FieldInfo BridgeField(string name) => typeof(WebSocketBridge).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!;
    private static FieldInfo RoomField(string name) => typeof(GameRoomManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    [Theory]
    [InlineData(false, true)] [InlineData(true, true)]
    [InlineData(false, false)] [InlineData(true, false)]
    public async Task 实际发送按频道隔离并在赛后延续_管理人员双收_普通对局保持共享(bool postGame, bool separated)
    {
        var root = Path.Combine(Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT")!, "chat-" + Guid.NewGuid().ToString("N"));
        var sessions = (ConcurrentDictionary<string, WsSession>)BridgeField("Sessions").GetValue(null)!;
        var rooms = (ConcurrentDictionary<string, GameRoomManager.RoomEntry>)RoomField("_rooms").GetValue(null)!;
        var sessionRooms = (ConcurrentDictionary<string, string>)RoomField("_sessionRoom").GetValue(null)!;
        var postGames = (PostGameChatRegistry)BridgeField("PostGameChats").GetValue(null)!;
        var originalStore = TournamentRuntime.Store;
        var originalPlayers = BridgeField("_playerDataStore").GetValue(null);
        var originalOperations = BridgeField("_operationsCenterStore").GetValue(null);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var captures = new[] { "选手甲", "选手乙", "观众甲", "观众乙", "本赛事管理", "其他赛事管理", "举办者", "裁判", "释迦", "无关桌观众" }
            .Select((name, index) => new Capture(index == 8 ? name : name + suffix)).ToArray();
        var roomId = "chat-room-" + suffix;
        try
        {
            var players = new PlayerDataStore(Path.Combine(root, "players.db")); players.Initialize();
            foreach (var capture in captures) players.Login(capture.Session.Account!);
            BridgeField("_playerDataStore").SetValue(null, players);
            BridgeField("_operationsCenterStore").SetValue(null, null);
            var store = new TournamentStore(Path.Combine(root, "tournaments.db"));
            TournamentRuntime.Initialize(store);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var tournament = store.Execute(captures[6].Session.Account!, "举办者", false, new TournamentCommand
            {
                Action = "apply", RequestId = suffix, Name = "聊天隔离回归", Capacity = 8,
                RegistrationOpensAt = now, RegistrationClosesAt = now + 300_000, StartsAt = now + 600_000,
                Managers = [captures[4].Session.Account!], Judges = [captures[7].Session.Account!],
            }, now: now);
            store.SystemUpdate(tournament.Id, t => {
                t.Rounds.Add(new() { Tables = [new() { RoomId = roomId, Player0 = captures[0].Session.Account, Player1 = captures[1].Session.Account }] });
                return true;
            });
            store.Execute("其他举办者" + suffix, "其他举办者", false, new TournamentCommand {
                Action = "apply", RequestId = suffix + "other", Name = "另一场赛事", Capacity = 8,
                RegistrationOpensAt = now, RegistrationClosesAt = now + 300_000, StartsAt = now + 600_000,
                Managers = [captures[5].Session.Account!],
            }, now: now);
            foreach (var capture in captures) Assert.True(sessions.TryAdd(capture.Session.SessionId, capture.Session));
            var playerIds = captures.Take(2).Select(c => c.Session.SessionId).ToArray();
            var observerIds = captures.Skip(2).Take(7).Select(c => c.Session.SessionId).ToArray();
            if (postGame) postGames.Register(playerIds, observerIds, roomId, separated);
            else
            {
                var room = new GameRoomManager.RoomEntry { RoomId = roomId, Engine = null!, PlayerSessionIds = playerIds,
                    PlayerAccounts = captures.Take(2).Select(c => c.Session.Account!).ToArray(),
                    PlayerDisplayNames = ["选手甲", "选手乙"],
                    MatchKind = separated ? MatchKind.Tournament : MatchKind.Casual };
                foreach (var capture in captures.Skip(2).Take(7)) room.Spectators[capture.Session.SessionId] = new()
                { SessionId = capture.Session.SessionId, Account = capture.Session.Account!, DisplayName = capture.Session.Account!, ViewPlayerIndex = 0 };
                Assert.True(rooms.TryAdd(roomId, room));
                foreach (var capture in captures.Take(9)) sessionRooms[capture.Session.SessionId] = roomId;
            }

            async Task SendAndCheck(int sender, string? channel, params int[] expected)
            {
                ((ConcurrentDictionary<string, DateTime>)BridgeField("GameChatAt").GetValue(null)!).TryRemove(captures[sender].Session.SessionId, out _);
                var message = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(new { Text = "聊天隔离验证", Channel = channel }))!;
                typeof(WebSocketBridge).GetMethod("OnGameChat", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [captures[sender].Session, message]);
                await Task.WhenAll(captures.Select(c => c.FlushAsync()));
                for (var index = 0; index < captures.Length; index++)
                {
                    var received = captures[index].Messages.ToArray();
                    captures[index].Messages.Clear();
                    if (expected.Contains(index))
                    {
                        var payload = Assert.Single(received);
                        Assert.Equal(roomId, payload.GetProperty("roomId").GetString());
                        Assert.Equal(channel ?? (separated ? sender < 2 ? "players" : "spectators" : "room"), payload.GetProperty("channel").GetString());
                    }
                    else if (index == sender && expected.Length == 0)
                        Assert.False(Assert.Single(received).GetProperty("result").GetBoolean());
                    else Assert.Empty(received);
                }
            }

            var all = Enumerable.Range(0, 9).ToArray();
            await SendAndCheck(0, null, separated ? [0, 1, 4, 6, 7, 8] : all);
            await SendAndCheck(2, null, separated ? [2, 3, 4, 5, 6, 7, 8] : all);
            if (separated)
            {
                await SendAndCheck(2, "players", []);
                await SendAndCheck(0, "spectators", []);
                await SendAndCheck(5, "players", []);
                await SendAndCheck(4, "players", [0, 1, 4, 6, 7, 8]);
                await SendAndCheck(8, "spectators", [2, 3, 4, 5, 6, 7, 8]);
                players.BlockPlayer(captures[0].Session.Account!, captures[4].Session.Account!);
                await SendAndCheck(0, "players", [0, 1, 4, 6, 7, 8]);
                players.UnblockPlayer(captures[0].Session.Account!, captures[4].Session.Account!);
                store.SystemUpdate(tournament.Id, t => { t.Managers.Clear(); return true; });
                await SendAndCheck(0, "players", [0, 1, 6, 7, 8]);
                await SendAndCheck(4, "players", []);
            }
        }
        finally
        {
            rooms.TryRemove(roomId, out _);
            foreach (var capture in captures)
            {
                sessionRooms.TryRemove(capture.Session.SessionId, out _); postGames.Leave(capture.Session.SessionId);
                sessions.TryRemove(capture.Session.SessionId, out _);
                ((ConcurrentDictionary<string, DateTime>)BridgeField("GameChatAt").GetValue(null)!).TryRemove(capture.Session.SessionId, out _);
                await capture.Session.StopSenderAsync();
            }
            ((System.Collections.IDictionary)BridgeField("GameChatEvidenceByRoom").GetValue(null)!).Remove(roomId);
            TournamentRuntime.Initialize(originalStore!);
            BridgeField("_playerDataStore").SetValue(null, originalPlayers);
            BridgeField("_operationsCenterStore").SetValue(null, originalOperations);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class Capture
    {
        public WsSession Session { get; }
        public ConcurrentQueue<JsonElement> Messages { get; } = new();
        private TaskCompletionSource _barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Capture(string account)
        {
            Session = new() { Socket = null!, Account = account };
            Session.StartSender(outbound => {
                var payload = JsonSerializer.SerializeToElement(outbound.Data);
                if (payload.GetProperty("proto").GetString() == "ChatTestBarrier") _barrier.TrySetResult();
                else Messages.Enqueue(payload);
                return Task.CompletedTask;
            });
        }
        public async Task FlushAsync()
        {
            _barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
            WebSocketBridge.Send(Session.SessionId, new { proto = "ChatTestBarrier" });
            await _barrier.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
