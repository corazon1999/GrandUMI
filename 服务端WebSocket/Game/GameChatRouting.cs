using GrandUMI.Game.Tournaments;

namespace GrandUMI.Game;

/// <summary>赛事聊天按本桌身份隔离；管理权限始终由服务端权威赛事信息决定。</summary>
public static class GameChatRouting
{
    public const string Players = "players";
    public const string Spectators = "spectators";
    public const string Room = "room";

    public static bool CanModerate(Tournament? tournament, string? account, bool administrator)
        => !string.IsNullOrWhiteSpace(account) && (administrator || tournament is not null
            && (TournamentStore.CanHost(tournament, account, false) || TournamentStore.CanJudge(tournament, account, false)));

    public static string[] Channels(bool separated, bool player, bool moderator)
        => !separated ? [Room] : moderator ? [Players, Spectators] : [player ? Players : Spectators];

    public static string? SelectChannel(bool separated, bool player, bool moderator, string? requested)
    {
        var channel = string.IsNullOrWhiteSpace(requested) ? separated ? player ? Players : Spectators : Room : requested;
        return Channels(separated, player, moderator).Contains(channel, StringComparer.Ordinal) ? channel : null;
    }

    public static bool CanReceive(string channel, bool player, bool moderator)
        => channel == Room || moderator || channel == (player ? Players : Spectators);
}
