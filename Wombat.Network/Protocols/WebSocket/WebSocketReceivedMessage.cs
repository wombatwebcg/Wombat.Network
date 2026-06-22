using System;
using System.Buffers;

namespace Wombat.Network.Protocols.WebSocket;

public readonly struct WebSocketReceivedMessage
{
    public WebSocketReceivedMessage(WebSocketMessageType messageType, ReadOnlySequence<byte> payload)
    {
        MessageType = messageType;
        Payload = payload;
    }

    public WebSocketMessageType MessageType { get; }
    public ReadOnlySequence<byte> Payload { get; }
}
