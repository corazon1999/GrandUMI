using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

public sealed class SessionReplacementTests
{
    [Fact]
    public async Task 终止通知发出后_发送循环停止且拒绝后续消息()
    {
        var sent = new List<string>();
        var session = new WsSession();
        session.StartSender(message =>
        {
            sent.Add(JsonSerializer.Serialize(message.Data));
            return Task.CompletedTask;
        });

        var delivered = await session.EnqueueTerminalAsync(new
        {
            proto = "MsgSessionReplaced",
            reason = "账号已在其他地方登录，请重新登录。",
        });
        await session.StopSenderAsync();

        Assert.True(delivered);
        Assert.False(session.Enqueue(new { proto = "MsgOnlineCount" }, isStateSnapshot: false));
        var payload = Assert.Single(sent);
        Assert.Contains("MsgSessionReplaced", payload);
        Assert.Contains("账号已在其他地方登录", payload);
    }
}
