using FluentAssertions;
using System;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Mqtt.Abstractions;
using Wombat.Network.Mqtt.Client;
using Wombat.Network.Mqtt.Protocol;
using Wombat.Network.Mqtt.Transport;
using Wombat.Network.Transports.Abstractions;
using Xunit;

namespace Wombat.Network.UnitTest.NewModel;

public class MqttClientFlowTests
{
    [Fact]
    public async Task Client_OverTcpConnection_ShouldConnectSubscribePingPublishAndDisconnect()
    {
        var pair = InMemoryTransportConnection.CreatePair();
        var server = new MqttPipeConnection(pair.Server);
        var clientFactory = new DelegateMqttConnectionFactory(_ => Task.FromResult<IMqttConnection>(new MqttPipeConnection(pair.Client)));
        var client = new MqttClient(new MqttClientOptions
        {
            Endpoint = new MqttEndpoint("localhost", 1883),
            ClientId = "client-one",
            KeepAliveSeconds = 12,
        }, clientFactory);
        var serverLoop = RunServerLoopAsync(server);

        var connAck = await client.ConnectAsync();
        var subAck = await client.SubscribeAsync("topic/1", MqttQualityOfService.AtLeastOnce);
        await client.PingAsync();
        await client.PublishAsync("topic/1", new byte[] { 1, 2, 3 }, MqttQualityOfService.AtLeastOnce);
        await client.DisconnectAsync();
        await serverLoop;

        connAck.ReasonCode.Should().Be(0);
        subAck.ReasonCodes.Should().ContainSingle().Which.Should().Be(1);
    }

    [Fact]
    public async Task WebSocketConnection_ShouldHandshakeAndExchangeMqttPacket()
    {
        var pair = InMemoryTransportConnection.CreatePair();
        var serverTask = MqttWebSocketConnection.CreateServerAsync(pair.Server);
        var clientTask = MqttWebSocketConnection.CreateClientAsync(pair.Client, "localhost", "/mqtt");

        await Task.WhenAll(serverTask, clientTask);

        var server = await serverTask;
        var client = await clientTask;
        var codec = new MqttPacketCodec();
        var packet = codec.Encode(new MqttConnectPacket("ws-client"));

        await client.SendAsync(packet);
        var received = await server.ReceiveAsync();

        received.HasValue.Should().BeTrue();
        codec.Decode(received.Value.Span).Should().BeOfType<MqttConnectPacket>();
    }

    private static async Task RunServerLoopAsync(IMqttConnection connection)
    {
        var codec = new MqttPacketCodec();

        while (true)
        {
            var payload = await connection.ReceiveAsync();
            if (!payload.HasValue)
            {
                return;
            }

            var packet = codec.Decode(payload.Value.Span);
            switch (packet)
            {
                case MqttConnectPacket _:
                    await connection.SendAsync(codec.Encode(new MqttConnAckPacket()));
                    break;
                case MqttSubscribePacket subscribe:
                    await connection.SendAsync(codec.Encode(new MqttSubAckPacket(subscribe.PacketIdentifier, new byte[] { 1 })));
                    break;
                case MqttPingReqPacket _:
                    await connection.SendAsync(codec.Encode(new MqttPingRespPacket()));
                    break;
                case MqttPublishPacket publish:
                    await connection.SendAsync(codec.Encode(new MqttPubAckPacket(publish.PacketIdentifier)));
                    break;
                case MqttDisconnectPacket _:
                    await connection.CloseAsync();
                    return;
                default:
                    throw new InvalidOperationException("Unexpected packet: " + packet.GetType().Name);
            }
        }
    }

    private sealed class DelegateMqttConnectionFactory : IMqttConnectionFactory
    {
        private readonly Func<MqttEndpoint, Task<IMqttConnection>> _connectAsync;

        public DelegateMqttConnectionFactory(Func<MqttEndpoint, Task<IMqttConnection>> connectAsync)
        {
            _connectAsync = connectAsync;
        }

        public Task<IMqttConnection> ConnectAsync(MqttEndpoint endpoint, CancellationToken cancellationToken = default)
            => _connectAsync(endpoint);
    }

    private sealed class InMemoryTransportConnection : ITransportConnection
    {
        private readonly DuplexPipe _transport;

        private InMemoryTransportConnection(string id, PipeReader input, PipeWriter output)
        {
            Id = id;
            _transport = new DuplexPipe(input, output);
            LocalEndPoint = new DnsEndPoint("localhost", 0);
            RemoteEndPoint = new DnsEndPoint("localhost", 0);
        }

        public string Id { get; }

        public EndPoint LocalEndPoint { get; }

        public EndPoint RemoteEndPoint { get; }

        public IDuplexPipe Transport => _transport;

        public Task StartAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            try { _transport.Input.Complete(); } catch { }
            try { _transport.Output.Complete(); } catch { }
            return Task.CompletedTask;
        }

        public static ConnectionPair CreatePair()
        {
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();

            return new ConnectionPair(
                new InMemoryTransportConnection("client", serverToClient.Reader, clientToServer.Writer),
                new InMemoryTransportConnection("server", clientToServer.Reader, serverToClient.Writer));
        }
    }

    private sealed class DuplexPipe : IDuplexPipe
    {
        public DuplexPipe(PipeReader input, PipeWriter output)
        {
            Input = input;
            Output = output;
        }

        public PipeReader Input { get; }

        public PipeWriter Output { get; }
    }

    private sealed class ConnectionPair
    {
        public ConnectionPair(ITransportConnection client, ITransportConnection server)
        {
            Client = client;
            Server = server;
        }

        public ITransportConnection Client { get; }

        public ITransportConnection Server { get; }
    }
}
