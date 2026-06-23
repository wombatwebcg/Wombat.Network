using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Wombat.Network.Channels;

namespace Wombat.Network.TestHelper;

public static class NetworkTestHelper
{
    public static void ValidateText(ReceivedMessage? message, string expectedText, string operation)
    {
        Ensure(message.HasValue, $"{operation} should receive one message.");
        var text = Encoding.UTF8.GetString(ToArray(message!.Value.Payload));
        Ensure(text == expectedText, $"{operation} text mismatch.");
    }

    public static void ValidatePayload(ReceivedMessage? message, byte[] expectedPayload, string operation)
    {
        Ensure(message.HasValue, $"{operation} should receive one message.");
        var payload = ToArray(message!.Value.Payload);
        Ensure(payload.AsSpan().SequenceEqual(expectedPayload), $"{operation} payload mismatch.");
    }

    public static void ValidatePayload(ReceivedMessage? message, int expectedLength, byte[] expectedHash, string operation)
    {
        Ensure(message.HasValue, $"{operation} should receive one message.");
        var payload = message!.Value.Payload;
        Ensure(payload.Length == expectedLength, $"{operation} length mismatch.");

        var buffer = ToArray(payload);
        var actualHash = SHA256.HashData(buffer);
        Ensure(actualHash.SequenceEqual(expectedHash), $"{operation} payload mismatch.");
    }

    public static byte[] ToArray(in ReadOnlySequence<byte> sequence)
    {
        var buffer = new byte[(int)sequence.Length];
        sequence.CopyTo(buffer);
        return buffer;
    }

    public static byte[] CreateRepeatedPayload(int length, byte value)
    {
        var buffer = new byte[length];
        Array.Fill(buffer, value);
        return buffer;
    }

    public static string CreateLargeNumericString(int max)
    {
        var builder = new StringBuilder(max * 6);
        for (var i = 1; i <= max; i++)
        {
            builder.Append(i);
        }

        return builder.ToString();
    }

    public static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
