namespace GrandUMI.Game;

/// <summary>对局来源；用于统计口径与后续按模式分析。</summary>
public enum MatchKind
{
    /// <summary>旧恢复日志或未显式标注来源的真人对局。</summary>
    UnknownHuman,
    Matchmaking,
    RoomCode,
    Friendly,
    Bot,
}
