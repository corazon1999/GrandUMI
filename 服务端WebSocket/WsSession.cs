using System.Net.WebSockets;

namespace GrandUMI;

public class WsSession
{
    public string    SessionId  { get; } = Guid.NewGuid().ToString("N");
    public WebSocket Socket     { get; init; } = null!;

    public string?   Account    { get; set; }
    public string?   PlayerName { get; set; }

    public string?   Deck       { get; set; }
    public bool      IsMatching       { get; set; }
    public string?   CurrentRoomCode  { get; set; }

    /// <summary>玩家设置：是否对所有生命牌都弹"是否发动触发"窗口（反信息泄露）</summary>
    public bool      AlwaysPromptOnLifeReveal { get; set; }

    // 防止并发写 WebSocket
    public SemaphoreSlim WriteLock { get; } = new(1, 1);

    public bool IsLoggedIn => Account is not null;
}
