namespace GrandUMI;

public static class GlobalAnnouncementPolicy
{
    public const string AuthorizedAccount = "释迦";
    public const int MaximumContentLength = 200;

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
}
