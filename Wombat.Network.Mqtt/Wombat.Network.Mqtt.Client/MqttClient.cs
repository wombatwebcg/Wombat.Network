using System;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Mqtt.Abstractions;
using Wombat.Network.Mqtt.Protocol;
using Wombat.Network.Mqtt.Transport;

namespace Wombat.Network.Mqtt.Client;

public sealed class MqttClientOptions
{
    public MqttEndpoint Endpoint { get; set; }

    public string ClientId { get; set; } = "wombat-client";

    public ushort KeepAliveSeconds { get; set; } = 30;

    public bool CleanStart { get; set; } = true;
}

public sealed class MqttClient
{
    private readonly MqttClientOptions _options;
    private readonly IMqttConnectionFactory _connectionFactory;
    private readonly MqttPacketCodec _codec;
    private IMqttConnection _connection;
    private ushort _packetIdentifier;

    public MqttClient(MqttClientOptions options, IMqttConnectionFactory connectionFactory = null, MqttPacketCodec codec = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionFactory = connectionFactory ?? new MqttConnectionFactory();
        _codec = codec ?? new MqttPacketCodec();
    }

    public async Task<MqttConnAckPacket> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_options.Endpoint == null)
        {
            throw new InvalidOperationException("MQTT endpoint is required.");
        }

        _connection = await _connectionFactory.ConnectAsync(_options.Endpoint, cancellationToken).ConfigureAwait(false);
        await SendPacketAsync(new MqttConnectPacket(_options.ClientId, _options.CleanStart, _options.KeepAliveSeconds), cancellationToken).ConfigureAwait(false);
        var response = await ReceiveRequiredAsync(cancellationToken).ConfigureAwait(false);
        var connAck = response as MqttConnAckPacket;
        if (connAck == null)
        {
            throw new InvalidOperationException("Expected CONNACK.");
        }

        return connAck;
    }

    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, MqttQualityOfService qualityOfService = MqttQualityOfService.AtMostOnce, CancellationToken cancellationToken = default)
    {
        var packetIdentifier = qualityOfService == MqttQualityOfService.AtLeastOnce ? NextPacketIdentifier() : (ushort)0;
        await SendPacketAsync(new MqttPublishPacket(topic, payload, qualityOfService, packetIdentifier), cancellationToken).ConfigureAwait(false);
        if (qualityOfService == MqttQualityOfService.AtLeastOnce)
        {
            var response = await ReceiveRequiredAsync(cancellationToken).ConfigureAwait(false);
            var pubAck = response as MqttPubAckPacket;
            if (pubAck == null || pubAck.PacketIdentifier != packetIdentifier)
            {
                throw new InvalidOperationException("Expected matching PUBACK.");
            }
        }
    }

    public async Task<MqttSubAckPacket> SubscribeAsync(string topicFilter, MqttQualityOfService qualityOfService = MqttQualityOfService.AtMostOnce, CancellationToken cancellationToken = default)
    {
        var packetIdentifier = NextPacketIdentifier();
        await SendPacketAsync(new MqttSubscribePacket(packetIdentifier, new[] { new MqttSubscription(topicFilter, qualityOfService) }), cancellationToken).ConfigureAwait(false);

        var response = await ReceiveRequiredAsync(cancellationToken).ConfigureAwait(false);
        var subAck = response as MqttSubAckPacket;
        if (subAck == null || subAck.PacketIdentifier != packetIdentifier)
        {
            throw new InvalidOperationException("Expected matching SUBACK.");
        }

        return subAck;
    }

    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        await SendPacketAsync(new MqttPingReqPacket(), cancellationToken).ConfigureAwait(false);
        var response = await ReceiveRequiredAsync(cancellationToken).ConfigureAwait(false);
        if (!(response is MqttPingRespPacket))
        {
            throw new InvalidOperationException("Expected PINGRESP.");
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection == null)
        {
            return;
        }

        await SendPacketAsync(new MqttDisconnectPacket(), cancellationToken).ConfigureAwait(false);
        await _connection.CloseAsync(cancellationToken).ConfigureAwait(false);
        _connection = null;
    }

    public async Task<MqttPacket> ReceiveAsync(CancellationToken cancellationToken = default)
        => await ReceiveRequiredAsync(cancellationToken).ConfigureAwait(false);

    private async Task SendPacketAsync(MqttPacket packet, CancellationToken cancellationToken)
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("Client is not connected.");
        }

        await _connection.SendAsync(_codec.Encode(packet), cancellationToken).ConfigureAwait(false);
    }

    private async Task<MqttPacket> ReceiveRequiredAsync(CancellationToken cancellationToken)
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("Client is not connected.");
        }

        var payload = await _connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        if (!payload.HasValue)
        {
            throw new InvalidOperationException("Connection closed.");
        }

        return _codec.Decode(payload.Value.Span);
    }

    private ushort NextPacketIdentifier()
    {
        _packetIdentifier++;
        if (_packetIdentifier == 0)
        {
            _packetIdentifier = 1;
        }

        return _packetIdentifier;
    }
}
