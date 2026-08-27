namespace GrandUMI;

public static class AdministratorPolicy
{
    private static readonly HashSet<string> AuthorizedAccounts = new(StringComparer.Ordinal)
    {
        "释迦",
        "释迦2号",
        "栗子",
    };

    public static bool IsAuthorized(string? account)
        => account is not null && AuthorizedAccounts.Contains(account);

    /// <summary>仅供服务启动迁移时固化“当时已存在”的白名单初始化管理员。</summary>
    public static IReadOnlyList<string> GetAuthorizedAccounts()
        => AuthorizedAccounts.Order(StringComparer.Ordinal).ToArray();
}

public static class GlobalAnnouncementPolicy
{
    public const int MaximumContentLength = 200;
    public const int RankedWinStreakAnnouncementThreshold = 3;

    public static bool IsAuthorized(string? account)
        => AdministratorPolicy.IsAuthorized(account);

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
        var faction = FormatFaction(defeatedFaction);
        var tier = string.IsNullOrWhiteSpace(defeatedTier) ? "未知段位" : defeatedTier.Trim();
        return Normalize($"{player} 打飞了“{faction}”的{tier}，完成了{FormatChineseNumber(winStreak)}连胜！");
    }

    public static string? FormatRankedWinStreakEnded(
        string? defeatedPlayerName,
        int endedWinStreak,
        string? winnerFaction,
        string? winnerName)
    {
        if (endedWinStreak < RankedWinStreakAnnouncementThreshold) return null;
        var defeatedPlayer = string.IsNullOrWhiteSpace(defeatedPlayerName) ? "玩家" : defeatedPlayerName.Trim();
        var winner = string.IsNullOrWhiteSpace(winnerName) ? "玩家" : winnerName.Trim();
        return Normalize($"{defeatedPlayer}的{FormatChineseNumber(endedWinStreak)}连胜 被 {FormatFaction(winnerFaction)} 的{winner} 终结了");
    }

    private static string FormatFaction(string? faction) => faction?.Trim().ToLowerInvariant() switch
    {
        "pirate" => "海贼阵营",
        "marine" => "海军阵营",
        "government" => "世界政府阵营",
        _ => "未知阵营",
    };

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
