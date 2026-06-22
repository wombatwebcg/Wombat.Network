using System;
using System.Collections.Generic;

namespace Wombat.Network.Protocols.WebSocket;

public sealed class WebSocketHandshakeResponse
{
    public int StatusCode { get; set; }
    public string ReasonPhrase { get; set; }
    public string HttpVersion { get; set; } = "1.1";
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
}
