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
    /// <summary>遵循当前环境禁限卡表的休闲公开匹配。</summary>
    CasualStandard,
    /// <summary>放宽标准轮换限制的休闲公开匹配；仍执行官网禁卡表。新建对局使用此值，旧快照 Casual 仍可恢复。</summary>
    CasualWild,
}
