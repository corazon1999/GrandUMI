using System.Security.Cryptography;

namespace GrandUMI.Game;

public static class SpectatingRules
{
    public const string Open = "open";
    public const string Closed = "closed";
    public const string Friends = "friends";
    public const string Password = "password";
    public static readonly TimeSpan HandRequestCooldown = TimeSpan.FromSeconds(30);

    public static string NormalizeMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        Closed => Closed,
        Friends => Friends,
        Password => Password,
        _ => Open,
    };

    public static string GenerateCode()
        => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static SpectateAccessDecision CheckAccess(
        string? mode,
        bool isFriend,
        string? expectedCode,
        string? providedCode,
        bool wasKicked)
    {
        if (wasKicked) return new(false, "你已被移出本局，无法再次观战");
        return NormalizeMode(mode) switch
        {
            Closed => new(false, "该玩家已关闭观战"),
            Friends when !isFriend => new(false, "该对局仅允许好友观战"),
            Password when !string.Equals(expectedCode, providedCode?.Trim(), StringComparison.Ordinal)
                => new(false, "观战码错误"),
            _ => new(true, null),
        };
    }
}

public readonly record struct SpectateAccessDecision(bool Allowed, string? Error);

public sealed class SpectatorConnection
{
    public required string SessionId { get; init; }
    public required string Account { get; init; }
    public required string DisplayName { get; init; }
    public required int ViewPlayerIndex { get; init; }
    public bool HandVisible { get; set; }
    public DateTime LastHandRequestUtc { get; set; } = DateTime.MinValue;
    public string? PendingRequestId { get; set; }
}

public sealed record SpectatorHandRequest(
    string RequestId,
    string SpectatorSessionId,
    string SpectatorAccount,
    string SpectatorName,
    int PlayerIndex);
