using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

[CollectionDefinition("观战列表广播", DisableParallelization = true)]
public sealed class SpectatorListBroadcastCollectionDefinition;

[Collection("观战列表广播")]
public sealed class SpectatorListBroadcastTests
{
    private static readonly FieldInfo SessionsField = typeof(WebSocketBridge).GetField(
        "Sessions", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public async Task 观战列表_双方获得权限详情而观战者只获得包含自己的公开名单()
    {
        var sessions = Assert.IsType<ConcurrentDictionary<string, WsSession>>(
            SessionsField.GetValue(null));
        var player0 = CaptureSession();
        var player1 = CaptureSession();
        var spectatorA = CaptureSession();
        var spectatorB = CaptureSession();
        var captures = new[] { player0, player1, spectatorA, spectatorB };

        foreach (var capture in captures)
            Assert.True(sessions.TryAdd(capture.Session.SessionId, capture.Session));

        try
        {
            var room = new GameRoomManager.RoomEntry
            {
                RoomId = $"spectator-list-{Guid.NewGuid():N}",
                Engine = null!,
                PlayerSessionIds = [player0.Session.SessionId, player1.Session.SessionId],
                PlayerAccounts = ["player-a", "player-b"],
                PlayerDisplayNames = ["玩家甲", "玩家乙"],
            };
            room.Spectators[spectatorA.Session.SessionId] = new SpectatorConnection
            {
                SessionId = spectatorA.Session.SessionId,
                Account = "secret-observer-a",
                DisplayName = "A观众",
                ViewPlayerIndex = 0,
                HandVisible = true,
            };
            room.Spectators[spectatorB.Session.SessionId] = new SpectatorConnection
            {
                SessionId = spectatorB.Session.SessionId,
                Account = "secret-observer-b",
                DisplayName = "B观众",
                ViewPlayerIndex = 1,
                HandVisible = false,
            };

            WebSocketBridge.BroadcastSpectatorList(room);

            var player0Payload = JsonSerializer.SerializeToElement(
                await player0.Message.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            var player1Payload = JsonSerializer.SerializeToElement(
                await player1.Message.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            var spectatorAPayload = JsonSerializer.SerializeToElement(
                await spectatorA.Message.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            var spectatorBPayload = JsonSerializer.SerializeToElement(
                await spectatorB.Message.Task.WaitAsync(TimeSpan.FromSeconds(2)));

            AssertPlayerPayload(player0Payload, viewingA: true);
            AssertPlayerPayload(player1Payload, viewingA: false);
            AssertPublicSpectatorPayload(spectatorAPayload);
            AssertPublicSpectatorPayload(spectatorBPayload);
        }
        finally
        {
            foreach (var capture in captures)
            {
                sessions.TryRemove(capture.Session.SessionId, out _);
                await capture.Session.StopSenderAsync();
            }
        }
    }

    private static (WsSession Session, TaskCompletionSource<object> Message) CaptureSession()
    {
        var message = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new WsSession { Socket = null! };
        session.StartSender(outbound =>
        {
            message.TrySetResult(outbound.Data);
            return Task.CompletedTask;
        });
        return (session, message);
    }

    private static void AssertPlayerPayload(JsonElement payload, bool viewingA)
    {
        Assert.Equal("MsgSpectatorList", payload.GetProperty("proto").GetString());
        Assert.Collection(
            Names(payload),
            item => Assert.Equal("A观众", item),
            item => Assert.Equal("B观众", item));

        var details = payload.GetProperty("details").EnumerateArray().ToArray();
        Assert.Equal(2, details.Length);
        Assert.Equal("secret-observer-a", details[0].GetProperty("account").GetString());
        Assert.Equal(viewingA, details[0].GetProperty("viewingYou").GetBoolean());
        Assert.True(details[0].GetProperty("handVisible").GetBoolean());
        Assert.Equal(!viewingA, details[1].GetProperty("viewingYou").GetBoolean());
    }

    private static void AssertPublicSpectatorPayload(JsonElement payload)
    {
        Assert.Equal("MsgSpectatorList", payload.GetProperty("proto").GetString());
        Assert.Collection(
            Names(payload),
            item => Assert.Equal("A观众", item),
            item => Assert.Equal("B观众", item));
        Assert.False(payload.TryGetProperty("details", out _));

        var json = payload.GetRawText();
        Assert.DoesNotContain("secret-observer", json, StringComparison.Ordinal);
        Assert.DoesNotContain("viewingYou", json, StringComparison.Ordinal);
        Assert.DoesNotContain("handVisible", json, StringComparison.Ordinal);
    }

    private static string[] Names(JsonElement payload)
        => payload.GetProperty("spectators")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
}
