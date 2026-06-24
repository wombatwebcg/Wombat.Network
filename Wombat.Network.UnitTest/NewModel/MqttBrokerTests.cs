using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using LiteDB;
using Wombat.Network.Mqtt.Abstractions;
using Wombat.Network.Mqtt.Broker;
using Wombat.Network.Mqtt.Client;
using Wombat.Network.Mqtt.Persistence.LiteDb;
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
    public async Task Broker_ShouldRouteQoS2Publish_AfterPubRel()
    {
        var broker = new MqttBroker();
        var publisher = new QueueMqttConnection(
            new MqttConnectPacket("publisher-qos2"),
            new MqttPublishPacket("room/b/temp", Encoding.UTF8.GetBytes("qos2"), MqttQualityOfService.ExactlyOnce, 7),
            new MqttPubRelPacket(7),
            new MqttDisconnectPacket());
        var subscriber = QueueMqttConnection.WaitForClose(
            new MqttConnectPacket("subscriber-qos2"),
            new MqttSubscribePacket(1, new[] { new MqttSubscription("room/#", MqttQualityOfService.ExactlyOnce) }));

        var subscriberTask = broker.RunConnectionAsync(subscriber);
        await Task.Delay(10);
        await broker.RunConnectionAsync(publisher);
        await subscriber.CloseAsync();
        await subscriberTask;

        publisher.SentPackets.OfType<MqttPubRecPacket>().Should().ContainSingle(x => x.PacketIdentifier == 7);
        publisher.SentPackets.OfType<MqttPubCompPacket>().Should().ContainSingle(x => x.PacketIdentifier == 7);
        subscriber.SentPackets.OfType<MqttPublishPacket>().Should().ContainSingle(x =>
            x.Topic == "room/b/temp" &&
            x.QualityOfService == MqttQualityOfService.ExactlyOnce &&
            Encoding.UTF8.GetString(x.Payload.ToArray()) == "qos2");
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

    [Fact]
    public async Task Broker_StartAsync_ShouldAcceptTlsClients()
    {
        var port = GetAvailablePort();
        using var certificate = CreateLoopbackCertificate();
        var broker = new MqttBroker(new MqttBrokerOptions().ListenTls(port).UseServerCertificate(certificate));
        await broker.StartAsync(_testCancellationTokenSource.Token);

        try
        {
            var subscriber = new MqttClient(new MqttClientOptions
            {
                ClientId = "tls-sub",
                Endpoint = new MqttEndpoint("localhost", port, MqttTransportScheme.Tls, serverCertificateValidationCallback: AllowAnyServerCertificate)
            });
            var publisher = new MqttClient(new MqttClientOptions
            {
                ClientId = "tls-pub",
                Endpoint = new MqttEndpoint("localhost", port, MqttTransportScheme.Tls, serverCertificateValidationCallback: AllowAnyServerCertificate)
            });

            await subscriber.ConnectAsync(_testCancellationTokenSource.Token);
            await subscriber.SubscribeAsync("tls/#", cancellationToken: _testCancellationTokenSource.Token);
            await publisher.ConnectAsync(_testCancellationTokenSource.Token);
            await publisher.PublishAsync("tls/demo", Encoding.UTF8.GetBytes("tls-ok"), MqttQualityOfService.AtLeastOnce, cancellationToken: _testCancellationTokenSource.Token);

            var inbound = await subscriber.ReceiveAsync(_testCancellationTokenSource.Token);
            var publish = inbound.Should().BeOfType<MqttPublishPacket>().Subject;
            publish.Topic.Should().Be("tls/demo");
            Encoding.UTF8.GetString(publish.Payload.ToArray()).Should().Be("tls-ok");

            await publisher.DisconnectAsync(_testCancellationTokenSource.Token);
            await subscriber.DisconnectAsync(_testCancellationTokenSource.Token);
        }
        finally
        {
            await broker.StopAsync(_testCancellationTokenSource.Token);
        }
    }

    [Fact]
    public async Task Broker_StartAsync_ShouldAcceptWebSocketSecureClients()
    {
        var port = GetAvailablePort();
        using var certificate = CreateLoopbackCertificate();
        var broker = new MqttBroker(new MqttBrokerOptions().ListenWebSocketSecure(port, "/mqtt").UseServerCertificate(certificate));
        await broker.StartAsync(_testCancellationTokenSource.Token);

        try
        {
            var subscriber = new MqttClient(new MqttClientOptions
            {
                ClientId = "wss-sub",
                Endpoint = new MqttEndpoint("localhost", port, MqttTransportScheme.WebSocketSecure, "/mqtt", AllowAnyServerCertificate)
            });
            var publisher = new MqttClient(new MqttClientOptions
            {
                ClientId = "wss-pub",
                Endpoint = new MqttEndpoint("localhost", port, MqttTransportScheme.WebSocketSecure, "/mqtt", AllowAnyServerCertificate)
            });

            await subscriber.ConnectAsync(_testCancellationTokenSource.Token);
            await subscriber.SubscribeAsync("wss/#", cancellationToken: _testCancellationTokenSource.Token);
            await publisher.ConnectAsync(_testCancellationTokenSource.Token);
            await publisher.PublishAsync("wss/demo", Encoding.UTF8.GetBytes("wss-ok"), cancellationToken: _testCancellationTokenSource.Token);

            var inbound = await subscriber.ReceiveAsync(_testCancellationTokenSource.Token);
            var publish = inbound.Should().BeOfType<MqttPublishPacket>().Subject;
            publish.Topic.Should().Be("wss/demo");
            Encoding.UTF8.GetString(publish.Payload.ToArray()).Should().Be("wss-ok");

            await publisher.DisconnectAsync(_testCancellationTokenSource.Token);
            await subscriber.DisconnectAsync(_testCancellationTokenSource.Token);
        }
        finally
        {
            await broker.StopAsync(_testCancellationTokenSource.Token);
        }
    }

    [Fact]
    public async Task Broker_StartAsync_ShouldRejectUnexpectedWebSocketPath()
    {
        var port = GetAvailablePort();
        var broker = new MqttBroker(new MqttBrokerOptions().ListenWebSocket(port, "/mqtt"));
        await broker.StartAsync(_testCancellationTokenSource.Token);

        try
        {
            var client = new MqttClient(new MqttClientOptions
            {
                ClientId = "ws-bad-path",
                Endpoint = new MqttEndpoint("127.0.0.1", port, MqttTransportScheme.WebSocket, "/wrong")
            });

            await Assert.ThrowsAsync<MqttClientException>(() => client.ConnectAsync(_testCancellationTokenSource.Token));
        }
        finally
        {
            await broker.StopAsync(_testCancellationTokenSource.Token);
        }
    }

    [Fact]
    public async Task Broker_ShouldInvokePlugins_AndPersistSessionStore()
    {
        using var database = new LiteDatabase(new MemoryStream());
        using var sessionStore = new LiteDbMqttSessionStore(database);
        var plugin = new RecordingBrokerPlugin();
        var broker = new MqttBroker(new MqttBrokerOptions().UseSessionStore(sessionStore).UsePlugin(plugin));
        var client = new QueueMqttConnection(
            MqttProtocolVersion.V311,
            new MqttConnectPacket("plugin-client", cleanStart: false, protocolVersion: MqttProtocolVersion.V311),
            new MqttSubscribePacket(1, new[] { new MqttSubscription("plugin/topic", MqttQualityOfService.AtLeastOnce) }),
            new MqttPublishPacket("plugin/topic", Encoding.UTF8.GetBytes("body"), MqttQualityOfService.AtLeastOnce, 9),
            new MqttDisconnectPacket());

        await broker.RunConnectionAsync(client);

        plugin.Events.Should().ContainInOrder("connected:plugin-client", "subscribed:plugin-client", "publishing:plugin-client", "disconnected:plugin-client");

        var restored = sessionStore.Get("plugin-client");
        restored.Should().NotBeNull();
        restored.Subscriptions.Should().ContainSingle(x => x.TopicFilter == "plugin/topic");
    }

    [Fact]
    public async Task Broker_ShouldSupportV311Clients_EndToEnd()
    {
        var broker = new MqttBroker();
        var publisher = new QueueMqttConnection(
            MqttProtocolVersion.V311,
            new MqttConnectPacket("publisher-311", protocolVersion: MqttProtocolVersion.V311),
            new MqttPublishPacket("room/311", Encoding.UTF8.GetBytes("body-311"), MqttQualityOfService.AtLeastOnce, 7),
            new MqttDisconnectPacket());
        var subscriber = QueueMqttConnection.WaitForClose(
            MqttProtocolVersion.V311,
            new MqttConnectPacket("subscriber-311", protocolVersion: MqttProtocolVersion.V311),
            new MqttSubscribePacket(1, new[] { new MqttSubscription("room/#", MqttQualityOfService.AtLeastOnce) }));

        var subscriberTask = broker.RunConnectionAsync(subscriber);
        await Task.Delay(10);
        await broker.RunConnectionAsync(publisher);
        await subscriber.CloseAsync();
        await subscriberTask;

        subscriber.SentPackets.OfType<MqttConnAckPacket>().Should().ContainSingle();
        subscriber.SentPackets.OfType<MqttSubAckPacket>().Should().ContainSingle();
        subscriber.SentPackets.OfType<MqttPublishPacket>().Should().ContainSingle(x =>
            x.Topic == "room/311" &&
            Encoding.UTF8.GetString(x.Payload.ToArray()) == "body-311");
        publisher.SentPackets.OfType<MqttPubAckPacket>().Should().ContainSingle(x => x.PacketIdentifier == 7);
    }

    [Fact]
    public async Task Broker_ShouldRestorePersistentSessionAndRetainedMessages_AfterRestart()
    {
        using var database = new LiteDatabase(new MemoryStream());
        using var sessionStore = new LiteDbMqttSessionStore(database);

        var firstBroker = new MqttBroker(new MqttBrokerOptions().UseSessionStore(sessionStore));
        var seedClient = new QueueMqttConnection(
            new MqttConnectPacket("restore-client", cleanStart: false),
            new MqttSubscribePacket(1, new[] { new MqttSubscription("restore/#", MqttQualityOfService.AtLeastOnce) }),
            new MqttPublishPacket("restore/topic", Encoding.UTF8.GetBytes("retained-body"), retain: true),
            new MqttDisconnectPacket());

        await firstBroker.RunConnectionAsync(seedClient);

        var secondBroker = new MqttBroker(new MqttBrokerOptions().UseSessionStore(sessionStore));
        var restoredClient = new QueueMqttConnection(
            new MqttConnectPacket("restore-client", cleanStart: false),
            new MqttDisconnectPacket());

        await secondBroker.RunConnectionAsync(restoredClient);

        restoredClient.SentPackets.OfType<MqttConnAckPacket>().Should().ContainSingle(x => x.SessionPresent);
        restoredClient.SentPackets.OfType<MqttPublishPacket>().Should().ContainSingle(x =>
            x.Topic == "restore/topic" &&
            Encoding.UTF8.GetString(x.Payload.ToArray()) == "retained-body");
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

        public static QueueMqttConnection CloseWithoutDisconnect(MqttProtocolVersion protocolVersion, params MqttPacket[] packets)
            => new QueueMqttConnection(protocolVersion, false, true, packets);

        public static QueueMqttConnection WaitForClose(MqttProtocolVersion protocolVersion, params MqttPacket[] packets)
            => new QueueMqttConnection(protocolVersion, true, false, packets);

        public QueueMqttConnection(bool waitForClose = false, bool closeWithoutDisconnect = false, params MqttPacket[] packets)
            : this(MqttProtocolVersion.V500, waitForClose, closeWithoutDisconnect, packets)
        {
        }

        public QueueMqttConnection(MqttProtocolVersion protocolVersion, params MqttPacket[] packets)
            : this(protocolVersion, false, false, packets)
        {
        }

        public QueueMqttConnection(MqttProtocolVersion protocolVersion, bool waitForClose = false, bool closeWithoutDisconnect = false, params MqttPacket[] packets)
        {
            ProtocolVersion = protocolVersion;
            _incoming = new Queue<ReadOnlyMemory<byte>>(packets.Select(x => (ReadOnlyMemory<byte>)_codec.Encode(x, protocolVersion)));
            _waitForClose = waitForClose;
            _closeWithoutDisconnect = closeWithoutDisconnect;
            SentPackets = new System.Collections.Concurrent.ConcurrentQueue<MqttPacket>();
        }

        public System.Collections.Concurrent.ConcurrentQueue<MqttPacket> SentPackets { get; }

        public MqttProtocolVersion ProtocolVersion { get; }

        public ValueTask SendAsync(ReadOnlyMemory<byte> packet, System.Threading.CancellationToken cancellationToken = default)
        {
            var decoded = _codec.Decode(packet.Span, ProtocolVersion);
            SentPackets.Enqueue(decoded);
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

    private sealed class RecordingBrokerPlugin : MqttBrokerPlugin
    {
        public List<string> Events { get; } = new List<string>();

        public override Task OnClientConnectedAsync(MqttBrokerConnectionContext context, System.Threading.CancellationToken cancellationToken = default)
        {
            Events.Add("connected:" + context.Session.ClientId);
            return Task.CompletedTask;
        }

        public override Task OnClientDisconnectedAsync(MqttBrokerConnectionContext context, System.Threading.CancellationToken cancellationToken = default)
        {
            Events.Add("disconnected:" + context.Session.ClientId);
            return Task.CompletedTask;
        }

        public override Task OnSubscribedAsync(MqttBrokerSubscriptionContext context, System.Threading.CancellationToken cancellationToken = default)
        {
            Events.Add("subscribed:" + context.Session.ClientId);
            return Task.CompletedTask;
        }

        public override Task OnPublishingAsync(MqttBrokerPublishContext context, System.Threading.CancellationToken cancellationToken = default)
        {
            Events.Add("publishing:" + context.Session.ClientId);
            return Task.CompletedTask;
        }
    }

    private static bool AllowAnyServerCertificate(object? sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        => true;

    private static X509Certificate2 CreateLoopbackCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return new X509Certificate2(ephemeral.Export(X509ContentType.Pfx));
    }
}
