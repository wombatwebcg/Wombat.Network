using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Wombat.Network.Transports.Udp;

public interface IUdpTransport
{
    string Id { get; }
    EndPoint LocalEndPoint { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, EndPoint remoteEndPoint, CancellationToken cancellationToken = default);
    ValueTask<ReceivedDatagram?> ReceiveAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}
