using System;
using System.Buffers;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Protocols;
using Wombat.Network.Transports.Udp;

namespace Wombat.Network.Channels;

public sealed class DatagramMessageChannel : IMessageChannel
{
    private readonly IUdpTransport _transport;
    private readonly EndPoint _defaultRemoteEndPoint;
    private readonly IMessagePipe _messagePipe;

    public DatagramMessageChannel(IUdpTransport transport, EndPoint defaultRemoteEndPoint = null, IMessagePipe messagePipe = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _defaultRemoteEndPoint = defaultRemoteEndPoint;
        _messagePipe = messagePipe;
    }

    public string Id => _transport.Id;

    public ValueTask SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
        => SendToAsync(message, _defaultRemoteEndPoint, cancellationToken);

    public ValueTask SendToAsync(ReadOnlyMemory<byte> message, EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
    {
        if (_messagePipe == null)
        {
            return _transport.SendAsync(message, remoteEndPoint, cancellationToken);
        }

        return SendFramedAsync(message, remoteEndPoint, cancellationToken);
    }

    public async ValueTask<ReceivedMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var datagram = await _transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        if (!datagram.HasValue)
        {
            return null;
        }

        if (_messagePipe == null)
        {
            return new ReceivedMessage(new ReadOnlySequence<byte>(datagram.Value.Payload), datagram.Value.RemoteEndPoint);
        }

        var buffer = new ReadOnlySequence<byte>(datagram.Value.Payload);
        if (!_messagePipe.TryRead(ref buffer, out var payload))
        {
            return null;
        }

        return new ReceivedMessage(payload, datagram.Value.RemoteEndPoint);
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
        => _transport.CloseAsync(cancellationToken);

    private async ValueTask SendFramedAsync(ReadOnlyMemory<byte> message, EndPoint remoteEndPoint, CancellationToken cancellationToken)
    {
        var pipe = new System.IO.Pipelines.Pipe();
        await _messagePipe.WriteAsync(pipe.Writer, message, cancellationToken).ConfigureAwait(false);
        pipe.Writer.Complete();

        var result = await pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var payload = result.Buffer.ToArray();
            await _transport.SendAsync(payload, remoteEndPoint, cancellationToken).ConfigureAwait(false);
            pipe.Reader.AdvanceTo(result.Buffer.End);
        }
        finally
        {
            pipe.Reader.Complete();
        }
    }
}
