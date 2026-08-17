using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

public sealed class SessionReplacementTests
{
    [Fact]
    public async Task 终止通知发出后_发送循环停止且拒绝后续消息()
    {
        var sent = new List<object>();
        var session = new WsSession();
        session.StartSender(message =>
        {
            sent.Add(message.Data);
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
        var payload = JsonSerializer.SerializeToElement(Assert.Single(sent));
        Assert.Equal("MsgSessionReplaced", payload.GetProperty("proto").GetString());
        Assert.Contains("账号已在其他地方登录",
            payload.GetProperty("reason").GetString());
    }
}
