using System;
using System.Collections.Generic;
using System.Text;

namespace Wombat.Network.WebSockets
{
    public enum WebSocketState
    {
        None = 0,
        Connecting = 1,
        Open = 2,
        Closing = 3,
        Closed = 5,
    }
}
