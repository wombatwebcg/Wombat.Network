namespace Wombat.Network.Protocols.WebSocket;

public enum WebSocketMessageType
{
    Text = 1,
    Binary = 2,
    Close = 8,
    Ping = 9,
    Pong = 10,
}
