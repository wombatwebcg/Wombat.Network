using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Transports.Abstractions;

namespace Wombat.Network.Protocols.WebSocket;

public static class WebSocketHandshakeMiddleware
{
    private static readonly byte[] Terminator = Encoding.ASCII.GetBytes("\r\n\r\n");
    private const string UpgradeToken = "websocket";
    private const string ConnectionToken = "Upgrade";
    private const string Version = "13";
    private const string AcceptSuffix = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    public static async Task<WebSocketHandshakeRequest> AcceptServerAsync(
        ITransportConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        var requestText = await ReadHttpMessageAsync(connection.Transport.Input, cancellationToken).ConfigureAwait(false);
        var request = ParseRequest(requestText);
        ValidateClientHandshake(request);

        var response = CreateServerResponse(request.Headers["Sec-WebSocket-Key"]);
        await WriteHttpMessageAsync(connection.Transport.Output, response, cancellationToken).ConfigureAwait(false);
        return request;
    }

    public static async Task AcceptClientAsync(
        ITransportConnection connection,
        string host,
        string path = "/",
        CancellationToken cancellationToken = default)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host is required.", nameof(host));
        }

        var nonce = CreateClientNonce();
        var request = CreateClientRequest(host, path, nonce);
        await WriteHttpMessageAsync(connection.Transport.Output, request, cancellationToken).ConfigureAwait(false);

        var responseText = await ReadHttpMessageAsync(connection.Transport.Input, cancellationToken).ConfigureAwait(false);
        var response = ParseResponse(responseText);
        ValidateServerHandshake(response, nonce);
    }

    private static async Task<string> ReadHttpMessageAsync(PipeReader reader, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;
            if (TryFindTerminator(buffer, out var end))
            {
                var message = buffer.Slice(0, end);
                var text = Encoding.ASCII.GetString(message.ToArray());
                reader.AdvanceTo(end, end);
                return text;
            }

            if (result.IsCompleted)
            {
                reader.AdvanceTo(buffer.End);
                throw new InvalidOperationException("Incomplete HTTP handshake.");
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    private static bool TryFindTerminator(ReadOnlySequence<byte> buffer, out SequencePosition end)
    {
        var bytes = buffer.ToArray();
        for (var i = 0; i <= bytes.Length - Terminator.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < Terminator.Length; j++)
            {
                if (bytes[i + j] != Terminator[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                end = buffer.GetPosition(i + Terminator.Length);
                return true;
            }
        }

        end = default;
        return false;
    }

    private static async Task WriteHttpMessageAsync(PipeWriter writer, string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.ASCII.GetBytes(message);
        writer.Write(bytes);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static WebSocketHandshakeRequest ParseRequest(string text)
    {
        var lines = SplitLines(text);
        if (lines.Length == 0)
        {
            throw new InvalidOperationException("Invalid HTTP request.");
        }

        var first = lines[0].Split(' ');
        if (first.Length < 3)
        {
            throw new InvalidOperationException("Invalid HTTP request line.");
        }

        var request = new WebSocketHandshakeRequest
        {
            Method = first[0],
            RequestTarget = first[1],
            HttpVersion = first[2].Replace("HTTP/", string.Empty),
        };

        FillHeaders(lines, 1, request.Headers);
        return request;
    }

    private static WebSocketHandshakeResponse ParseResponse(string text)
    {
        var lines = SplitLines(text);
        if (lines.Length == 0)
        {
            throw new InvalidOperationException("Invalid HTTP response.");
        }

        var first = lines[0].Split(' ');
        if (first.Length < 3)
        {
            throw new InvalidOperationException("Invalid HTTP status line.");
        }

        var response = new WebSocketHandshakeResponse
        {
            HttpVersion = first[0].Replace("HTTP/", string.Empty),
            StatusCode = int.Parse(first[1]),
            ReasonPhrase = string.Join(" ", first, 2, first.Length - 2),
        };

        FillHeaders(lines, 1, response.Headers);
        return response;
    }

    private static string[] SplitLines(string text)
        => text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

    private static void FillHeaders(string[] lines, int startIndex, IDictionary<string, string> headers)
    {
        for (var i = startIndex; i < lines.Length; i++)
        {
            var separatorIndex = lines[i].IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = lines[i].Substring(0, separatorIndex).Trim();
            var value = lines[i].Substring(separatorIndex + 1).Trim();
            headers[name] = value;
        }
    }

    private static void ValidateClientHandshake(WebSocketHandshakeRequest request)
    {
        if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("WebSocket handshake requires GET.");
        }

        ValidateCommonHandshakeHeaders(request.Headers);
    }

    private static void ValidateServerHandshake(WebSocketHandshakeResponse response, string nonce)
    {
        if (response.StatusCode != 101)
        {
            throw new InvalidOperationException("WebSocket server did not switch protocols.");
        }

        ValidateCommonHandshakeHeaders(response.Headers);
        if (!response.Headers.TryGetValue("Sec-WebSocket-Accept", out var accept) ||
            !string.Equals(accept, ComputeAccept(nonce), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid Sec-WebSocket-Accept header.");
        }
    }

    private static void ValidateCommonHandshakeHeaders(IDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Upgrade", out var upgrade) ||
            !string.Equals(upgrade, UpgradeToken, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid Upgrade header.");
        }

        if (!headers.TryGetValue("Connection", out var connection) ||
            connection.IndexOf(ConnectionToken, StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new InvalidOperationException("Invalid Connection header.");
        }

        if (headers.TryGetValue("Sec-WebSocket-Version", out var version) &&
            !string.Equals(version, Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported Sec-WebSocket-Version.");
        }
    }

    private static string CreateClientRequest(string host, string path, string nonce)
        => $"GET {NormalizePath(path)} HTTP/1.1\r\nHost: {host}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {nonce}\r\nSec-WebSocket-Version: {Version}\r\n\r\n";

    private static string CreateServerResponse(string nonce)
        => $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {ComputeAccept(nonce)}\r\n\r\n";

    private static string CreateClientNonce()
    {
        var bytes = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }

        return Convert.ToBase64String(bytes);
    }

    private static string ComputeAccept(string nonce)
    {
        using (var sha1 = SHA1.Create())
        {
            var bytes = Encoding.ASCII.GetBytes(nonce + AcceptSuffix);
            return Convert.ToBase64String(sha1.ComputeHash(bytes));
        }
    }

    private static string NormalizePath(string path)
        => string.IsNullOrWhiteSpace(path) ? "/" : path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
}
