namespace GrandUMI.Game;

/// <summary>对局来源；用于统计口径与后续按模式分析。</summary>
public enum MatchKind
{
    /// <summary>旧恢复日志或未显式标注来源的真人对局。</summary>
    UnknownHuman,
    /// <summary>旧客户端进入的普通公开匹配，按休闲对局处理。</summary>
    Matchmaking,
    Casual,
    Ranked,
    RankedWild,
    RoomCode,
    Friendly,
    Bot,
}
