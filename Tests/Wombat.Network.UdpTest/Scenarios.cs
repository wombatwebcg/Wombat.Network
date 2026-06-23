using System.Security.Cryptography;
using System.Text;
using Wombat.Network.Protocols.Framing;
using Wombat.Network.TestHelper;

namespace Wombat.Network.UdpTest;

internal static class Scenarios
{
    public static readonly Scenario[] All =
    [
        new("EchoDatagram", "无 framing 单包回显，校验内容。", RunEchoDatagramAsync),
        new("FramedPayloads", "启用 LengthField framing，覆盖 0B/小包/大包。", RunFramedPayloadsAsync),
        new("ManySmallDatagrams", "连续发送大量小包，校验顺序和内容。", RunManySmallDatagramsAsync),
        new("LargeDatagramHash", "发送接近 UDP 安全上限的大包，校验长度和哈希。", RunLargeDatagramHashAsync),
    ];

    private static async Task RunEchoDatagramAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var payload = Encoding.UTF8.GetBytes("udp-echo");

        await UdpTestHarness.RunConnectedPairAsync(
            async (serverChannel, token) =>
            {
                var inbound = await serverChannel.ReceiveAsync(token);
                NetworkTestHelper.ValidateText(inbound, "udp-echo", "server recv");
                await serverChannel.SendToAsync(payload, inbound!.Value.RemoteEndPoint, token);
            },
            async (clientChannel, token) =>
            {
                await clientChannel.SendAsync(payload, token);
                var echoed = await clientChannel.ReceiveAsync(token);
                NetworkTestHelper.ValidateText(echoed, "udp-echo", "client recv");
            },
            messagePipe: null,
            cts.Token);
    }

    private static async Task RunFramedPayloadsAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var payloads = new[]
        {
            Array.Empty<byte>(),
            new byte[] { 0x2A },
            Encoding.UTF8.GetBytes("0123456789"),
            NetworkTestHelper.CreateRepeatedPayload(4 * 1024, 0x61),
        };
        var pipe = new LengthFieldMessagePipe(LengthField.FourBytes);

        await UdpTestHarness.RunConnectedPairAsync(
            async (serverChannel, token) =>
            {
                for (var i = 0; i < payloads.Length; i++)
                {
                    var inbound = await serverChannel.ReceiveAsync(token);
                    NetworkTestHelper.ValidatePayload(inbound, payloads[i], $"server framed {i + 1}");
                    await serverChannel.SendToAsync(payloads[i], inbound!.Value.RemoteEndPoint, token);
                }
            },
            async (clientChannel, token) =>
            {
                for (var i = 0; i < payloads.Length; i++)
                {
                    await clientChannel.SendAsync(payloads[i], token);
                    var echoed = await clientChannel.ReceiveAsync(token);
                    NetworkTestHelper.ValidatePayload(echoed, payloads[i], $"client framed {i + 1}");
                }
            },
            pipe,
            cts.Token);

        Console.WriteLine($"      payload sizes: {string.Join(", ", payloads.Select(static x => x.Length))}");
    }

    private static async Task RunManySmallDatagramsAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        const int count = 1_000;
        var payloads = Enumerable.Range(1, count).Select(i => Encoding.UTF8.GetBytes($"udp-msg-{i:000000}")).ToArray();

        await UdpTestHarness.RunConnectedPairAsync(
            async (serverChannel, token) =>
            {
                for (var i = 0; i < payloads.Length; i++)
                {
                    var inbound = await serverChannel.ReceiveAsync(token);
                    NetworkTestHelper.ValidatePayload(inbound, payloads[i], $"server seq {i + 1}");
                    await serverChannel.SendToAsync(payloads[i], inbound!.Value.RemoteEndPoint, token);
                }
            },
            async (clientChannel, token) =>
            {
                for (var i = 0; i < payloads.Length; i++)
                {
                    await clientChannel.SendAsync(payloads[i], token);
                    var echoed = await clientChannel.ReceiveAsync(token);
                    NetworkTestHelper.ValidatePayload(echoed, payloads[i], $"client seq {i + 1}");
                }
            },
            messagePipe: null,
            cts.Token);

        Console.WriteLine($"      datagram count: {count}");
    }

    private static async Task RunLargeDatagramHashAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var payload = NetworkTestHelper.CreateRepeatedPayload(60 * 1024, 0x5A);
        var expectedHash = SHA256.HashData(payload);

        await UdpTestHarness.RunConnectedPairAsync(
            async (serverChannel, token) =>
            {
                var inbound = await serverChannel.ReceiveAsync(token);
                NetworkTestHelper.ValidatePayload(inbound, payload.Length, expectedHash, "server large");
                await serverChannel.SendToAsync(payload, inbound!.Value.RemoteEndPoint, token);
            },
            async (clientChannel, token) =>
            {
                await clientChannel.SendAsync(payload, token);
                var echoed = await clientChannel.ReceiveAsync(token);
                NetworkTestHelper.ValidatePayload(echoed, payload.Length, expectedHash, "client large");
            },
            messagePipe: null,
            cts.Token);

        Console.WriteLine($"      payload bytes: {payload.Length}");
        Console.WriteLine($"      payload sha256: {Convert.ToHexString(expectedHash)}");
    }
}

internal sealed record Scenario(string Name, string Description, Func<Task> RunAsync);
