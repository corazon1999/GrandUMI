using GrandUMI.Game;

namespace GrandUMI;

/// <summary>邀请对战与房间码对战共用的赛前房间。</summary>
public sealed class DuelLobby
{
    public required string RoomId { get; init; }
    public required MatchKind MatchKind { get; init; }
    public string? JoinCode { get; init; }
    public string?[] Accounts { get; } = new string?[2];
    public string?[] Names { get; } = new string?[2];
    public string?[] Decks { get; } = new string?[2];
    public string?[] DeckNames { get; } = new string?[2];
    public bool[] Ready { get; } = new bool[2];
    public int[] Scores { get; } = new int[2];
    public string State { get; set; } = "lobby";
    public object Gate { get; } = new();

    public bool IsRoomCode => MatchKind == MatchKind.RoomCode;
    public bool IsFull => Accounts[0] is not null && Accounts[1] is not null;

    public int IndexOf(string account)
        => Array.FindIndex(Accounts, value =>
            string.Equals(value, account, StringComparison.OrdinalIgnoreCase));

    /// <summary>仅允许房间码等待房间从一人原子转换为两人。</summary>
    public bool TryAddGuest(string account, string name, string deck, string deckName, out string? error)
    {
        lock (Gate)
        {
            if (!IsRoomCode || State != "lobby")
            {
                error = "房间已经开始或已失效";
                return false;
            }
            if (string.Equals(Accounts[0], account, StringComparison.OrdinalIgnoreCase))
            {
                error = "不能加入自己创建的房间";
                return false;
            }
            if (Accounts[1] is not null)
            {
                error = "房间已有其他玩家";
                return false;
            }

            Accounts[1] = account;
            Names[1] = name;
            Decks[1] = deck;
            DeckNames[1] = deckName;
            Ready[1] = false;
            error = null;
            return true;
        }
    }

    /// <summary>双方准备完成后仅允许一个调用者取得开局权。</summary>
    public bool TryBeginStart(out DuelLobbyStartData? start)
    {
        lock (Gate)
        {
            if (State != "lobby" || !IsFull || !Ready[0] || !Ready[1] ||
                Decks[0] is null || Decks[1] is null)
            {
                start = null;
                return false;
            }

            State = "starting";
            start = new DuelLobbyStartData(
                Accounts[0]!, Accounts[1]!, Decks[0]!, Decks[1]!);
            return true;
        }
    }

    public void CompleteStart(bool success)
    {
        lock (Gate)
        {
            State = success ? "playing" : "lobby";
            Ready[0] = false;
            Ready[1] = false;
        }
    }

    /// <summary>将房间原子标记为关闭；过期清理可限定为仍在等待第二位玩家时才生效。</summary>
    public bool TryClose(bool onlyIfWaitingForGuest = false)
    {
        lock (Gate)
        {
            if (State == "closed") return false;
            if (onlyIfWaitingForGuest && (State != "lobby" || IsFull)) return false;
            State = "closed";
            return true;
        }
    }
}

public sealed record DuelLobbyStartData(
    string HostAccount,
    string GuestAccount,
    string HostDeck,
    string GuestDeck);
