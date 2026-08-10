using Xunit;

namespace GrandUMI.Tests;

public sealed class GlobalAnnouncementPolicyTests
{
    [Fact]
    public void OnlyTheConfiguredAccountCanSendAnnouncements()
    {
        Assert.True(GlobalAnnouncementPolicy.IsAuthorized("释迦"));
        Assert.False(GlobalAnnouncementPolicy.IsAuthorized("释迦 "));
        Assert.False(GlobalAnnouncementPolicy.IsAuthorized("管理员"));
        Assert.False(GlobalAnnouncementPolicy.IsAuthorized(null));
    }

    [Fact]
    public void AnnouncementContentIsTrimmedAndCapped()
    {
        Assert.Equal("服务器维护通知", GlobalAnnouncementPolicy.Normalize("  服务器维护通知  "));
        Assert.Null(GlobalAnnouncementPolicy.Normalize("   "));
        Assert.Equal(GlobalAnnouncementPolicy.MaximumContentLength,
            GlobalAnnouncementPolicy.Normalize(new string('a', GlobalAnnouncementPolicy.MaximumContentLength + 1))!.Length);
    }
}
