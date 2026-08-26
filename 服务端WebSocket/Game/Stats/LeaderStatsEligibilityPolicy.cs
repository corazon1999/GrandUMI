namespace GrandUMI.Game.Stats;

/// <summary>
/// 公开 Leader 统计的对局来源白名单。新增对局来源默认不公开，必须在这里显式评审后放行。
/// </summary>
internal static class LeaderStatsEligibilityPolicy
{
    internal const string PublicMatchKindsSql =
        "'Matchmaking', 'Casual', 'CasualStandard', 'CasualWild', 'Ranked', 'RankedWild'";

    internal static bool IsPublicMatch(MatchKind matchKind)
        => matchKind is MatchKind.Matchmaking
            or MatchKind.Casual
            or MatchKind.CasualStandard
            or MatchKind.CasualWild
            or MatchKind.Ranked
            or MatchKind.RankedWild;

    internal static string ExcludedMatchKindReason(MatchKind matchKind)
        => matchKind switch
        {
            MatchKind.Bot => "bot",
            MatchKind.Friendly or MatchKind.RoomCode => "private_match",
            _ => "unsupported_match_kind",
        };
}
