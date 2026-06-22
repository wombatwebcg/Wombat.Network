using System.Buffers;
using System.Net;

namespace Wombat.Network.Channels;

public readonly struct ReceivedMessage
{
    public ReceivedMessage(ReadOnlySequence<byte> payload, EndPoint remoteEndPoint)
    {
        Payload = payload;
        RemoteEndPoint = remoteEndPoint;
    }

    public ReadOnlySequence<byte> Payload { get; }
    public EndPoint RemoteEndPoint { get; }
}
