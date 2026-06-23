using System;
using System.Collections.Generic;

namespace Wombat.Network.Mqtt.Protocol;

public enum MqttPacketType : byte
{
    Connect = 1,
    ConnAck = 2,
    Publish = 3,
    PubAck = 4,
    Subscribe = 8,
    SubAck = 9,
    PingReq = 12,
    PingResp = 13,
    Disconnect = 14,
}

public enum MqttQualityOfService : byte
{
    AtMostOnce = 0,
    AtLeastOnce = 1,
}

public abstract class MqttPacket
{
    protected MqttPacket(MqttPacketType packetType, byte flags)
    {
        PacketType = packetType;
        Flags = flags;
    }

    public MqttPacketType PacketType { get; }

    public byte Flags { get; }
}

public sealed class MqttConnectPacket : MqttPacket
{
    public MqttConnectPacket(string clientId, bool cleanStart = true, ushort keepAliveSeconds = 30, MqttPublishPacket willMessage = null)
        : base(MqttPacketType.Connect, 0)
    {
        ClientId = clientId ?? string.Empty;
        CleanStart = cleanStart;
        KeepAliveSeconds = keepAliveSeconds;
        WillMessage = willMessage;
    }

    public string ClientId { get; }

    public bool CleanStart { get; }

    public ushort KeepAliveSeconds { get; }

    public MqttPublishPacket WillMessage { get; }
}

public sealed class MqttConnAckPacket : MqttPacket
{
    public MqttConnAckPacket(byte reasonCode = 0, bool sessionPresent = false)
        : base(MqttPacketType.ConnAck, 0)
    {
        ReasonCode = reasonCode;
        SessionPresent = sessionPresent;
    }

    public byte ReasonCode { get; }

    public bool SessionPresent { get; }
}

public sealed class MqttPublishPacket : MqttPacket
{
    public MqttPublishPacket(string topic, ReadOnlyMemory<byte> payload, MqttQualityOfService qualityOfService = MqttQualityOfService.AtMostOnce, ushort packetIdentifier = 0, bool retain = false, bool duplicate = false)
        : base(MqttPacketType.Publish, (byte)((duplicate ? 0x08 : 0) | (((int)qualityOfService & 0x03) << 1) | (retain ? 0x01 : 0)))
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic is required.", nameof(topic));
        }

        Topic = topic;
        Payload = payload;
        QualityOfService = qualityOfService;
        PacketIdentifier = packetIdentifier;
        Retain = retain;
        Duplicate = duplicate;
    }

    public string Topic { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public MqttQualityOfService QualityOfService { get; }

    public ushort PacketIdentifier { get; }

    public bool Retain { get; }

    public bool Duplicate { get; }
}

public sealed class MqttPubAckPacket : MqttPacket
{
    public MqttPubAckPacket(ushort packetIdentifier, byte reasonCode = 0)
        : base(MqttPacketType.PubAck, 0)
    {
        PacketIdentifier = packetIdentifier;
        ReasonCode = reasonCode;
    }

    public ushort PacketIdentifier { get; }

    public byte ReasonCode { get; }
}

public sealed class MqttSubscription
{
    public MqttSubscription(string topicFilter, MqttQualityOfService qualityOfService = MqttQualityOfService.AtMostOnce)
    {
        if (string.IsNullOrWhiteSpace(topicFilter))
        {
            throw new ArgumentException("Topic filter is required.", nameof(topicFilter));
        }

        TopicFilter = topicFilter;
        QualityOfService = qualityOfService;
    }

    public string TopicFilter { get; }

    public MqttQualityOfService QualityOfService { get; }
}

public sealed class MqttSubscribePacket : MqttPacket
{
    public MqttSubscribePacket(ushort packetIdentifier, IReadOnlyList<MqttSubscription> subscriptions)
        : base(MqttPacketType.Subscribe, 0x02)
    {
        if (subscriptions == null || subscriptions.Count == 0)
        {
            throw new ArgumentException("At least one subscription is required.", nameof(subscriptions));
        }

        PacketIdentifier = packetIdentifier;
        Subscriptions = subscriptions;
    }

    public ushort PacketIdentifier { get; }

    public IReadOnlyList<MqttSubscription> Subscriptions { get; }
}

public sealed class MqttSubAckPacket : MqttPacket
{
    public MqttSubAckPacket(ushort packetIdentifier, IReadOnlyList<byte> reasonCodes)
        : base(MqttPacketType.SubAck, 0)
    {
        if (reasonCodes == null || reasonCodes.Count == 0)
        {
            throw new ArgumentException("At least one reason code is required.", nameof(reasonCodes));
        }

        PacketIdentifier = packetIdentifier;
        ReasonCodes = reasonCodes;
    }

    public ushort PacketIdentifier { get; }

    public IReadOnlyList<byte> ReasonCodes { get; }
}

public sealed class MqttPingReqPacket : MqttPacket
{
    public MqttPingReqPacket()
        : base(MqttPacketType.PingReq, 0)
    {
    }
}

public sealed class MqttPingRespPacket : MqttPacket
{
    public MqttPingRespPacket()
        : base(MqttPacketType.PingResp, 0)
    {
    }
}

public sealed class MqttDisconnectPacket : MqttPacket
{
    public MqttDisconnectPacket(byte reasonCode = 0)
        : base(MqttPacketType.Disconnect, 0)
    {
        ReasonCode = reasonCode;
    }

    public byte ReasonCode { get; }
}
