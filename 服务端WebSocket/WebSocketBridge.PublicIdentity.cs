using System.Text.Json;
using GrandUMI.Game.Ranked;
using GrandUMI.Persistence;

namespace GrandUMI;

public static partial class WebSocketBridge
{
    private static void OnPublicPlayerIdentities(WsSession session, Dictionary<string, JsonElement> message)
    {
        if (!session.IsLoggedIn || !IsCurrentAccountSession(session)
            || !session.TryConsumeRateLimit("public-player-identities", capacity: 6, refillPerSecond: 2)) return;
        if (!message.TryGetValue("names", out var value) || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() > 40) return;
        var names = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } name
                || string.IsNullOrWhiteSpace(name) || name.Length > PlayerDataStore.MaxDisplayNameLength) return;
            names.Add(name);
        }
        try
        {
            var accounts = _playerDataStore.ResolvePublicPlayerAccounts(names);
            var titles = RankedStore.Default.GetPublicEquippedSeasonTitles(accounts.Values.ToArray());
            var identities = names.Distinct(StringComparer.Ordinal).Select(name => new
            {
                name,
                equippedSeasonTitle = accounts.TryGetValue(name, out var account)
                    ? titles.GetValueOrDefault(account) : null,
            }).ToArray();
            Send(session.SessionId, new { proto = "MsgPublicPlayerIdentities", identities });
        }
        catch (Exception ex) { LogErr($"公开称号读取失败：{ex.Message}"); }
    }

    private static void BroadcastPublicPlayerIdentity(string name, string account)
    {
        // 佩戴或取消后主动通知所有已登录客户端，聊天、好友、榜单同步更新。
        try
        {
            var identity = new { name, equippedSeasonTitle = RankedStore.Default.GetPublicEquippedSeasonTitle(account) };
            foreach (var recipient in Sessions.Values.Where(IsCurrentAccountSession))
                Send(recipient.SessionId, new { proto = "MsgPublicPlayerIdentities", identities = new[] { identity } });
        }
        catch (Exception ex) { LogErr($"公开称号通知失败，客户端将自动重取：{ex.Message}"); }
    }
}
