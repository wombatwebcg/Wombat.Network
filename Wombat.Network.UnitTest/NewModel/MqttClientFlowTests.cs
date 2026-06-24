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
        var script = new ScriptedMqttConnection(
            new MqttConnAckPacket(),
            new MqttSubAckPacket(1, new byte[] { 1 }),
            new MqttPingRespPacket(),
            new MqttPubAckPacket(2));
        var clientFactory = new DelegateMqttConnectionFactory(_ => Task.FromResult<IMqttConnection>(script));
        var client = new MqttClient(new MqttClientOptions
        {
            Endpoint = new MqttEndpoint("localhost", 1883),
            ClientId = "client-one",
            KeepAliveSeconds = 12,
        }, clientFactory);

        var connAck = await client.ConnectAsync();
        var subAck = await client.SubscribeAsync("topic/1", MqttQualityOfService.AtLeastOnce);
        await client.PingAsync();
        await client.PublishAsync("topic/1", new byte[] { 1, 2, 3 }, MqttQualityOfService.AtLeastOnce);
        await client.DisconnectAsync();

        connAck.ReasonCode.Should().Be(0);
        subAck.ReasonCodes.Should().ContainSingle().Which.Should().Be(1);
        script.SentPackets.Should().ContainInOrder(
            typeof(MqttConnectPacket),
            typeof(MqttSubscribePacket),
            typeof(MqttPingReqPacket),
            typeof(MqttPublishPacket),
            typeof(MqttDisconnectPacket));
    }

    [Fact]
    public async Task Client_ShouldPublishQoS2_WithPubRecPubRelPubCompFlow()
    {
        var script = new ScriptedMqttConnection(
            new MqttConnAckPacket(),
            new MqttPubRecPacket(1),
            new MqttPubCompPacket(1));
        var clientFactory = new DelegateMqttConnectionFactory(_ => Task.FromResult<IMqttConnection>(script));
        var client = new MqttClient(new MqttClientOptions
        {
            Endpoint = new MqttEndpoint("localhost", 1883),
            ClientId = "client-qos2",
        }, clientFactory);

        await client.ConnectAsync();
        await client.PublishAsync("topic/2", new byte[] { 7, 8, 9 }, MqttQualityOfService.ExactlyOnce);

        script.SentPackets.Should().ContainInOrder(
            typeof(MqttConnectPacket),
            typeof(MqttPublishPacket),
            typeof(MqttPubRelPacket));
    }

    [Fact]
    public async Task Client_ShouldUseV311Flow_WhenConfigured()
    {
        var script = new ScriptedMqttConnection(
            MqttProtocolVersion.V311,
            new MqttConnAckPacket(),
            new MqttSubAckPacket(1, new byte[] { 1 }),
            new MqttPubAckPacket(2));
        var clientFactory = new DelegateMqttConnectionFactory(_ => Task.FromResult<IMqttConnection>(script));
        var client = new MqttClient(new MqttClientOptions
        {
            Endpoint = new MqttEndpoint("localhost", 1883),
            ClientId = "client-v311",
            ProtocolVersion = MqttProtocolVersion.V311,
        }, clientFactory);

        await client.ConnectAsync();
        await client.SubscribeAsync("topic/311", MqttQualityOfService.AtLeastOnce);
        await client.PublishAsync("topic/311", new byte[] { 1, 2, 3 }, MqttQualityOfService.AtLeastOnce);
        await client.DisconnectAsync();

        script.ConnectProtocols.Should().ContainSingle().Which.Should().Be(MqttProtocolVersion.V311);
        script.SentPackets.Should().ContainInOrder(
            typeof(MqttConnectPacket),
            typeof(MqttSubscribePacket),
            typeof(MqttPublishPacket),
            typeof(MqttDisconnectPacket));
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

    private sealed class ScriptedMqttConnection : IMqttConnection
    {
        private readonly MqttPacketCodec _codec = new MqttPacketCodec();
        private readonly System.Collections.Generic.Queue<ReadOnlyMemory<byte>> _responses;
        private readonly MqttProtocolVersion _protocolVersion;

        public ScriptedMqttConnection(params MqttPacket[] responses)
            : this(MqttProtocolVersion.V500, responses)
        {
        }

        public ScriptedMqttConnection(MqttProtocolVersion protocolVersion, params MqttPacket[] responses)
        {
            _protocolVersion = protocolVersion;
            _responses = new System.Collections.Generic.Queue<ReadOnlyMemory<byte>>();
            SentPackets = new System.Collections.Generic.List<Type>();
            ConnectProtocols = new System.Collections.Generic.List<MqttProtocolVersion>();
            foreach (var response in responses)
            {
                _responses.Enqueue(_codec.Encode(response, protocolVersion));
            }
        }

        public System.Collections.Generic.List<Type> SentPackets { get; }

        public System.Collections.Generic.List<MqttProtocolVersion> ConnectProtocols { get; }

        public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
        {
            var decoded = _codec.Decode(packet.Span, _protocolVersion);
            SentPackets.Add(decoded.GetType());
            if (decoded is MqttConnectPacket connect)
            {
                ConnectProtocols.Add(connect.ProtocolVersion);
            }

            return default;
        }

        public ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            if (_responses.Count == 0)
            {
                return new ValueTask<ReadOnlyMemory<byte>?>(result: null);
            }

            return new ValueTask<ReadOnlyMemory<byte>?>(_responses.Dequeue());
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
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
