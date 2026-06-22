using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Wombat.Network.Protocols.Framing;

public sealed class LengthFieldMessagePipe : IMessagePipe
{
    private readonly LengthField _lengthField;
    private readonly int _maxPayloadLength;

    public LengthFieldMessagePipe()
        : this(LengthField.FourBytes)
    {
    }

    public LengthFieldMessagePipe(LengthField lengthField, int maxPayloadLength = int.MaxValue)
    {
        if (!IsSupported(lengthField))
        {
            throw new NotSupportedException("Specified length field is not supported.");
        }

        if (maxPayloadLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPayloadLength));
        }

        _lengthField = lengthField;
        _maxPayloadLength = maxPayloadLength;
    }

    public async ValueTask WriteAsync(
        PipeWriter writer,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        WriteLengthPrefix(writer, payload.Length);
        writer.Write(payload.Span);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool TryRead(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> payload)
    {
        var prefixLength = (int)_lengthField;
        if (buffer.Length < prefixLength)
        {
            payload = default;
            return false;
        }

        var prefix = buffer.Slice(0, prefixLength);
        var payloadLength = ReadLength(prefix);
        if (payloadLength > _maxPayloadLength)
        {
            throw new InvalidOperationException("Payload length exceeds the configured limit.");
        }

        var frameLength = prefixLength + payloadLength;
        if (buffer.Length < frameLength)
        {
            payload = default;
            return false;
        }

        payload = buffer.Slice(prefixLength, payloadLength);
        buffer = buffer.Slice(frameLength);
        return true;
    }

    private void WriteLengthPrefix(PipeWriter writer, int payloadLength)
    {
        if (payloadLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadLength));
        }

        switch (_lengthField)
        {
            case LengthField.OneByte:
                if (payloadLength > byte.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(payloadLength));
                }

                var oneByteSpan = writer.GetSpan(1);
                oneByteSpan[0] = (byte)payloadLength;
                writer.Advance(1);
                break;
            case LengthField.TwoBytes:
                if (payloadLength > ushort.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(payloadLength));
                }

                var twoByteSpan = writer.GetSpan(2);
                twoByteSpan[0] = (byte)(payloadLength >> 8);
                twoByteSpan[1] = (byte)payloadLength;
                writer.Advance(2);
                break;
            case LengthField.FourBytes:
                var fourByteSpan = writer.GetSpan(4);
                var unsignedPayloadLength = (uint)payloadLength;
                fourByteSpan[0] = (byte)(unsignedPayloadLength >> 24);
                fourByteSpan[1] = (byte)(unsignedPayloadLength >> 16);
                fourByteSpan[2] = (byte)(unsignedPayloadLength >> 8);
                fourByteSpan[3] = (byte)unsignedPayloadLength;
                writer.Advance(4);
                break;
            case LengthField.EigthBytes:
                var eightByteSpan = writer.GetSpan(8);
                var longPayloadLength = (ulong)payloadLength;
                eightByteSpan[0] = (byte)(longPayloadLength >> 56);
                eightByteSpan[1] = (byte)(longPayloadLength >> 48);
                eightByteSpan[2] = (byte)(longPayloadLength >> 40);
                eightByteSpan[3] = (byte)(longPayloadLength >> 32);
                eightByteSpan[4] = (byte)(longPayloadLength >> 24);
                eightByteSpan[5] = (byte)(longPayloadLength >> 16);
                eightByteSpan[6] = (byte)(longPayloadLength >> 8);
                eightByteSpan[7] = (byte)longPayloadLength;
                writer.Advance(8);
                break;
            default:
                throw new NotSupportedException("Specified length field is not supported.");
        }
    }

    private long ReadLength(in ReadOnlySequence<byte> prefix)
    {
        switch (_lengthField)
        {
            case LengthField.OneByte:
                return prefix.First.Span[0];
            case LengthField.TwoBytes:
                return ReadTwoByteLength(prefix);
            case LengthField.FourBytes:
                return ReadFourByteLength(prefix);
            case LengthField.EigthBytes:
                return ReadEightByteLength(prefix);
            default:
                throw new NotSupportedException("Specified length field is not supported.");
        }
    }

    private static int ReadTwoByteLength(in ReadOnlySequence<byte> prefix)
    {
        Span<byte> buffer = stackalloc byte[2];
        prefix.CopyTo(buffer);
        return buffer[0] << 8 | buffer[1];
    }

    private static int ReadFourByteLength(in ReadOnlySequence<byte> prefix)
    {
        Span<byte> buffer = stackalloc byte[4];
        prefix.CopyTo(buffer);
        return buffer[0] << 24 |
            buffer[1] << 16 |
            buffer[2] << 8 |
            buffer[3];
    }

    private static long ReadEightByteLength(in ReadOnlySequence<byte> prefix)
    {
        Span<byte> buffer = stackalloc byte[8];
        prefix.CopyTo(buffer);

        var high = (uint)(buffer[0] << 24 |
            buffer[1] << 16 |
            buffer[2] << 8 |
            buffer[3]);
        var low = (uint)(buffer[4] << 24 |
            buffer[5] << 16 |
            buffer[6] << 8 |
            buffer[7]);

        return ((long)high << 32) | low;
    }

    private static bool IsSupported(LengthField lengthField)
        => lengthField == LengthField.OneByte
        || lengthField == LengthField.TwoBytes
        || lengthField == LengthField.FourBytes
        || lengthField == LengthField.EigthBytes;
}
