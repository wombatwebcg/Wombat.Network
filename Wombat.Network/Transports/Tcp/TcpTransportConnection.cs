using Pipelines.Sockets.Unofficial;
using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Transports.Abstractions;

namespace Wombat.Network.Transports.Tcp;

public sealed class TcpTransportConnection : ITransportConnection, IDisposable
{
    private readonly SocketConnection _transport;
    private int _closed;

    public TcpTransportConnection(Socket socket, PipeOptions pipeOptions = null, string id = null)
        : this(SocketConnection.Create(socket ?? throw new ArgumentNullException(nameof(socket)), pipeOptions, name: id), id)
    {
    }

    public TcpTransportConnection(SocketConnection transport, string id = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    }

    public string Id { get; }
    public EndPoint LocalEndPoint => _transport.Socket.LocalEndPoint;
    public EndPoint RemoteEndPoint => _transport.Socket.RemoteEndPoint;
    public IDuplexPipe Transport => _transport;

    public static async Task<TcpTransportConnection> ConnectAsync(
        EndPoint remoteEndPoint,
        PipeOptions pipeOptions = null,
        string id = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transport = await SocketConnection.ConnectAsync(remoteEndPoint, pipeOptions, name: id).ConfigureAwait(false);
        return new TcpTransportConnection(transport, id);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            try { _transport.Input.Complete(); } catch { }
            try { _transport.Output.Complete(); } catch { }
            _transport.Dispose();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        CloseAsync().GetAwaiter().GetResult();
    }
}
