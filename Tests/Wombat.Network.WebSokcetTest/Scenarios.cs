using System.Security.Cryptography;
using System.Text;
using Wombat.Network.Protocols.WebSocket;
using Wombat.Network.TestHelper;

namespace Wombat.Network.WebSokcetTest;

internal static class Scenarios
{
    public static readonly Scenario[] All =
    [
        new("HandshakeAndText", "完成握手并双向收发文本。", RunHandshakeAndTextAsync),
        new("BinaryPayload", "发送二进制消息，校验长度和哈希。", RunBinaryPayloadAsync),
        new("PingPong", "显式发送 ping/pong，校验消息类型和负载。", RunPingPongAsync),
        new("ReconnectAfterDisconnect", "客户端断开后重新握手并连接同一端口，校验新会话仍可正常收发。", RunReconnectAfterDisconnectAsync),
        new("CloseFrame", "发送 close 帧，校验对端能收到关闭消息。", RunCloseFrameAsync),
    ];

    private static async Task RunHandshakeAndTextAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await WebSocketTestHarness.RunConnectedPairAsync(
            async (serverChannel, request, token) =>
            {
                NetworkTestHelper.Ensure(request.RequestTarget == "/chat", "server handshake path mismatch.");
                var inbound = await serverChannel.ReceiveAsync(token);
                ValidateMessage(inbound, WebSocketMessageType.Text, "ping", "server text");
                await serverChannel.SendTextAsync("pong", token);
            },
            async (clientChannel, token) =>
            {
                await clientChannel.SendTextAsync("ping", token);
                var echoed = await clientChannel.ReceiveAsync(token);
                ValidateMessage(echoed, WebSocketMessageType.Text, "pong", "client text");
            },
            "/chat",
            cts.Token);
    }

    private static async Task RunBinaryPayloadAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var payload = Encoding.UTF8.GetBytes(NetworkTestHelper.CreateLargeNumericString(100_000));
        var expectedHash = SHA256.HashData(payload);

        await WebSocketTestHarness.RunConnectedPairAsync(
            async (serverChannel, _, token) =>
            {
                var inbound = await serverChannel.ReceiveAsync(token);
                ValidateBinary(inbound, payload.Length, expectedHash, "server binary");
                await serverChannel.SendBinaryAsync(payload, token);
            },
            async (clientChannel, token) =>
            {
                await clientChannel.SendBinaryAsync(payload, token);
                var echoed = await clientChannel.ReceiveAsync(token);
                ValidateBinary(echoed, payload.Length, expectedHash, "client binary");
            },
            "/binary",
            cts.Token);

        Console.WriteLine($"      payload bytes: {payload.Length}");
        Console.WriteLine($"      payload sha256: {Convert.ToHexString(expectedHash)}");
    }

    private static async Task RunPingPongAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var pingPayload = Encoding.UTF8.GetBytes("are-you-there");
        var pongPayload = Encoding.UTF8.GetBytes("yes");
        var pongObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await WebSocketTestHarness.RunConnectedPairAsync(
            async (serverChannel, _, token) =>
            {
                var ping = await serverChannel.ReceiveAsync(token);
                ValidateMessage(ping, WebSocketMessageType.Ping, "are-you-there", "server ping");
                await serverChannel.SendPongAsync(pongPayload, token);
                await pongObserved.Task.WaitAsync(token);
            },
            async (clientChannel, token) =>
            {
                await clientChannel.SendPingAsync(pingPayload, token);
                var pong = await clientChannel.ReceiveAsync(token);
                ValidateMessage(pong, WebSocketMessageType.Pong, "yes", "client pong");
                pongObserved.TrySetResult();
            },
            "/ping",
            cts.Token);
    }

    private static async Task RunReconnectAfterDisconnectAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var endPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, NetworkTestHelper.GetAvailablePort());
        using var listener = new Wombat.Network.Transports.Tcp.TcpTransportListener(endPoint);
        await listener.StartAsync(cts.Token);

        var serverTask = Task.Run(async () =>
        {
            for (var session = 1; session <= 2; session++)
            {
                using var accepted = (Wombat.Network.Transports.Tcp.TcpTransportConnection)await listener.AcceptAsync(cts.Token);
                await accepted.StartAsync(cts.Token);
                var request = await WebSocketHandshakeMiddleware.AcceptServerAsync(accepted, cts.Token);
                NetworkTestHelper.Ensure(request.RequestTarget == $"/reconnect/{session}", $"server reconnect {session} handshake path mismatch.");
                var channel = new Wombat.Network.Channels.WebSocketMessageChannel(accepted, isClient: false);

                try
                {
                    var received = await channel.ReceiveAsync(cts.Token);
                    ValidateMessage(received, WebSocketMessageType.Text, $"client-session-{session}", $"server reconnect {session}");
                    await channel.SendTextAsync($"server-session-{session}", cts.Token);
                }
                finally
                {
                    await accepted.CloseAsync(cts.Token);
                }
            }
        }, cts.Token);

        for (var session = 1; session <= 2; session++)
        {
            using var client = await Wombat.Network.Transports.Tcp.TcpTransportConnection.ConnectAsync(endPoint, cancellationToken: cts.Token);
            await client.StartAsync(cts.Token);
            await WebSocketHandshakeMiddleware.AcceptClientAsync(client, $"127.0.0.1:{endPoint.Port}", $"/reconnect/{session}", cts.Token);
            var channel = new Wombat.Network.Channels.WebSocketMessageChannel(client, isClient: true);

            try
            {
                await channel.SendTextAsync($"client-session-{session}", cts.Token);
                var received = await channel.ReceiveAsync(cts.Token);
                ValidateMessage(received, WebSocketMessageType.Text, $"server-session-{session}", $"client reconnect {session}");
                await channel.SendCloseAsync(cancellationToken: cts.Token);
            }
            finally
            {
                await client.CloseAsync(cts.Token);
            }
        }

        await serverTask;
        await listener.CloseAsync(cts.Token);
        Console.WriteLine("      reconnect sessions: 2");
    }

    private static async Task RunCloseFrameAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var closeObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await WebSocketTestHarness.RunConnectedPairAsync(
            async (serverChannel, _, token) =>
            {
                await serverChannel.SendCloseAsync(WebSocketCloseCode.NormalClosure, "bye", token);
                await closeObserved.Task.WaitAsync(token);
            },
            async (clientChannel, token) =>
            {
                var close = await clientChannel.ReceiveAsync(token);
                NetworkTestHelper.Ensure(close.HasValue, "client close should receive one message.");
                NetworkTestHelper.Ensure(close.Value.MessageType == WebSocketMessageType.Close, "client close message type mismatch.");
                closeObserved.TrySetResult();
            },
            "/close",
            cts.Token);
    }

    private static void ValidateMessage(WebSocketReceivedMessage? message, WebSocketMessageType expectedType, string expectedText, string operation)
    {
        NetworkTestHelper.Ensure(message.HasValue, $"{operation} should receive one message.");
        var actual = message!.Value;
        NetworkTestHelper.Ensure(actual.MessageType == expectedType, $"{operation} message type mismatch.");
        var text = Encoding.UTF8.GetString(NetworkTestHelper.ToArray(actual.Payload));
        NetworkTestHelper.Ensure(text == expectedText, $"{operation} payload mismatch.");
    }

    private static void ValidateBinary(WebSocketReceivedMessage? message, int expectedLength, byte[] expectedHash, string operation)
    {
        NetworkTestHelper.Ensure(message.HasValue, $"{operation} should receive one message.");
        var actual = message!.Value;
        NetworkTestHelper.Ensure(actual.MessageType == WebSocketMessageType.Binary, $"{operation} message type mismatch.");
        NetworkTestHelper.Ensure(actual.Payload.Length == expectedLength, $"{operation} length mismatch.");
        var payload = NetworkTestHelper.ToArray(actual.Payload);
        var actualHash = SHA256.HashData(payload);
        NetworkTestHelper.Ensure(actualHash.SequenceEqual(expectedHash), $"{operation} payload mismatch.");
    }
}

internal sealed record Scenario(string Name, string Description, Func<Task> RunAsync);
