namespace GrandUMI;

public static class GlobalAnnouncementPolicy
{
    public const string AuthorizedAccount = "释迦";
    public const int MaximumContentLength = 200;
    public const int RankedWinStreakAnnouncementThreshold = 3;

    public static bool IsAuthorized(string? account)
        => string.Equals(account, AuthorizedAccount, StringComparison.Ordinal);

    public static string? Normalize(string? content)
    {
        var normalized = content?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        return normalized.Length <= MaximumContentLength
            ? normalized
            : normalized[..MaximumContentLength];
    }

    public static string? FormatRankedWinStreak(string? displayName, int winStreak)
    {
        if (winStreak < RankedWinStreakAnnouncementThreshold) return null;
        var player = string.IsNullOrWhiteSpace(displayName) ? "玩家" : displayName.Trim();
        return Normalize($"恭喜 {player} 在排位赛中取得 {winStreak} 连胜！");
    }
}
