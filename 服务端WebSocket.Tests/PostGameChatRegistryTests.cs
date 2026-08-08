using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class PostGameChatRegistryTests
{
    [Fact]
    public void 结算后_双方与观战者仍在同一聊天组()
    {
        var registry = new PostGameChatRegistry(TimeSpan.FromMinutes(30));
        registry.Register(new[] { "p0", "p1" }, new[] { "spectator" });

        var playerAudience = registry.GetAudience("p0");
        var spectatorAudience = registry.GetAudience("spectator");

        Assert.NotNull(playerAudience);
        Assert.Equal(new[] { "p0", "p1" }, playerAudience.PlayerSessionIds);
        Assert.Equal(new[] { "p0", "p1", "spectator" }, playerAudience.RecipientSessionIds);
        Assert.NotNull(spectatorAudience);
        Assert.Equal(playerAudience.PlayerSessionIds, spectatorAudience.PlayerSessionIds);
        Assert.Equal(playerAudience.RecipientSessionIds, spectatorAudience.RecipientSessionIds);
    }

    [Fact]
    public void 离开结算页后_不会继续收到原对局消息()
    {
        var registry = new PostGameChatRegistry(TimeSpan.FromMinutes(30));
        registry.Register(new[] { "p0", "p1" }, Array.Empty<string>());

        registry.Leave("p1");

        Assert.Null(registry.GetAudience("p1"));
        Assert.Equal(new[] { "p0" }, registry.GetAudience("p0")?.RecipientSessionIds);
    }

    [Fact]
    public void 加入新聊天组后_旧对局不会把消息串入新对局()
    {
        var registry = new PostGameChatRegistry(TimeSpan.FromMinutes(30));
        registry.Register(new[] { "p0", "old-opponent" }, Array.Empty<string>());
        registry.Register(new[] { "p0", "new-opponent" }, Array.Empty<string>());

        Assert.Equal(new[] { "p0", "new-opponent" }, registry.GetAudience("p0")?.RecipientSessionIds);
        Assert.Equal(new[] { "old-opponent" }, registry.GetAudience("old-opponent")?.RecipientSessionIds);
    }

    [Fact]
    public void 超过保留时间后_赛后聊天组自动失效()
    {
        var now = new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc);
        var registry = new PostGameChatRegistry(TimeSpan.FromMinutes(30), () => now);
        registry.Register(new[] { "p0", "p1" }, Array.Empty<string>());

        now = now.AddMinutes(31);

        Assert.Null(registry.GetAudience("p0"));
        Assert.Null(registry.GetAudience("p1"));
    }
}
