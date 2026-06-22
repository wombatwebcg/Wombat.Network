using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Wombat.Network.Transports.Udp;

public sealed class UdpDatagramTransport : IUdpTransport, IDisposable
{
    private readonly IPEndPoint _localEndPoint;
    private readonly IPEndPoint _defaultRemoteEndPoint;
    private UdpClient _udpClient;
    private int _closed;

    public UdpDatagramTransport(IPEndPoint localEndPoint = null, IPEndPoint defaultRemoteEndPoint = null, string id = null)
    {
        _localEndPoint = localEndPoint;
        _defaultRemoteEndPoint = defaultRemoteEndPoint;
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    }

    public string Id { get; }
    public EndPoint LocalEndPoint => _udpClient?.Client?.LocalEndPoint ?? _localEndPoint;
    public EndPoint DefaultRemoteEndPoint => _defaultRemoteEndPoint;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_udpClient != null)
        {
            throw new InvalidOperationException("Transport already started.");
        }

        var udpClient = _localEndPoint == null
            ? new UdpClient(_defaultRemoteEndPoint?.AddressFamily ?? AddressFamily.InterNetwork)
            : new UdpClient(_localEndPoint);

        if (_defaultRemoteEndPoint != null)
        {
            udpClient.Connect(_defaultRemoteEndPoint);
        }

        _udpClient = udpClient;
        return Task.CompletedTask;
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var udpClient = _udpClient ?? throw new InvalidOperationException("Transport not started.");
        var target = remoteEndPoint ?? _defaultRemoteEndPoint;
        if (target == null)
        {
            throw new InvalidOperationException("Remote endpoint is required for UDP send.");
        }

        if (target is not IPEndPoint ipEndPoint)
        {
            throw new NotSupportedException("Only IPEndPoint is supported.");
        }

        var buffer = payload.ToArray();
        if (_defaultRemoteEndPoint != null && Equals(ipEndPoint, _defaultRemoteEndPoint))
        {
            await udpClient.SendAsync(buffer, buffer.Length).ConfigureAwait(false);
            return;
        }

        await udpClient.SendAsync(buffer, buffer.Length, ipEndPoint).ConfigureAwait(false);
    }

    public async ValueTask<ReceivedDatagram?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var udpClient = _udpClient ?? throw new InvalidOperationException("Transport not started.");
        var result = await udpClient.ReceiveAsync().ConfigureAwait(false);
        return new ReceivedDatagram(result.Buffer, result.RemoteEndPoint);
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            var udpClient = Interlocked.Exchange(ref _udpClient, null);
            udpClient?.Dispose();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        CloseAsync().GetAwaiter().GetResult();
    }
}
