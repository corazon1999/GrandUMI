using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

[Collection("观战列表广播")]
public sealed class AdministratorSpectatingTests
{
    [Theory]
    [InlineData(0, "open")]
    [InlineData(1, "closed")]
    [InlineData(0, "friends")]
    [InlineData(1, "password")]
    public void 管理员即使曾被移除也能进入受限观战且手牌仍按原权限隐藏(int seat, string mode)
    {
        using var fixture = new RoomFixture(mode);
        foreach (var account in AdministratorPolicy.GetAuthorizedAccounts())
        {
            var sid = Guid.NewGuid().ToString("N");
            fixture.Room.KickedSpectatorAccounts[account] = 0;
            GameRoomManager.AddSpectator(fixture.Room.RoomId, sid, account, "管理员", seat);
            Assert.Same(fixture.Room, GameRoomManager.GetRoomBySession(sid));
            Assert.True(fixture.Room.Spectators.TryGetValue(sid, out var spectator));
            Assert.Equal(seat, spectator.ViewPlayerIndex);
            Assert.False(spectator.HandVisible);
        }
    }

    [Theory]
    [InlineData(0, "释迦")]
    [InlineData(1, "释迦")]
    [InlineData(0, "释迦2号")]
    [InlineData(1, "释迦2号")]
    [InlineData(0, "栗子")]
    [InlineData(1, "栗子")]
    public async Task 对战双方都无法移除管理员的任何观战会话(int player, string account)
    {
        using var fixture = new RoomFixture("open");
        var sessions = GetDictionary<WsSession>(typeof(WebSocketBridge), "Sessions");
        var response = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new WsSession { Socket = null! };
        session.StartSender(outbound =>
        {
            var payload = JsonSerializer.SerializeToElement(outbound.Data);
            if (payload.GetProperty("proto").GetString() == "MsgKickSpectator")
                response.TrySetResult(outbound.Data);
            return Task.CompletedTask;
        });
        sessions[session.SessionId] = session;
        fixture.BindPlayer(player, session.SessionId);
        try
        {
            var observerIds = new[] { Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N") };
            foreach (var sid in observerIds)
                GameRoomManager.AddSpectator(fixture.Room.RoomId, sid, account, "管理员");
            GameRoomManager.KickSpectator(session.SessionId, $" {account} ");
            var payload = JsonSerializer.SerializeToElement(await response.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("MsgKickSpectator", payload.GetProperty("proto").GetString());
            Assert.False(payload.GetProperty("result").GetBoolean());
            Assert.Contains("管理员", payload.GetProperty("logStr").GetString());
            Assert.False(fixture.Room.KickedSpectatorAccounts.ContainsKey(account));
            foreach (var sid in observerIds)
            {
                Assert.True(fixture.Room.Spectators.ContainsKey(sid));
                Assert.Same(fixture.Room, GameRoomManager.GetRoomBySession(sid));
            }
        }
        finally
        {
            sessions.TryRemove(session.SessionId, out _);
            await session.StopSenderAsync();
        }
    }

    [Fact]
    public void 普通观战者仍可被移除且无法再次进入()
    {
        using var fixture = new RoomFixture("open");
        var sid = Guid.NewGuid().ToString("N");
        GameRoomManager.AddSpectator(fixture.Room.RoomId, sid, "普通观众", "释迦");
        GameRoomManager.KickSpectator(fixture.Room.PlayerSessionIds[0], "普通观众");
        Assert.Empty(fixture.Room.Spectators);
        Assert.Null(GameRoomManager.GetRoomBySession(sid));
        Assert.True(fixture.Room.KickedSpectatorAccounts.ContainsKey("普通观众"));
        GameRoomManager.AddSpectator(fixture.Room.RoomId, sid, "普通观众", "释迦");
        Assert.Empty(fixture.Room.Spectators);
    }

    private static ConcurrentDictionary<string, T> GetDictionary<T>(Type type, string name)
        => (ConcurrentDictionary<string, T>)type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    private sealed class RoomFixture : IDisposable
    {
        private readonly ConcurrentDictionary<string, GameRoomManager.RoomEntry> _rooms = GetDictionary<GameRoomManager.RoomEntry>(typeof(GameRoomManager), "_rooms");
        private readonly ConcurrentDictionary<string, string> _sessionRooms = GetDictionary<string>(typeof(GameRoomManager), "_sessionRoom");
        public GameRoomManager.RoomEntry Room { get; }

        public RoomFixture(string mode)
        {
            // 只验证观战准入和成员管理，快照任务保留在未启动的房间队列中。
            Room = new()
            {
                RoomId = $"admin-spectating-{Guid.NewGuid():N}",
                Engine = null!,
                PlayerSessionIds = [Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")],
                PlayerAccounts = ["玩家甲", "玩家乙"],
                PlayerDisplayNames = ["玩家甲", "玩家乙"],
                SpectateModes = [mode, mode],
                SpectateCodes = ["123456", "123456"],
            };
            Assert.True(_rooms.TryAdd(Room.RoomId, Room));
            for (var i = 0; i < 2; i++) BindPlayer(i, Room.PlayerSessionIds[i]);
        }

        public void BindPlayer(int seat, string sid)
        {
            _sessionRooms.TryRemove(Room.PlayerSessionIds[seat], out _);
            Room.PlayerSessionIds[seat] = sid;
            _sessionRooms[sid] = Room.RoomId;
        }

        public void Dispose()
        {
            _rooms.TryRemove(Room.RoomId, out _);
            foreach (var pair in _sessionRooms.Where(pair => pair.Value == Room.RoomId))
                _sessionRooms.TryRemove(pair.Key, out _);
        }
    }
}
