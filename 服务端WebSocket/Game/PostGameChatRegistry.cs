using System.Collections.Concurrent;

namespace GrandUMI.Game;

/// <summary>已经结束的对局可继续聊天的接收范围。</summary>
public sealed record GameChatAudience(string[] PlayerSessionIds, string[] RecipientSessionIds);

/// <summary>
/// 仅保留赛后聊天所需的会话 ID，不持有 GameEngine 或牌局状态。
/// 玩家返回大厅、加入新对局或超过保留时间后，会从对应聊天组解绑。
/// </summary>
public sealed class PostGameChatRegistry
{
    private sealed class ChatGroup
    {
        public ChatGroup(string[] playerSessionIds, string[] participantSessionIds, DateTime expiresAtUtc)
        {
            PlayerSessionIds = playerSessionIds;
            ParticipantSessionIds = participantSessionIds;
            ExpiresAtUtc = expiresAtUtc;
        }

        public string[] PlayerSessionIds { get; }
        public string[] ParticipantSessionIds { get; }
        public DateTime ExpiresAtUtc { get; }
    }

    private readonly ConcurrentDictionary<string, ChatGroup> _groupsBySession = new(StringComparer.Ordinal);
    private readonly TimeSpan _retention;
    private readonly Func<DateTime> _utcNow;

    public PostGameChatRegistry(TimeSpan retention, Func<DateTime>? utcNow = null)
    {
        if (retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention));
        _retention = retention;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>登记一场刚结束的对局；双方玩家始终包含在接收范围内。</summary>
    public void Register(IEnumerable<string> playerSessionIds, IEnumerable<string> spectatorSessionIds)
    {
        var now = _utcNow();
        PurgeExpired(now);

        var players = playerSessionIds
            .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var participants = players
            .Concat(spectatorSessionIds)
            .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (participants.Length == 0) return;

        var group = new ChatGroup(players, participants, now + _retention);
        foreach (var sessionId in participants)
            _groupsBySession[sessionId] = group;
    }

    /// <summary>取得发送者仍绑定的赛后聊天组，并过滤已经离开或加入其他组的接收者。</summary>
    public GameChatAudience? GetAudience(string sessionId)
    {
        if (!_groupsBySession.TryGetValue(sessionId, out var group)) return null;
        if (group.ExpiresAtUtc <= _utcNow())
        {
            RemoveGroup(group);
            return null;
        }

        var recipients = group.ParticipantSessionIds
            .Where(participant =>
                _groupsBySession.TryGetValue(participant, out var current)
                && ReferenceEquals(current, group))
            .ToArray();
        return recipients.Contains(sessionId, StringComparer.Ordinal)
            ? new GameChatAudience(group.PlayerSessionIds, recipients)
            : null;
    }

    /// <summary>让单个会话离开原赛后聊天组，不影响仍停留在结算页的其他人。</summary>
    public void Leave(string sessionId)
    {
        _groupsBySession.TryRemove(sessionId, out _);
    }

    private void PurgeExpired(DateTime now)
    {
        foreach (var group in _groupsBySession.Values)
            if (group.ExpiresAtUtc <= now)
                RemoveGroup(group);
    }

    private void RemoveGroup(ChatGroup group)
    {
        foreach (var sessionId in group.ParticipantSessionIds)
            ((ICollection<KeyValuePair<string, ChatGroup>>)_groupsBySession)
                .Remove(new KeyValuePair<string, ChatGroup>(sessionId, group));
    }
}
