using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Wombat.Network.TestHelper;
using Wombat.Network.Transports.Abstractions;
using Wombat.Network.Transports.Tcp;

namespace Wombat.Network.TcpTest;

internal static class Scenarios
{
    public static readonly Scenario[] All =
    [
        new("LargePayloadDuplex", "大字符串双向各发送接收 2 次，校验长度和哈希。", RunLargePayloadDuplexAsync),
        new("ManySmallMessages", "双向连续发送大量小消息，校验顺序和内容。", RunManySmallMessagesAsync),
        new("MixedPayloadSizes", "混合 0B/1B/10B/1KB/1MB/大消息，校验边界长度。", RunMixedPayloadSizesAsync),
        new("ReconnectAfterDisconnect", "客户端断开后重新连接同一监听端口，校验新连接仍可正常收发。", RunReconnectAfterDisconnectAsync),
        new("TimeoutAndCancellation", "验证 Accept/Receive 在取消时能正常退出。", RunTimeoutAndCancellationAsync),
        new("ParallelClients", "多个客户端并发连接并双向收发，校验隔离性。", RunParallelClientsAsync),
    ];

    private static async Task RunLargePayloadDuplexAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var payload = Encoding.UTF8.GetBytes(NetworkTestHelper.CreateLargeNumericString(10_000_000));
        var expectedHash = SHA256.HashData(payload);

        await TcpTestHarness.RunConnectedPairAsync(
            async (serverChannel, token) =>
            {
                for (var round = 1; round <= 2; round++)
                {
                    await serverChannel.SendAsync(payload, token);
                    var received = await serverChannel.ReceiveAsync(token);
                    NetworkTestHelper.ValidatePayload(received, payload.Length, expectedHash, $"server round {round}");
                }
            },
            async (clientChannel, token) =>
            {
                for (var round = 1; round <= 2; round++)
                {
                    await clientChannel.SendAsync(payload, token);
                    var received = await clientChannel.ReceiveAsync(token);
                    NetworkTestHelper.ValidatePayload(received, payload.Length, expectedHash, $"client round {round}");
                }
            },
            cts.Token);

        Console.WriteLine($"      payload bytes: {payload.Length}");
        Console.WriteLine($"      payload sha256: {Convert.ToHexString(expectedHash)}");
    }

    private static async Task RunManySmallMessagesAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        const int count = 2_000;
        var clientMessages = Enumerable.Range(1, count).Select(i => $"client-msg-{i:000000}").ToArray();
        var serverMessages = Enumerable.Range(1, count).Select(i => $"server-msg-{i:000000}").ToArray();

        await TcpTestHarness.RunConnectedPairAsync(
            async (serverChannel, token) =>
            {
                for (var i = 0; i < serverMessages.Length; i++)
                {
                    var received = await serverChannel.ReceiveAsync(token);
                    NetworkTestHelper.ValidateText(received, clientMessages[i], $"server recv {i + 1}");
                    await serverChannel.SendAsync(Encoding.UTF8.GetBytes(serverMessages[i]), token);
                }

                var closed = await serverChannel.ReceiveAsync(token);
                NetworkTestHelper.Ensure(!closed.HasValue, "server should observe remote close after final small-message echo.");
            },
            async (clientChannel, token) =>
            {
                for (var i = 0; i < clientMessages.Length; i++)
                {
                    await clientChannel.SendAsync(Encoding.UTF8.GetBytes(clientMessages[i]), token);
                    var received = await clientChannel.ReceiveAsync(token);
                    NetworkTestHelper.ValidateText(received, serverMessages[i], $"client recv {i + 1}");
                }
            },
            cts.Token);

        Console.WriteLine($"      message count per side: {count}");
    }

    private static async Task RunMixedPayloadSizesAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var payloads = new[]
        {
            Array.Empty<byte>(),
            new byte[] { 0x2A },
            Encoding.UTF8.GetBytes("0123456789"),
            NetworkTestHelper.CreateRepeatedPayload(1024, 0x61),
            NetworkTestHelper.CreateRepeatedPayload(1024 * 1024, 0x62),
            Encoding.UTF8.GetBytes(NetworkTestHelper.CreateLargeNumericString(200_000)),
        };

        await TcpTestHarness.RunConnectedPairAsync(
            async (serverChannel, token) =>
            {
                for (var i = 0; i < payloads.Length; i++)
                {
                    var sent = payloads[i];
                    await serverChannel.SendAsync(sent, token);
                    var received = await serverChannel.ReceiveAsync(token);
                    NetworkTestHelper.ValidatePayload(received, sent, $"server mixed {i + 1}");
                }
            },
            async (clientChannel, token) =>
            {
                for (var i = 0; i < payloads.Length; i++)
                {
                    var sent = payloads[i];
                    await clientChannel.SendAsync(sent, token);
                    var received = await clientChannel.ReceiveAsync(token);
                    NetworkTestHelper.ValidatePayload(received, sent, $"client mixed {i + 1}");
                }
            },
            cts.Token);

        Console.WriteLine($"      payload sizes: {string.Join(", ", payloads.Select(static x => x.Length))}");
    }

    private static async Task RunReconnectAfterDisconnectAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var endPoint = new IPEndPoint(IPAddress.Loopback, NetworkTestHelper.GetAvailablePort());
        using var listener = new TcpTransportListener(endPoint);
        await listener.StartAsync(cts.Token);

        var serverTask = Task.Run(async () =>
        {
            for (var session = 1; session <= 2; session++)
            {
                using var accepted = (TcpTransportConnection)await listener.AcceptAsync(cts.Token);
                await accepted.StartAsync(cts.Token);
                var channel = TcpTestHarness.CreateChannel(accepted);

                try
                {
                    var received = await channel.ReceiveAsync(cts.Token);
                    NetworkTestHelper.ValidateText(received, $"client-session-{session}", $"server reconnect {session}");
                    await channel.SendAsync(Encoding.UTF8.GetBytes($"server-session-{session}"), cts.Token);

                    var closed = await channel.ReceiveAsync(cts.Token);
                    NetworkTestHelper.Ensure(!closed.HasValue, $"server reconnect {session} should observe remote close.");
                }
                finally
                {
                    await channel.CloseAsync(cts.Token);
                }
            }
        }, cts.Token);

        for (var session = 1; session <= 2; session++)
        {
            using var client = await TcpTransportConnection.ConnectAsync(endPoint, cancellationToken: cts.Token);
            await client.StartAsync(cts.Token);
            var channel = TcpTestHarness.CreateChannel(client);

            try
            {
                await channel.SendAsync(Encoding.UTF8.GetBytes($"client-session-{session}"), cts.Token);
                var received = await channel.ReceiveAsync(cts.Token);
                NetworkTestHelper.ValidateText(received, $"server-session-{session}", $"client reconnect {session}");
            }
            finally
            {
                await channel.CloseAsync(cts.Token);
            }
        }

        await serverTask;
        await listener.CloseAsync(cts.Token);
        Console.WriteLine("      reconnect sessions: 2");
    }

    private static async Task RunTimeoutAndCancellationAsync()
    {
        var acceptPort = NetworkTestHelper.GetAvailablePort();
        using (var acceptListener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, acceptPort)))
        {
            await acceptListener.StartAsync();
            var acceptTask = acceptListener.AcceptAsync();
            await Task.Delay(300);
            await acceptListener.CloseAsync();

            try
            {
                await acceptTask;
                throw new InvalidOperationException("AcceptAsync should have been interrupted by listener close.");
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
            catch (InvalidOperationException ex) when (ex.Message == "Listener not started.")
            {
            }
        }

        var receivePort = NetworkTestHelper.GetAvailablePort();
        using var listener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, receivePort));
        await listener.StartAsync();

        var serverTask = Task.Run(async () =>
        {
            var accepted = await listener.AcceptAsync();
            await accepted.StartAsync();
            var channel = TcpTestHarness.CreateChannel(accepted);
            using var receiveCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            try
            {
                await channel.ReceiveAsync(receiveCts.Token);
                throw new InvalidOperationException("ReceiveAsync should have been cancelled.");
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                await channel.CloseAsync();
            }
        });

        using var client = await TcpTransportConnection.ConnectAsync(new IPEndPoint(IPAddress.Loopback, receivePort));
        await client.StartAsync();
        await Task.Delay(600);
        await client.CloseAsync();
        await serverTask;
        await listener.CloseAsync();
    }

    private static async Task RunParallelClientsAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        const int clientCount = 12;
        const int rounds = 2;
        var endPoint = new IPEndPoint(IPAddress.Loopback, NetworkTestHelper.GetAvailablePort());

        using var listener = new TcpTransportListener(endPoint);
        await listener.StartAsync(cts.Token);

        var serverTask = Task.Run(async () =>
        {
            var handlers = new List<Task>(clientCount);
            for (var i = 0; i < clientCount; i++)
            {
                var accepted = await listener.AcceptAsync(cts.Token);
                handlers.Add(HandleParallelClientAsync(accepted, cts.Token));
            }

            await Task.WhenAll(handlers);
        }, cts.Token);

        var clientTasks = Enumerable.Range(1, clientCount)
            .Select(clientId => Task.Run(async () =>
            {
                using var client = await TcpTransportConnection.ConnectAsync(endPoint, cancellationToken: cts.Token);
                await client.StartAsync(cts.Token);
                var channel = TcpTestHarness.CreateChannel(client);

                for (var round = 1; round <= rounds; round++)
                {
                    var text = $"client-{clientId}-round-{round}";
                    await channel.SendAsync(Encoding.UTF8.GetBytes(text), cts.Token);
                    var received = await channel.ReceiveAsync(cts.Token);
                    NetworkTestHelper.ValidateText(received, $"echo:{text}", $"parallel client {clientId} round {round}");
                }

                await channel.CloseAsync(cts.Token);
            }, cts.Token))
            .ToArray();

        await Task.WhenAll(clientTasks);
        await serverTask;
        await listener.CloseAsync(cts.Token);

        Console.WriteLine($"      clients: {clientCount}, rounds per client: {rounds}");
    }

    private static async Task HandleParallelClientAsync(ITransportConnection connection, CancellationToken cancellationToken)
    {
        await connection.StartAsync(cancellationToken);
        var channel = TcpTestHarness.CreateChannel(connection);

        try
        {
            for (var round = 1; round <= 2; round++)
            {
                var received = await channel.ReceiveAsync(cancellationToken);
                NetworkTestHelper.Ensure(received.HasValue, $"parallel server round {round} should receive one message.");
                var text = Encoding.UTF8.GetString(NetworkTestHelper.ToArray(received!.Value.Payload));
                await channel.SendAsync(Encoding.UTF8.GetBytes($"echo:{text}"), cancellationToken);
            }

            var closed = await channel.ReceiveAsync(cancellationToken);
            NetworkTestHelper.Ensure(!closed.HasValue, "parallel server should observe remote close after final echo.");
        }
        finally
        {
            await channel.CloseAsync();
        }
    }
}

internal sealed record Scenario(string Name, string Description, Func<Task> RunAsync);
