using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Wombat.Network.Protocols.Framing;
using Wombat.Network.Protocols.WebSocket;

namespace Wombat.Network.Benchmark;

internal static class BenchmarkData
{
public static byte[] CreatePayload(int size)
    {
        var payload = new byte[size];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 251);
        }

        return payload;
    }

    public static string CreateText(int size)
    {
        var chars = new char[size];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)('a' + (i % 26));
        }

        return new string(chars);
    }

    public static byte[] CreateLengthFieldFrame(LengthField lengthField, int payloadSize, int messageCount)
    {
        var pipe = new Pipe();
        var writer = pipe.Writer;
        var payload = CreatePayload(payloadSize);
        var codec = new LengthFieldMessagePipe(lengthField);

        for (var i = 0; i < messageCount; i++)
        {
            codec.WriteAsync(writer, payload).GetAwaiter().GetResult();
        }

        var result = pipe.Reader.ReadAsync().GetAwaiter().GetResult();
        try
        {
            return result.Buffer.ToArray();
        }
        finally
        {
            pipe.Reader.AdvanceTo(result.Buffer.End);
            pipe.Reader.Complete();
            pipe.Writer.Complete();
        }
    }

    public static byte[] CreateWebSocketBinaryFrame(int payloadSize, bool masked)
    {
        var codec = new WebSocketFrameCodec();
        return codec.EncodeBinary(CreatePayload(payloadSize), masked).ToArray();
    }

    public static byte[] CreateWebSocketTextFrame(int payloadSize, bool masked)
    {
        var codec = new WebSocketFrameCodec();
        return codec.EncodeText(CreateText(payloadSize), masked).ToArray();
    }

}
