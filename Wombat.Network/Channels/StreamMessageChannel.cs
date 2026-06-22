using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Protocols;
using Wombat.Network.Transports.Abstractions;

namespace Wombat.Network.Channels;

public sealed class StreamMessageChannel : IMessageChannel
{
    private readonly ITransportConnection _connection;
    private readonly IMessagePipe _messagePipe;

    public StreamMessageChannel(ITransportConnection connection, IMessagePipe messagePipe)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _messagePipe = messagePipe ?? throw new ArgumentNullException(nameof(messagePipe));
    }

    public string Id => _connection.Id;

    public async ValueTask SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        await _messagePipe.WriteAsync(_connection.Transport.Output, message, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ReceivedMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var result = await _connection.Transport.Input.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;
            var workingBuffer = buffer;

            try
            {
                if (_messagePipe.TryRead(ref workingBuffer, out var payload))
                {
                    _connection.Transport.Input.AdvanceTo(workingBuffer.Start, workingBuffer.End);
                    return new ReceivedMessage(payload, _connection.RemoteEndPoint);
                }

                if (result.IsCompleted)
                {
                    _connection.Transport.Input.AdvanceTo(buffer.End);
                    return null;
                }

                _connection.Transport.Input.AdvanceTo(buffer.Start, buffer.End);
            }
            catch
            {
                _connection.Transport.Input.AdvanceTo(buffer.End);
                throw;
            }
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
        => _connection.CloseAsync(cancellationToken);
}
