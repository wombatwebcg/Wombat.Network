using System;
using System.Net;

namespace Wombat.Network.Transports.Udp;

public readonly struct ReceivedDatagram
{
    public ReceivedDatagram(ReadOnlyMemory<byte> payload, EndPoint remoteEndPoint)
    {
        Payload = payload;
        RemoteEndPoint = remoteEndPoint;
    }

    public ReadOnlyMemory<byte> Payload { get; }
    public EndPoint RemoteEndPoint { get; }
}
