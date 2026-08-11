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

    public static string? FormatRankedWinStreak(
        string? displayName,
        string? defeatedFaction,
        string? defeatedTier,
        int winStreak)
    {
        if (winStreak < RankedWinStreakAnnouncementThreshold) return null;
        var player = string.IsNullOrWhiteSpace(displayName) ? "玩家" : displayName.Trim();
        var faction = defeatedFaction?.Trim().ToLowerInvariant() switch
        {
            "pirate" => "海贼阵营",
            "marine" => "海军阵营",
            "government" => "世界政府阵营",
            _ => "未知阵营",
        };
        var tier = string.IsNullOrWhiteSpace(defeatedTier) ? "未知段位" : defeatedTier.Trim();
        return Normalize($"{player} 打飞了“{faction}”的{tier}，完成了{FormatChineseNumber(winStreak)}连胜！");
    }

    private static string FormatChineseNumber(int value)
    {
        const string digits = "零一二三四五六七八九";
        if (value < 10) return digits[value].ToString();
        if (value == 10) return "十";
        if (value < 20) return $"十{digits[value % 10]}";
        if (value < 100)
        {
            var ones = value % 10 == 0 ? string.Empty : digits[value % 10].ToString();
            return $"{digits[value / 10]}十{ones}";
        }
        return value.ToString();
    }
}
