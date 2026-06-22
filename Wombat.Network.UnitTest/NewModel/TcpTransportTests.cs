using FluentAssertions;
using System;
using System.Buffers;
using System.Net;
using System.Threading.Tasks;
using Wombat.Network.Channels;
using Wombat.Network.Protocols.Framing;
using Wombat.Network.Transports.Tcp;
using Wombat.Network.UnitTest.TestHelpers;
using Xunit;

namespace Wombat.Network.UnitTest.NewModel;

public class TcpTransportTests : NetworkTestBase
{
    [Fact]
    public async Task StreamMessageChannel_WithTcpTransport_ShouldEcho()
    {
        var port = GetAvailablePort();
        var listener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, port));
        var messagePipe = new LengthFieldMessagePipe(LengthField.FourBytes);

        await listener.StartAsync(_testCancellationTokenSource.Token);

        var serverTask = Task.Run(async () =>
        {
            var accepted = await listener.AcceptAsync(_testCancellationTokenSource.Token);
            await accepted.StartAsync(_testCancellationTokenSource.Token);

            var serverChannel = new StreamMessageChannel(accepted, messagePipe);
            var received = await serverChannel.ReceiveAsync(_testCancellationTokenSource.Token);
            received.HasValue.Should().BeTrue();

            var payload = ToArray(received.Value.Payload);
            await serverChannel.SendAsync(payload, _testCancellationTokenSource.Token);
            await serverChannel.CloseAsync(_testCancellationTokenSource.Token);
        });

        var client = await TcpTransportConnection.ConnectAsync(
            new IPEndPoint(IPAddress.Loopback, port),
            cancellationToken: _testCancellationTokenSource.Token);
        await client.StartAsync(_testCancellationTokenSource.Token);

        var clientChannel = new StreamMessageChannel(client, messagePipe);
        var outbound = GenerateTestData(128);

        await clientChannel.SendAsync(outbound, _testCancellationTokenSource.Token);
        var inbound = await clientChannel.ReceiveAsync(_testCancellationTokenSource.Token);

        inbound.HasValue.Should().BeTrue();
        ToArray(inbound.Value.Payload).Should().Equal(outbound);

        await clientChannel.CloseAsync(_testCancellationTokenSource.Token);
        await listener.CloseAsync(_testCancellationTokenSource.Token);
        await serverTask;
    }

    private static byte[] ToArray(in ReadOnlySequence<byte> sequence)
    {
        var buffer = new byte[sequence.Length];
        sequence.CopyTo(buffer);
        return buffer;
    }
}
