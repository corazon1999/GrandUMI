namespace GrandUMI.Game;

internal enum AccountRoomOccupancyKind
{
    Creating,
    ActiveGame,
    TerminalFinalizing,
    StateChanging,
}

internal readonly record struct AccountRoomOccupancy(
    string RoomId,
    AccountRoomOccupancyKind Kind)
{
    internal string ErrorCode => Kind == AccountRoomOccupancyKind.TerminalFinalizing
        ? "terminal_finalizing"
        : "account_in_game";

    internal string ClientState => Kind switch
    {
        AccountRoomOccupancyKind.Creating => "creating",
        AccountRoomOccupancyKind.ActiveGame => "active_game",
        AccountRoomOccupancyKind.TerminalFinalizing => "terminal_finalizing",
        _ => "state_changing",
    };

    internal string PlayerMessage => Kind switch
    {
        AccountRoomOccupancyKind.Creating => "该账号正在创建对局，请稍候重试。",
        AccountRoomOccupancyKind.ActiveGame => "该账号已在一场对局中，请返回对局；如已断线，请重新登录恢复。",
        AccountRoomOccupancyKind.TerminalFinalizing => "上一局已结束，服务器正在完成终局收尾，请稍候重试。",
        _ => "该账号的对局状态正在更新，请稍候重试。",
    };

    internal string SharedLobbyMessage => Kind == AccountRoomOccupancyKind.TerminalFinalizing
        ? "有玩家的上一局正在完成终局收尾，请稍候再开始。"
        : "有玩家已在其他对局中，请返回对应对局后再开始。";
}

internal sealed class AccountRoomOccupiedException : InvalidOperationException
{
    internal AccountRoomOccupiedException(string account, AccountRoomOccupancy occupancy)
        : base($"账号「{account}」已在其他对局中（{occupancy.ClientState}）")
    {
        Account = account;
        Occupancy = occupancy;
    }

    internal string Account { get; }
    internal AccountRoomOccupancy Occupancy { get; }
    internal string PlayerMessage => Occupancy.PlayerMessage;
}
