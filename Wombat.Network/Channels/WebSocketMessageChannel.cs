using System;
using System.Buffers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Protocols.WebSocket;
using Wombat.Network.Transports.Abstractions;

namespace Wombat.Network.Channels;

public sealed class WebSocketMessageChannel
{
    private readonly ITransportConnection _connection;
    private readonly WebSocketFrameCodec _codec;
    private readonly bool _isClient;

    public WebSocketMessageChannel(ITransportConnection connection, bool isClient, WebSocketFrameCodec codec = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _codec = codec ?? new WebSocketFrameCodec();
        _isClient = isClient;
    }

    public string Id => _connection.Id;

    public ValueTask SendBinaryAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        => SendFrameAsync(_codec.EncodeBinary(payload, _isClient), cancellationToken);

    public ValueTask SendTextAsync(string text, CancellationToken cancellationToken = default)
        => SendFrameAsync(_codec.EncodeText(text, _isClient), cancellationToken);

    public ValueTask SendPingAsync(ReadOnlyMemory<byte> payload = default, CancellationToken cancellationToken = default)
        => SendFrameAsync(_codec.EncodePing(payload, _isClient), cancellationToken);

    public ValueTask SendPongAsync(ReadOnlyMemory<byte> payload = default, CancellationToken cancellationToken = default)
        => SendFrameAsync(_codec.EncodePong(payload, _isClient), cancellationToken);

    public ValueTask SendCloseAsync(WebSockets.WebSocketCloseCode closeCode = WebSockets.WebSocketCloseCode.NormalClosure, string reason = null, CancellationToken cancellationToken = default)
        => SendFrameAsync(_codec.EncodeClose(closeCode, reason, _isClient), cancellationToken);

    public async ValueTask<WebSocketReceivedMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var result = await _connection.Transport.Input.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;
            var working = buffer;

            try
            {
                if (_codec.TryDecode(ref working, out var decoded))
                {
                    _connection.Transport.Input.AdvanceTo(working.Start, working.End);
                    return decoded;
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

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await SendCloseAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await _connection.CloseAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        _connection.Transport.Output.Write(frame.Span);
        await _connection.Transport.Output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
