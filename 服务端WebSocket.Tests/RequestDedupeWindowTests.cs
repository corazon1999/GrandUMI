using GrandUMI.Game;
using Xunit;

namespace GrandUMIServer.Tests;

public sealed class RequestDedupeWindowTests
{
    [Fact]
    public void 同一玩家同一请求只允许注册一次()
    {
        var window = new RequestDedupeWindow(8, TimeSpan.FromMinutes(10));

        Assert.True(window.TryRegister(0, "request-1"));
        Assert.False(window.TryRegister(0, "request-1"));
        Assert.True(window.TryRegister(1, "request-1"));
    }

    [Fact]
    public void 过期请求会在容量压力下释放()
    {
        var window = new RequestDedupeWindow(2, TimeSpan.FromMinutes(10));
        var start = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(window.TryRegister(0, "old", start));
        Assert.True(window.TryRegister(0, "new", start.AddMinutes(11)));
        Assert.True(window.TryRegister(0, "third", start.AddMinutes(11)));
        Assert.True(window.TryRegister(0, "old", start.AddMinutes(11)));
    }

    [Fact]
    public void 发送失败后可撤销登记以便玩家重试()
    {
        var window = new RequestDedupeWindow(8, TimeSpan.FromMinutes(10));
        Assert.True(window.TryRegister(0, "retryable"));

        window.Remove(0, "retryable");

        Assert.True(window.TryRegister(0, "retryable"));
    }

    [Fact]
    public void 重启快照会恢复未过期请求并继续去重()
    {
        var now = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        var beforeRestart = new RequestDedupeWindow(8, TimeSpan.FromMinutes(10));
        Assert.True(beforeRestart.TryRegister(0, "persisted", now));

        var afterRestart = new RequestDedupeWindow(8, TimeSpan.FromMinutes(10));
        afterRestart.Restore(beforeRestart.Snapshot(now), now.AddMinutes(1));

        Assert.False(afterRestart.TryRegister(0, "persisted", now.AddMinutes(1)));
        Assert.True(afterRestart.TryRegister(1, "persisted", now.AddMinutes(1)));
    }
}
