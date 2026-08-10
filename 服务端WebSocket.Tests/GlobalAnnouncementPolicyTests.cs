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

    [Fact]
    public void RankedWinStreakAnnouncementStartsAtThreeWins()
    {
        Assert.Null(GlobalAnnouncementPolicy.FormatRankedWinStreak("爱丽丝", 2));
        Assert.Equal("恭喜 爱丽丝 在排位赛中取得 3 连胜！",
            GlobalAnnouncementPolicy.FormatRankedWinStreak("爱丽丝", 3));
        Assert.Equal("恭喜 爱丽丝 在排位赛中取得 8 连胜！",
            GlobalAnnouncementPolicy.FormatRankedWinStreak("爱丽丝", 8));
    }
}
