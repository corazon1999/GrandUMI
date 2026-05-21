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

    // 防止并发写 WebSocket
    public SemaphoreSlim WriteLock { get; } = new(1, 1);

    public bool IsLoggedIn => Account is not null;
}
