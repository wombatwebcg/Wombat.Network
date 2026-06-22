using System;
using System.Buffers;
using System.Text;

namespace Wombat.Network.Protocols.WebSocket;

public sealed class WebSocketFrameCodec
{
    private readonly WebSockets.WebSocketFrameBuilder _builder = new();

    public ReadOnlyMemory<byte> EncodeBinary(ReadOnlyMemory<byte> payload, bool isMasked)
    {
        var buffer = payload.ToArray();
        return _builder.EncodeFrame(new WebSockets.BinaryFrame(buffer, 0, buffer.Length, isMasked));
    }

    public ReadOnlyMemory<byte> EncodeText(string text, bool isMasked)
        => _builder.EncodeFrame(new WebSockets.TextFrame(text ?? string.Empty, isMasked));

    public ReadOnlyMemory<byte> EncodePing(ReadOnlyMemory<byte> payload, bool isMasked)
        => _builder.EncodeFrame(new WebSockets.PingFrame(payload.IsEmpty ? null : Encoding.UTF8.GetString(payload.ToArray()), isMasked));

    public ReadOnlyMemory<byte> EncodePong(ReadOnlyMemory<byte> payload, bool isMasked)
        => _builder.EncodeFrame(new WebSockets.PongFrame(payload.IsEmpty ? null : Encoding.UTF8.GetString(payload.ToArray()), isMasked));

    public ReadOnlyMemory<byte> EncodeClose(WebSockets.WebSocketCloseCode closeCode, string reason, bool isMasked)
        => _builder.EncodeFrame(new WebSockets.CloseFrame(closeCode, reason, isMasked));

    public bool TryDecode(ref ReadOnlySequence<byte> buffer, out WebSocketReceivedMessage message)
    {
        if (buffer.Length < 2)
        {
            message = default;
            return false;
        }

        var headerProbeLength = (int)Math.Min(buffer.Length, 14);
        var headerProbe = buffer.Slice(0, headerProbeLength).ToArray();
        if (!_builder.TryDecodeFrameHeader(headerProbe, 0, headerProbeLength, out var header))
        {
            message = default;
            return false;
        }

        var frameLength = header.Length + header.PayloadLength;
        if (buffer.Length < frameLength)
        {
            message = default;
            return false;
        }

        var frameBytes = buffer.Slice(0, frameLength).ToArray();
        _builder.DecodePayload(frameBytes, 0, header, out var payload, out var payloadOffset, out var payloadCount);
        var payloadSequence = new ReadOnlySequence<byte>(payload, payloadOffset, payloadCount);
        message = new WebSocketReceivedMessage((WebSocketMessageType)header.OpCode, payloadSequence);
        buffer = buffer.Slice(frameLength);
        return true;
    }
}
