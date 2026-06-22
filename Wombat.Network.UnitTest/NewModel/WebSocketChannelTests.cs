using FluentAssertions;
using System;
using System.Buffers;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Wombat.Network.Channels;
using Wombat.Network.Protocols.WebSocket;
using Wombat.Network.Transports.Tcp;
using Wombat.Network.UnitTest.TestHelpers;
using Xunit;

namespace Wombat.Network.UnitTest.NewModel;

public class WebSocketChannelTests : NetworkTestBase
{
    [Fact]
    public async Task WebSocketMessageChannel_ShouldHandshakeAndExchangeText()
    {
        var port = GetAvailablePort();
        using var listener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, port));
        await listener.StartAsync(_testCancellationTokenSource.Token);

        var serverTask = Task.Run(async () =>
        {
            using var accepted = (TcpTransportConnection)await listener.AcceptAsync(_testCancellationTokenSource.Token);
            await accepted.StartAsync(_testCancellationTokenSource.Token);
            var request = await WebSocketHandshakeMiddleware.AcceptServerAsync(accepted, _testCancellationTokenSource.Token);
            request.RequestTarget.Should().Be("/chat");

            var serverChannel = new WebSocketMessageChannel(accepted, isClient: false);
            var inbound = await serverChannel.ReceiveAsync(_testCancellationTokenSource.Token);
            inbound.HasValue.Should().BeTrue();
            inbound.Value.MessageType.Should().Be(WebSocketMessageType.Text);
            Encoding.UTF8.GetString(ToArray(inbound.Value.Payload)).Should().Be("ping");

            await serverChannel.SendTextAsync("pong", _testCancellationTokenSource.Token);
            await serverChannel.CloseAsync(_testCancellationTokenSource.Token);
        });

        using var client = await TcpTransportConnection.ConnectAsync(
            new IPEndPoint(IPAddress.Loopback, port),
            cancellationToken: _testCancellationTokenSource.Token);
        await client.StartAsync(_testCancellationTokenSource.Token);
        await WebSocketHandshakeMiddleware.AcceptClientAsync(client, $"127.0.0.1:{port}", "/chat", _testCancellationTokenSource.Token);

        var clientChannel = new WebSocketMessageChannel(client, isClient: true);
        await clientChannel.SendTextAsync("ping", _testCancellationTokenSource.Token);
        var echoed = await clientChannel.ReceiveAsync(_testCancellationTokenSource.Token);

        echoed.HasValue.Should().BeTrue();
        echoed.Value.MessageType.Should().Be(WebSocketMessageType.Text);
        Encoding.UTF8.GetString(ToArray(echoed.Value.Payload)).Should().Be("pong");

        await serverTask;
    }

    private static byte[] ToArray(in ReadOnlySequence<byte> sequence)
    {
        var buffer = new byte[sequence.Length];
        sequence.CopyTo(buffer);
        return buffer;
    }
}
