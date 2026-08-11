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
        Assert.Null(GlobalAnnouncementPolicy.FormatRankedWinStreak(
            "爱丽丝", "marine", "海军少将", 2));
        Assert.Equal("爱丽丝 打飞了“海军阵营”的海军少将，完成了三连胜！",
            GlobalAnnouncementPolicy.FormatRankedWinStreak(
                "爱丽丝", "marine", "海军少将", 3));
        Assert.Equal("爱丽丝 打飞了“海贼阵营”的船长，完成了八连胜！",
            GlobalAnnouncementPolicy.FormatRankedWinStreak(
                "爱丽丝", "pirate", "船长", 8));
        Assert.Equal("爱丽丝 打飞了“世界政府阵营”的神之骑士团，完成了十一连胜！",
            GlobalAnnouncementPolicy.FormatRankedWinStreak(
                "爱丽丝", "government", "神之骑士团", 11));
    }
}
