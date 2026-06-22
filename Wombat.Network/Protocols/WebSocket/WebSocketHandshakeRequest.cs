using System;
using System.Collections.Generic;

namespace Wombat.Network.Protocols.WebSocket;

public sealed class WebSocketHandshakeRequest
{
    public string Method { get; set; }
    public string RequestTarget { get; set; }
    public string HttpVersion { get; set; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
}
