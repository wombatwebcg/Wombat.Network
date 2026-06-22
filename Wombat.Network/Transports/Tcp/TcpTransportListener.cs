using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Transports.Abstractions;

namespace Wombat.Network.Transports.Tcp;

public sealed class TcpTransportListener : ITransportListener, IDisposable
{
    private readonly IPEndPoint _localEndPoint;
    private readonly int _backlog;
    private readonly PipeOptions _pipeOptions;
    private Socket _listener;

    public TcpTransportListener(IPEndPoint localEndPoint, int backlog = 20, PipeOptions pipeOptions = null)
    {
        _localEndPoint = localEndPoint ?? throw new ArgumentNullException(nameof(localEndPoint));
        _backlog = backlog;
        _pipeOptions = pipeOptions;
    }

    public EndPoint LocalEndPoint => _listener?.LocalEndPoint ?? _localEndPoint;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_listener != null)
        {
            throw new InvalidOperationException("Listener already started.");
        }

        var listener = new Socket(_localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(_localEndPoint);
        listener.Listen(_backlog);
        _listener = listener;
        return Task.CompletedTask;
    }

    public async Task<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default)
    {
        if (_listener == null)
        {
            throw new InvalidOperationException("Listener not started.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var socket = await _listener.AcceptAsync().ConfigureAwait(false);
        return new TcpTransportConnection(socket, _pipeOptions);
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var listener = Interlocked.Exchange(ref _listener, null);
        if (listener != null)
        {
            try { listener.Dispose(); } catch { }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        CloseAsync().GetAwaiter().GetResult();
    }
}
