using FluentAssertions;
using System.Net;
using System.Threading.Tasks;
using Wombat.Network.Channels;
using Wombat.Network.Protocols.Framing;
using Wombat.Network.Transports.Udp;
using Wombat.Network.UnitTest.TestHelpers;
using Xunit;

namespace Wombat.Network.UnitTest.NewModel;

public class UdpTransportTests : NetworkTestBase
{
    [Fact]
    public async Task DatagramMessageChannel_WithoutFraming_ShouldEchoDatagram()
    {
        var serverPort = GetAvailablePort();
        using var serverTransport = new UdpDatagramTransport(new IPEndPoint(IPAddress.Loopback, serverPort));
        using var clientTransport = new UdpDatagramTransport(defaultRemoteEndPoint: new IPEndPoint(IPAddress.Loopback, serverPort));

        await serverTransport.StartAsync(_testCancellationTokenSource.Token);
        await clientTransport.StartAsync(_testCancellationTokenSource.Token);

        var serverChannel = new DatagramMessageChannel(serverTransport);
        var clientChannel = new DatagramMessageChannel(clientTransport, new IPEndPoint(IPAddress.Loopback, serverPort));
        var outbound = GenerateTestData(96);

        var serverTask = Task.Run(async () =>
        {
            var inbound = await serverChannel.ReceiveAsync(_testCancellationTokenSource.Token);
            inbound.HasValue.Should().BeTrue();
            await serverChannel.SendToAsync(ToArray(inbound.Value.Payload), inbound.Value.RemoteEndPoint, _testCancellationTokenSource.Token);
        });

        await clientChannel.SendAsync(outbound, _testCancellationTokenSource.Token);
        var echoed = await clientChannel.ReceiveAsync(_testCancellationTokenSource.Token);

        echoed.HasValue.Should().BeTrue();
        ToArray(echoed.Value.Payload).Should().Equal(outbound);
        await serverTask;
    }

    [Fact]
    public async Task DatagramMessageChannel_WithFraming_ShouldDecodeSingleMessageFromDatagram()
    {
        var serverPort = GetAvailablePort();
        var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, serverPort);
        using var serverTransport = new UdpDatagramTransport(new IPEndPoint(IPAddress.Loopback, serverPort));
        using var clientTransport = new UdpDatagramTransport(defaultRemoteEndPoint: remoteEndPoint);
        var pipe = new LengthFieldMessagePipe(LengthField.FourBytes);

        await serverTransport.StartAsync(_testCancellationTokenSource.Token);
        await clientTransport.StartAsync(_testCancellationTokenSource.Token);

        var serverChannel = new DatagramMessageChannel(serverTransport, messagePipe: pipe);
        var clientChannel = new DatagramMessageChannel(clientTransport, remoteEndPoint, pipe);
        var outbound = GenerateTestData(64);

        await clientChannel.SendAsync(outbound, _testCancellationTokenSource.Token);
        var inbound = await serverChannel.ReceiveAsync(_testCancellationTokenSource.Token);

        inbound.HasValue.Should().BeTrue();
        ToArray(inbound.Value.Payload).Should().Equal(outbound);
    }

    private static byte[] ToArray(in System.Buffers.ReadOnlySequence<byte> sequence)
    {
        var buffer = new byte[sequence.Length];
        var offset = 0;
        foreach (var segment in sequence)
        {
            segment.Span.CopyTo(buffer.AsSpan(offset));
            offset += segment.Length;
        }

        return buffer;
    }
}
