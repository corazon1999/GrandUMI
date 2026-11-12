using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

[Collection("匹配身份隔离")]
public sealed class AccountRoomRecoveryTests
{
    private static readonly FieldInfo SessionsField = typeof(WebSocketBridge).GetField(
        "Sessions", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo RejectForAccountRoomOccupancyMethod = typeof(WebSocketBridge).GetMethod(
        "RejectForAccountRoomOccupancy", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public async Task 大厅匹配遇到进行中对局_同会话自动重领并补发牌桌状态()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var account = $"recovery-player-{suffix}";
        var opponentAccount = $"recovery-opponent-{suffix}";
        var player = new WsSession { Socket = null!, Account = account, PlayerName = account };
        var opponent = new WsSession { Socket = null!, Account = opponentAccount, PlayerName = opponentAccount };
        var response = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gameState = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.StartSender(outbound =>
        {
            var payload = JsonSerializer.SerializeToElement(outbound.Data);
            switch (payload.GetProperty("proto").GetString())
            {
                case "MsgEnterMatch": response.TrySetResult(payload); break;
                case "MsgGameState": gameState.TrySetResult(payload); break;
            }
            return Task.CompletedTask;
        });
        var sessions = Assert.IsType<ConcurrentDictionary<string, WsSession>>(SessionsField.GetValue(null));
        Assert.True(sessions.TryAdd(player.SessionId, player));
        var deck = string.Join('\n', new[] { "OP15-001" }.Concat(Enumerable.Repeat("OP15-003", 50)));
        var room = GameRoomManager.CreateRoom(
            player.SessionId, account, deck,
            opponent.SessionId, opponentAccount, deck,
            broadcastInitialState: false);

        try
        {
            var rejected = Assert.IsType<bool>(RejectForAccountRoomOccupancyMethod.Invoke(
                null, [player, "MsgEnterMatch"]));
            Assert.True(rejected);

            var rejection = await response.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(rejection.GetProperty("result").GetBoolean());
            Assert.Equal("account_in_game", rejection.GetProperty("errorCode").GetString());
            Assert.Equal("active_game", rejection.GetProperty("roomState").GetString());
            Assert.True(rejection.GetProperty("recoveryRequested").GetBoolean());
            Assert.Contains("返回牌桌", rejection.GetProperty("logStr").GetString());

            var snapshot = await gameState.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("player", snapshot.GetProperty("viewerKind").GetString());
            Assert.False(snapshot.GetProperty("isGameOver").GetBoolean());
            Assert.Same(room, GameRoomManager.GetRoomBySession(player.SessionId));
            Assert.Equal(player.SessionId, room.PlayerSessionIds[0]);
        }
        finally
        {
            GameRoomManager.CleanupRoom(room.RoomId);
            sessions.TryRemove(player.SessionId, out _);
            await player.StopSenderAsync();
        }
    }
}
