using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wombat.Network.Mqtt.Abstractions;
using Wombat.Network.Mqtt.Broker;
using Wombat.Network.Mqtt.Client;
using Wombat.Network.Mqtt.Protocol;
using Wombat.Network.UnitTest.TestHelpers;
using Xunit;

namespace Wombat.Network.UnitTest.NewModel;

public class MqttBrokerTests : NetworkTestBase
{
    [Fact]
    public async Task Broker_ShouldRouteQoS1RetainAndWildcardSubscriptions()
    {
        var broker = new MqttBroker();
        var publisher = new QueueMqttConnection(
            new MqttConnectPacket("publisher"),
            new MqttPublishPacket("room/a/temp", Encoding.UTF8.GetBytes("42"), MqttQualityOfService.AtLeastOnce, 7),
            new MqttDisconnectPacket());
        var subscriber = QueueMqttConnection.WaitForClose(
            new MqttConnectPacket("subscriber"),
            new MqttSubscribePacket(1, new[] { new MqttSubscription("room/+/temp", MqttQualityOfService.AtLeastOnce) }));

        var subscriberTask = broker.RunConnectionAsync(subscriber);
        await Task.Delay(10);
        await broker.RunConnectionAsync(publisher);
        await subscriber.CloseAsync();
        await subscriberTask;

        subscriber.SentPackets.OfType<MqttConnAckPacket>().Should().ContainSingle();
        subscriber.SentPackets.OfType<MqttSubAckPacket>().Should().ContainSingle();
        subscriber.SentPackets.OfType<MqttPublishPacket>().Should().ContainSingle(x =>
            x.Topic == "room/a/temp" &&
            x.QualityOfService == MqttQualityOfService.AtLeastOnce &&
            Encoding.UTF8.GetString(x.Payload.ToArray()) == "42");

        var retainedPublisher = new QueueMqttConnection(
            new MqttConnectPacket("retained-publisher"),
            new MqttPublishPacket("room/a/temp", Encoding.UTF8.GetBytes("retained"), retain: true),
            new MqttDisconnectPacket());
        await broker.RunConnectionAsync(retainedPublisher);

        var lateSubscriber = new QueueMqttConnection(
            new MqttConnectPacket("late"),
            new MqttSubscribePacket(2, new[] { new MqttSubscription("room/#") }),
            new MqttDisconnectPacket());

        await broker.RunConnectionAsync(lateSubscriber);

        lateSubscriber.SentPackets.OfType<MqttPublishPacket>().Should().Contain(x =>
            x.Topic == "room/a/temp" &&
            Encoding.UTF8.GetString(x.Payload.ToArray()) == "retained");
    }

    [Fact]
    public async Task Broker_ShouldPublishWill_WhenClientDrops()
    {
        var broker = new MqttBroker();
        var watcher = QueueMqttConnection.WaitForClose(
            new MqttConnectPacket("watcher"),
            new MqttSubscribePacket(1, new[] { new MqttSubscription("client/status") }));
        var dropped = QueueMqttConnection.CloseWithoutDisconnect(
            new MqttConnectPacket("dropped", willMessage: new MqttPublishPacket("client/status", Encoding.UTF8.GetBytes("offline"))));

        var watcherTask = broker.RunConnectionAsync(watcher);
        var droppedTask = broker.RunConnectionAsync(dropped);

        await droppedTask;
        await watcher.CloseAsync();
        await watcherTask;

        watcher.SentPackets.OfType<MqttPublishPacket>().Should().ContainSingle(x =>
            x.Topic == "client/status" &&
            Encoding.UTF8.GetString(x.Payload.ToArray()) == "offline");
    }

    [Fact]
    public async Task Broker_StartAsync_ShouldAcceptTcpClients()
    {
        var port = GetAvailablePort();
        var broker = new MqttBroker(new MqttBrokerOptions().ListenTcp(port));
        await broker.StartAsync(_testCancellationTokenSource.Token);

        try
        {
            var subscriber = new MqttClient(new MqttClientOptions
            {
                ClientId = "tcp-sub",
                Endpoint = new MqttEndpoint("127.0.0.1", port, MqttTransportScheme.Tcp)
            });
            var publisher = new MqttClient(new MqttClientOptions
            {
                ClientId = "tcp-pub",
                Endpoint = new MqttEndpoint("127.0.0.1", port, MqttTransportScheme.Tcp)
            });

            await subscriber.ConnectAsync(_testCancellationTokenSource.Token);
            await subscriber.SubscribeAsync("room/#", cancellationToken: _testCancellationTokenSource.Token);
            await publisher.ConnectAsync(_testCancellationTokenSource.Token);
            await publisher.PublishAsync("room/demo", Encoding.UTF8.GetBytes("tcp-ok"), MqttQualityOfService.AtLeastOnce, cancellationToken: _testCancellationTokenSource.Token);

            var inbound = await subscriber.ReceiveAsync(_testCancellationTokenSource.Token);
            var publish = inbound.Should().BeOfType<MqttPublishPacket>().Subject;
            publish.Topic.Should().Be("room/demo");
            Encoding.UTF8.GetString(publish.Payload.ToArray()).Should().Be("tcp-ok");

            await publisher.DisconnectAsync(_testCancellationTokenSource.Token);
            await subscriber.DisconnectAsync(_testCancellationTokenSource.Token);
        }
        finally
        {
            await broker.StopAsync(_testCancellationTokenSource.Token);
        }
    }

    [Fact]
    public async Task Broker_StartAsync_ShouldAcceptWebSocketClients()
    {
        var port = GetAvailablePort();
        var broker = new MqttBroker(new MqttBrokerOptions().ListenWebSocket(port, "/mqtt"));
        await broker.StartAsync(_testCancellationTokenSource.Token);

        try
        {
            var subscriber = new MqttClient(new MqttClientOptions
            {
                ClientId = "ws-sub",
                Endpoint = new MqttEndpoint("127.0.0.1", port, MqttTransportScheme.WebSocket, "/mqtt")
            });
            var publisher = new MqttClient(new MqttClientOptions
            {
                ClientId = "ws-pub",
                Endpoint = new MqttEndpoint("127.0.0.1", port, MqttTransportScheme.WebSocket, "/mqtt")
            });

            await subscriber.ConnectAsync(_testCancellationTokenSource.Token);
            await subscriber.SubscribeAsync("ws/#", cancellationToken: _testCancellationTokenSource.Token);
            await publisher.ConnectAsync(_testCancellationTokenSource.Token);
            await publisher.PublishAsync("ws/demo", Encoding.UTF8.GetBytes("ws-ok"), cancellationToken: _testCancellationTokenSource.Token);

            var inbound = await subscriber.ReceiveAsync(_testCancellationTokenSource.Token);
            var publish = inbound.Should().BeOfType<MqttPublishPacket>().Subject;
            publish.Topic.Should().Be("ws/demo");
            Encoding.UTF8.GetString(publish.Payload.ToArray()).Should().Be("ws-ok");

            await publisher.DisconnectAsync(_testCancellationTokenSource.Token);
            await subscriber.DisconnectAsync(_testCancellationTokenSource.Token);
        }
        finally
        {
            await broker.StopAsync(_testCancellationTokenSource.Token);
        }
    }

    private sealed class QueueMqttConnection : IMqttConnection
    {
        private readonly Queue<ReadOnlyMemory<byte>> _incoming;
        private readonly MqttPacketCodec _codec = new MqttPacketCodec();
        private readonly bool _waitForClose;
        private readonly bool _closeWithoutDisconnect;
        private bool _closed;

        public QueueMqttConnection(params MqttPacket[] packets)
            : this(false, false, packets)
        {
        }

        public static QueueMqttConnection CloseWithoutDisconnect(params MqttPacket[] packets)
            => new QueueMqttConnection(false, true, packets);

        public static QueueMqttConnection WaitForClose(params MqttPacket[] packets)
            => new QueueMqttConnection(true, false, packets);

        public QueueMqttConnection(bool waitForClose = false, bool closeWithoutDisconnect = false, params MqttPacket[] packets)
        {
            _incoming = new Queue<ReadOnlyMemory<byte>>(packets.Select(x => (ReadOnlyMemory<byte>)_codec.Encode(x)));
            _waitForClose = waitForClose;
            _closeWithoutDisconnect = closeWithoutDisconnect;
            SentPackets = new System.Collections.Concurrent.ConcurrentQueue<MqttPacket>();
        }

        public System.Collections.Concurrent.ConcurrentQueue<MqttPacket> SentPackets { get; }

        public ValueTask SendAsync(ReadOnlyMemory<byte> packet, System.Threading.CancellationToken cancellationToken = default)
        {
            SentPackets.Enqueue(_codec.Decode(packet.Span));
            return default;
        }

        public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            if (_incoming.Count > 0)
            {
                var next = _incoming.Dequeue();
                if (_closeWithoutDisconnect && _incoming.Count == 0)
                {
                    _closed = true;
                }

                return next;
            }

            if (_waitForClose && !_closed)
            {
                while (!_closed)
                {
                    await Task.Delay(5, cancellationToken).ConfigureAwait(false);
                }
            }

            return null;
        }

        public Task CloseAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            _closed = true;
            return Task.CompletedTask;
        }
    }
}
