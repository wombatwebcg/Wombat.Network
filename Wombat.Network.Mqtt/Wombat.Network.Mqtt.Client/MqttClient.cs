using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    public MqttPublishPacket WillMessage { get; set; }
}

public sealed class MqttClient
{
    private readonly MqttClientOptions _options;
    private readonly IMqttConnectionFactory _connectionFactory;
    private readonly MqttPacketCodec _codec;
    private readonly ILogger _logger;
    private IMqttConnection _connection;
    private ushort _packetIdentifier;

    public MqttClient(MqttClientOptions options, IMqttConnectionFactory connectionFactory = null, MqttPacketCodec codec = null, ILogger logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionFactory = connectionFactory ?? new MqttConnectionFactory();
        _codec = codec ?? new MqttPacketCodec();
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<MqttConnAckPacket> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_options.Endpoint == null)
        {
            throw new InvalidOperationException("MQTT endpoint is required.");
        }

        _connection = await _connectionFactory.ConnectAsync(_options.Endpoint, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("MQTT client connecting to {Host}:{Port} over {Scheme}.", _options.Endpoint.Host, _options.Endpoint.Port, _options.Endpoint.Scheme);
        await SendPacketAsync(new MqttConnectPacket(_options.ClientId, _options.CleanStart, _options.KeepAliveSeconds, _options.WillMessage), cancellationToken).ConfigureAwait(false);
        var response = await ReceiveRequiredAsync(cancellationToken).ConfigureAwait(false);
        var connAck = response as MqttConnAckPacket;
        if (connAck == null)
        {
            throw new MqttClientException("Expected CONNACK.");
        }

        _logger.LogInformation("MQTT client connected as {ClientId}.", _options.ClientId);
        return connAck;
    }

    public Task<MqttConnAckPacket> ConnectWebSocketAsync(MqttEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        if (endpoint == null)
        {
            throw new ArgumentNullException(nameof(endpoint));
        }

        _options.Endpoint = endpoint;
        return ConnectAsync(cancellationToken);
    }

    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, MqttQualityOfService qualityOfService = MqttQualityOfService.AtMostOnce, bool retain = false, CancellationToken cancellationToken = default)
    {
        var packetIdentifier = qualityOfService == MqttQualityOfService.AtLeastOnce ? NextPacketIdentifier() : (ushort)0;
        await SendPacketAsync(new MqttPublishPacket(topic, payload, qualityOfService, packetIdentifier, retain), cancellationToken).ConfigureAwait(false);
        if (qualityOfService == MqttQualityOfService.AtLeastOnce)
        {
            var response = await ReceiveRequiredAsync(cancellationToken).ConfigureAwait(false);
            var pubAck = response as MqttPubAckPacket;
            if (pubAck == null || pubAck.PacketIdentifier != packetIdentifier)
            {
                throw new MqttClientException("Expected matching PUBACK.");
            }
        }

        _logger.LogInformation("MQTT client published {Topic} with qos {QualityOfService}.", topic, qualityOfService);
    }

    public async Task<MqttSubAckPacket> SubscribeAsync(string topicFilter, MqttQualityOfService qualityOfService = MqttQualityOfService.AtMostOnce, CancellationToken cancellationToken = default)
    {
        var packetIdentifier = NextPacketIdentifier();
        await SendPacketAsync(new MqttSubscribePacket(packetIdentifier, new[] { new MqttSubscription(topicFilter, qualityOfService) }), cancellationToken).ConfigureAwait(false);

        var response = await ReceiveRequiredAsync(cancellationToken).ConfigureAwait(false);
        var subAck = response as MqttSubAckPacket;
        if (subAck == null || subAck.PacketIdentifier != packetIdentifier)
        {
            throw new MqttClientException("Expected matching SUBACK.");
        }

        _logger.LogInformation("MQTT client subscribed {TopicFilter}.", topicFilter);
        return subAck;
    }

    public Task<MqttSubAckPacket> SubscribeAsync(IEnumerable<MqttSubscription> subscriptions, CancellationToken cancellationToken = default)
    {
        if (subscriptions == null)
        {
            throw new ArgumentNullException(nameof(subscriptions));
        }

        return SubscribeCoreAsync(new List<MqttSubscription>(subscriptions), cancellationToken);
    }

    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        await SendPacketAsync(new MqttPingReqPacket(), cancellationToken).ConfigureAwait(false);
        var response = await ReceiveRequiredAsync(cancellationToken).ConfigureAwait(false);
        if (!(response is MqttPingRespPacket))
        {
            throw new MqttClientException("Expected PINGRESP.");
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
        _logger.LogInformation("MQTT client disconnected.");
    }

    public async Task<MqttPacket> ReceiveAsync(CancellationToken cancellationToken = default)
        => await ReceiveRequiredAsync(cancellationToken).ConfigureAwait(false);

    private async Task SendPacketAsync(MqttPacket packet, CancellationToken cancellationToken)
    {
        if (_connection == null)
        {
            throw new MqttClientException("Client is not connected.");
        }

        await _connection.SendAsync(_codec.Encode(packet), cancellationToken).ConfigureAwait(false);
    }

    private async Task<MqttPacket> ReceiveRequiredAsync(CancellationToken cancellationToken)
    {
        if (_connection == null)
        {
            throw new MqttClientException("Client is not connected.");
        }

        var payload = await _connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        if (!payload.HasValue)
        {
            throw new MqttClientException("Connection closed.");
        }

        return _codec.Decode(payload.Value.Span);
    }

    private async Task<MqttSubAckPacket> SubscribeCoreAsync(IReadOnlyList<MqttSubscription> subscriptions, CancellationToken cancellationToken)
    {
        var packetIdentifier = NextPacketIdentifier();
        await SendPacketAsync(new MqttSubscribePacket(packetIdentifier, subscriptions), cancellationToken).ConfigureAwait(false);
        var response = await ReceiveRequiredAsync(cancellationToken).ConfigureAwait(false);
        var subAck = response as MqttSubAckPacket;
        if (subAck == null || subAck.PacketIdentifier != packetIdentifier)
        {
            throw new MqttClientException("Expected matching SUBACK.");
        }

        _logger.LogInformation("MQTT client subscribed {Count} topic filters.", subscriptions.Count);
        return subAck;
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

public sealed class MqttClientException : InvalidOperationException
{
    public MqttClientException(string message)
        : base(message)
    {
    }
}
