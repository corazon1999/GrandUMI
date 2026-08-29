namespace GrandUMI.Game;

/// <summary>动作真实来源；System 动作只能推进重放，不得成为真人训练标签。</summary>
public enum GameActionSource
{
    Player,
    System,
}

internal static class GameActionSourceWire
{
    public static string Value(GameActionSource source)
        => source switch
        {
            GameActionSource.Player => "player",
            GameActionSource.System => "system",
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

    public static string CorrelationId(string? requestId, GameActionSource source)
    {
        var normalized = requestId?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized) && normalized.Length <= 128)
            return normalized;
        var prefix = source == GameActionSource.System ? "system" : "server";
        return $"{prefix}-{Guid.NewGuid():N}";
    }
}
