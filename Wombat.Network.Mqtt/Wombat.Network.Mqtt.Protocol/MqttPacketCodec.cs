using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace Wombat.Network.Mqtt.Protocol;

public sealed class MqttPacketCodec
{
    private static readonly byte[] ProtocolName = Encoding.UTF8.GetBytes("MQTT");

    public byte[] Encode(MqttPacket packet, MqttProtocolVersion protocolVersion = MqttProtocolVersion.V500)
    {
        if (packet == null)
        {
            throw new ArgumentNullException(nameof(packet));
        }

        var body = new List<byte>();
        switch (packet)
        {
            case MqttConnectPacket connect:
                WriteConnect(body, connect);
                break;
            case MqttConnAckPacket connAck:
                WriteConnAck(body, connAck, protocolVersion);
                break;
            case MqttPublishPacket publish:
                WritePublish(body, publish, protocolVersion);
                break;
            case MqttPubAckPacket pubAck:
                WritePubAck(body, pubAck, protocolVersion);
                break;
            case MqttPubRecPacket pubRec:
                WritePubRec(body, pubRec, protocolVersion);
                break;
            case MqttPubRelPacket pubRel:
                WritePubRel(body, pubRel, protocolVersion);
                break;
            case MqttPubCompPacket pubComp:
                WritePubComp(body, pubComp, protocolVersion);
                break;
            case MqttSubscribePacket subscribe:
                WriteSubscribe(body, subscribe, protocolVersion);
                break;
            case MqttSubAckPacket subAck:
                WriteSubAck(body, subAck, protocolVersion);
                break;
            case MqttPingReqPacket _:
                break;
            case MqttPingRespPacket _:
                break;
            case MqttDisconnectPacket disconnect:
                WriteDisconnect(body, disconnect, protocolVersion);
                break;
            default:
                throw new NotSupportedException("Unsupported packet type: " + packet.GetType().FullName);
        }

        var frame = new byte[1 + GetVariableByteIntegerSize(body.Count) + body.Count];
        frame[0] = (byte)(((int)packet.PacketType << 4) | packet.Flags);
        var offset = 1 + WriteVariableByteInteger(frame, 1, body.Count);
        body.CopyTo(frame, offset);
        return frame;
    }

    public bool TryReadPacket(ref ReadOnlySequence<byte> buffer, out MqttPacket packet)
    {
        if (!TryReadPacketBytes(ref buffer, out var packetBytes))
        {
            packet = null;
            return false;
        }

        packet = Decode(packetBytes.ToArray());
        return true;
    }

    public bool TryReadPacketBytes(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> packetBytes)
    {
        packetBytes = default;
        if (buffer.Length < 2)
        {
            return false;
        }

        var header = buffer.ToArray();
        var multiplier = 1;
        var remainingLength = 0;
        var lengthBytes = 0;
        byte encodedByte;

        do
        {
            if (1 + lengthBytes >= header.Length)
            {
                return false;
            }

            encodedByte = header[1 + lengthBytes];
            remainingLength += (encodedByte & 0x7F) * multiplier;
            multiplier *= 128;
            lengthBytes++;

            if (lengthBytes > 4)
            {
                throw new InvalidOperationException("Invalid MQTT remaining length.");
            }
        }
        while ((encodedByte & 0x80) != 0);

        var totalLength = 1 + lengthBytes + remainingLength;
        if (buffer.Length < totalLength)
        {
            return false;
        }

        packetBytes = buffer.Slice(0, totalLength);
        buffer = buffer.Slice(totalLength);
        return true;
    }

    public MqttPacket Decode(ReadOnlySpan<byte> packetBytes)
        => Decode(packetBytes, MqttProtocolVersion.V500, false);

    public MqttPacket Decode(ReadOnlySpan<byte> packetBytes, MqttProtocolVersion protocolVersion)
        => Decode(packetBytes, protocolVersion, true);

    private MqttPacket Decode(ReadOnlySpan<byte> packetBytes, MqttProtocolVersion protocolVersion, bool protocolVersionKnown)
    {
        if (packetBytes.Length < 2)
        {
            throw new InvalidOperationException("MQTT packet is incomplete.");
        }

        var header = packetBytes[0];
        var packetType = (MqttPacketType)(header >> 4);
        var flags = (byte)(header & 0x0F);
        var offset = 1;
        var remainingLength = ReadVariableByteInteger(packetBytes, ref offset);
        var end = offset + remainingLength;
        if (end > packetBytes.Length)
        {
            throw new InvalidOperationException("MQTT packet payload is incomplete.");
        }

        switch (packetType)
        {
            case MqttPacketType.Connect:
                return ReadConnect(packetBytes, offset);
            case MqttPacketType.ConnAck:
                return ReadConnAck(packetBytes, offset, protocolVersionKnown ? protocolVersion : MqttProtocolVersion.V500);
            case MqttPacketType.Publish:
                return ReadPublish(packetBytes, offset, end, flags, protocolVersionKnown ? protocolVersion : MqttProtocolVersion.V500);
            case MqttPacketType.PubAck:
                return ReadPubAck(packetBytes, offset, end, protocolVersionKnown ? protocolVersion : MqttProtocolVersion.V500);
            case MqttPacketType.PubRec:
                return ReadPubRec(packetBytes, offset, end, protocolVersionKnown ? protocolVersion : MqttProtocolVersion.V500);
            case MqttPacketType.PubRel:
                return ReadPubRel(packetBytes, offset, end, protocolVersionKnown ? protocolVersion : MqttProtocolVersion.V500);
            case MqttPacketType.PubComp:
                return ReadPubComp(packetBytes, offset, end, protocolVersionKnown ? protocolVersion : MqttProtocolVersion.V500);
            case MqttPacketType.Subscribe:
                return ReadSubscribe(packetBytes, offset, end, protocolVersionKnown ? protocolVersion : MqttProtocolVersion.V500);
            case MqttPacketType.SubAck:
                return ReadSubAck(packetBytes, offset, end, protocolVersionKnown ? protocolVersion : MqttProtocolVersion.V500);
            case MqttPacketType.PingReq:
                return new MqttPingReqPacket();
            case MqttPacketType.PingResp:
                return new MqttPingRespPacket();
            case MqttPacketType.Disconnect:
                return ReadDisconnect(packetBytes, offset, end, protocolVersionKnown ? protocolVersion : MqttProtocolVersion.V500);
            default:
                throw new NotSupportedException("Unsupported packet type: " + packetType);
        }
    }

    private static void WriteConnect(List<byte> body, MqttConnectPacket packet)
    {
        WriteString(body, ProtocolName);
        body.Add((byte)packet.ProtocolVersion);
        var flags = packet.CleanStart ? (byte)0x02 : (byte)0x00;
        if (packet.WillMessage != null)
        {
            flags |= 0x04;
            flags |= (byte)(((int)packet.WillMessage.QualityOfService & 0x03) << 3);
            if (packet.WillMessage.Retain)
            {
                flags |= 0x20;
            }
        }

        body.Add(flags);
        WriteUInt16(body, packet.KeepAliveSeconds);
        if (packet.ProtocolVersion == MqttProtocolVersion.V500)
        {
            body.Add(0);
        }

        WriteString(body, packet.ClientId);
        if (packet.WillMessage != null)
        {
            if (packet.ProtocolVersion == MqttProtocolVersion.V500)
            {
                body.Add(0);
            }

            WriteString(body, packet.WillMessage.Topic);
            WriteBinary(body, packet.WillMessage.Payload);
        }
    }

    private static void WriteConnAck(List<byte> body, MqttConnAckPacket packet, MqttProtocolVersion protocolVersion)
    {
        body.Add(packet.SessionPresent ? (byte)0x01 : (byte)0x00);
        body.Add(packet.ReasonCode);
        if (protocolVersion == MqttProtocolVersion.V500)
        {
            body.Add(0);
        }
    }

    private static void WritePublish(List<byte> body, MqttPublishPacket packet, MqttProtocolVersion protocolVersion)
    {
        WriteString(body, packet.Topic);
        if (packet.QualityOfService != MqttQualityOfService.AtMostOnce)
        {
            WriteUInt16(body, packet.PacketIdentifier);
        }

        if (protocolVersion == MqttProtocolVersion.V500)
        {
            body.Add(0);
        }

        if (!packet.Payload.IsEmpty)
        {
            body.AddRange(packet.Payload.ToArray());
        }
    }

    private static void WritePubAck(List<byte> body, MqttPubAckPacket packet, MqttProtocolVersion protocolVersion)
    {
        WriteUInt16(body, packet.PacketIdentifier);
        if (protocolVersion == MqttProtocolVersion.V500)
        {
            body.Add(packet.ReasonCode);
            body.Add(0);
        }
    }

    private static void WritePubRec(List<byte> body, MqttPubRecPacket packet, MqttProtocolVersion protocolVersion)
    {
        WriteUInt16(body, packet.PacketIdentifier);
        if (protocolVersion == MqttProtocolVersion.V500)
        {
            body.Add(packet.ReasonCode);
            body.Add(0);
        }
    }

    private static void WritePubRel(List<byte> body, MqttPubRelPacket packet, MqttProtocolVersion protocolVersion)
    {
        WriteUInt16(body, packet.PacketIdentifier);
        if (protocolVersion == MqttProtocolVersion.V500)
        {
            body.Add(packet.ReasonCode);
            body.Add(0);
        }
    }

    private static void WritePubComp(List<byte> body, MqttPubCompPacket packet, MqttProtocolVersion protocolVersion)
    {
        WriteUInt16(body, packet.PacketIdentifier);
        if (protocolVersion == MqttProtocolVersion.V500)
        {
            body.Add(packet.ReasonCode);
            body.Add(0);
        }
    }

    private static void WriteSubscribe(List<byte> body, MqttSubscribePacket packet, MqttProtocolVersion protocolVersion)
    {
        WriteUInt16(body, packet.PacketIdentifier);
        if (protocolVersion == MqttProtocolVersion.V500)
        {
            body.Add(0);
        }

        for (var i = 0; i < packet.Subscriptions.Count; i++)
        {
            var subscription = packet.Subscriptions[i];
            WriteString(body, subscription.TopicFilter);
            body.Add((byte)subscription.QualityOfService);
        }
    }

    private static void WriteSubAck(List<byte> body, MqttSubAckPacket packet, MqttProtocolVersion protocolVersion)
    {
        WriteUInt16(body, packet.PacketIdentifier);
        if (protocolVersion == MqttProtocolVersion.V500)
        {
            body.Add(0);
        }

        for (var i = 0; i < packet.ReasonCodes.Count; i++)
        {
            body.Add(packet.ReasonCodes[i]);
        }
    }

    private static void WriteDisconnect(List<byte> body, MqttDisconnectPacket packet, MqttProtocolVersion protocolVersion)
    {
        if (protocolVersion == MqttProtocolVersion.V500)
        {
            body.Add(packet.ReasonCode);
            body.Add(0);
        }
    }

    private static MqttPacket ReadConnect(ReadOnlySpan<byte> packetBytes, int offset)
    {
        var protocolName = ReadString(packetBytes, ref offset);
        if (!string.Equals(protocolName, "MQTT", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported protocol name.");
        }

        var version = packetBytes[offset++];
        if (version != (byte)MqttProtocolVersion.V500 && version != (byte)MqttProtocolVersion.V311)
        {
            throw new InvalidOperationException("Unsupported MQTT version.");
        }

        var flags = packetBytes[offset++];
        var keepAlive = ReadUInt16(packetBytes, ref offset);
        var protocolVersion = (MqttProtocolVersion)version;
        if (protocolVersion == MqttProtocolVersion.V500)
        {
            SkipProperties(packetBytes, ref offset);
        }

        var clientId = ReadString(packetBytes, ref offset);
        MqttPublishPacket willMessage = null;
        if ((flags & 0x04) != 0)
        {
            if (protocolVersion == MqttProtocolVersion.V500)
            {
                SkipProperties(packetBytes, ref offset);
            }

            var topic = ReadString(packetBytes, ref offset);
            var payload = ReadBinary(packetBytes, ref offset);
            var willQos = (MqttQualityOfService)((flags >> 3) & 0x03);
            var willRetain = (flags & 0x20) != 0;
            willMessage = new MqttPublishPacket(topic, payload, willQos, retain: willRetain);
        }

        return new MqttConnectPacket(clientId, (flags & 0x02) != 0, keepAlive, willMessage, protocolVersion);
    }

    private static MqttPacket ReadConnAck(ReadOnlySpan<byte> packetBytes, int offset, MqttProtocolVersion protocolVersion)
    {
        var acknowledgeFlags = packetBytes[offset++];
        var reasonCode = packetBytes[offset++];
        if (protocolVersion == MqttProtocolVersion.V500)
        {
            SkipProperties(packetBytes, ref offset);
        }

        return new MqttConnAckPacket(reasonCode, (acknowledgeFlags & 0x01) != 0);
    }

    private static MqttPacket ReadPublish(ReadOnlySpan<byte> packetBytes, int offset, int end, byte flags, MqttProtocolVersion protocolVersion)
    {
        var topic = ReadString(packetBytes, ref offset);
        var qos = (MqttQualityOfService)((flags >> 1) & 0x03);
        ushort packetIdentifier = 0;
        if (qos != MqttQualityOfService.AtMostOnce)
        {
            packetIdentifier = ReadUInt16(packetBytes, ref offset);
        }

        if (protocolVersion == MqttProtocolVersion.V500)
        {
            SkipProperties(packetBytes, ref offset);
        }

        var payloadLength = end - offset;
        var payload = payloadLength == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(packetBytes.Slice(offset, payloadLength).ToArray());
        return new MqttPublishPacket(topic, payload, qos, packetIdentifier, (flags & 0x01) != 0, (flags & 0x08) != 0);
    }

    private static MqttPacket ReadPubAck(ReadOnlySpan<byte> packetBytes, int offset, int end, MqttProtocolVersion protocolVersion)
    {
        var packetIdentifier = ReadUInt16(packetBytes, ref offset);
        var reasonCode = (byte)0;
        if (protocolVersion == MqttProtocolVersion.V500 && offset < end)
        {
            reasonCode = packetBytes[offset++];
            if (offset < end)
            {
                SkipProperties(packetBytes, ref offset);
            }
        }

        return new MqttPubAckPacket(packetIdentifier, reasonCode);
    }

    private static MqttPacket ReadPubRec(ReadOnlySpan<byte> packetBytes, int offset, int end, MqttProtocolVersion protocolVersion)
    {
        var packetIdentifier = ReadUInt16(packetBytes, ref offset);
        var reasonCode = (byte)0;
        if (protocolVersion == MqttProtocolVersion.V500 && offset < end)
        {
            reasonCode = packetBytes[offset++];
            if (offset < end)
            {
                SkipProperties(packetBytes, ref offset);
            }
        }

        return new MqttPubRecPacket(packetIdentifier, reasonCode);
    }

    private static MqttPacket ReadPubRel(ReadOnlySpan<byte> packetBytes, int offset, int end, MqttProtocolVersion protocolVersion)
    {
        var packetIdentifier = ReadUInt16(packetBytes, ref offset);
        var reasonCode = (byte)0;
        if (protocolVersion == MqttProtocolVersion.V500 && offset < end)
        {
            reasonCode = packetBytes[offset++];
            if (offset < end)
            {
                SkipProperties(packetBytes, ref offset);
            }
        }

        return new MqttPubRelPacket(packetIdentifier, reasonCode);
    }

    private static MqttPacket ReadPubComp(ReadOnlySpan<byte> packetBytes, int offset, int end, MqttProtocolVersion protocolVersion)
    {
        var packetIdentifier = ReadUInt16(packetBytes, ref offset);
        var reasonCode = (byte)0;
        if (protocolVersion == MqttProtocolVersion.V500 && offset < end)
        {
            reasonCode = packetBytes[offset++];
            if (offset < end)
            {
                SkipProperties(packetBytes, ref offset);
            }
        }

        return new MqttPubCompPacket(packetIdentifier, reasonCode);
    }

    private static MqttPacket ReadSubscribe(ReadOnlySpan<byte> packetBytes, int offset, int end, MqttProtocolVersion protocolVersion)
    {
        var packetIdentifier = ReadUInt16(packetBytes, ref offset);
        if (protocolVersion == MqttProtocolVersion.V500)
        {
            SkipProperties(packetBytes, ref offset);
        }

        var subscriptions = new List<MqttSubscription>();
        while (offset < end)
        {
            var topicFilter = ReadString(packetBytes, ref offset);
            var options = packetBytes[offset++];
            subscriptions.Add(new MqttSubscription(topicFilter, (MqttQualityOfService)(options & 0x03)));
        }

        return new MqttSubscribePacket(packetIdentifier, subscriptions);
    }

    private static MqttPacket ReadSubAck(ReadOnlySpan<byte> packetBytes, int offset, int end, MqttProtocolVersion protocolVersion)
    {
        var packetIdentifier = ReadUInt16(packetBytes, ref offset);
        if (protocolVersion == MqttProtocolVersion.V500)
        {
            SkipProperties(packetBytes, ref offset);
        }

        var reasonCodes = new List<byte>();
        while (offset < end)
        {
            reasonCodes.Add(packetBytes[offset++]);
        }

        return new MqttSubAckPacket(packetIdentifier, reasonCodes);
    }

    private static MqttPacket ReadDisconnect(ReadOnlySpan<byte> packetBytes, int offset, int end, MqttProtocolVersion protocolVersion)
    {
        if (offset >= end)
        {
            return new MqttDisconnectPacket();
        }

        if (protocolVersion == MqttProtocolVersion.V311)
        {
            return new MqttDisconnectPacket();
        }

        var reasonCode = packetBytes[offset++];
        if (offset < end)
        {
            SkipProperties(packetBytes, ref offset);
        }

        return new MqttDisconnectPacket(reasonCode);
    }

    private static void WriteString(List<byte> target, string value)
    {
        WriteString(target, Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private static void WriteString(List<byte> target, byte[] value)
    {
        WriteUInt16(target, (ushort)value.Length);
        target.AddRange(value);
    }

    private static void WriteBinary(List<byte> target, ReadOnlyMemory<byte> value)
    {
        WriteUInt16(target, (ushort)value.Length);
        if (!value.IsEmpty)
        {
            target.AddRange(value.ToArray());
        }
    }

    private static string ReadString(ReadOnlySpan<byte> source, ref int offset)
    {
        var length = ReadUInt16(source, ref offset);
        var value = Encoding.UTF8.GetString(source.Slice(offset, length).ToArray());
        offset += length;
        return value;
    }

    private static ReadOnlyMemory<byte> ReadBinary(ReadOnlySpan<byte> source, ref int offset)
    {
        var length = ReadUInt16(source, ref offset);
        if (length == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        var value = source.Slice(offset, length).ToArray();
        offset += length;
        return value;
    }

    private static void WriteUInt16(List<byte> target, ushort value)
    {
        target.Add((byte)(value >> 8));
        target.Add((byte)value);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> source, ref int offset)
    {
        var value = (ushort)((source[offset] << 8) | source[offset + 1]);
        offset += 2;
        return value;
    }

    private static void SkipProperties(ReadOnlySpan<byte> source, ref int offset)
    {
        var propertyLength = ReadVariableByteInteger(source, ref offset);
        offset += propertyLength;
    }

    private static int ReadVariableByteInteger(ReadOnlySpan<byte> source, ref int offset)
    {
        var multiplier = 1;
        var value = 0;
        byte encodedByte;

        do
        {
            encodedByte = source[offset++];
            value += (encodedByte & 0x7F) * multiplier;
            multiplier *= 128;
            if (multiplier > 128 * 128 * 128 * 128)
            {
                throw new InvalidOperationException("Malformed variable byte integer.");
            }
        }
        while ((encodedByte & 0x80) != 0);

        return value;
    }

    private static int GetVariableByteIntegerSize(int value)
    {
        if (value < 128)
        {
            return 1;
        }

        if (value < 16384)
        {
            return 2;
        }

        if (value < 2097152)
        {
            return 3;
        }

        return 4;
    }

    private static int WriteVariableByteInteger(byte[] target, int offset, int value)
    {
        var count = 0;
        do
        {
            var encodedByte = value % 128;
            value /= 128;
            if (value > 0)
            {
                encodedByte |= 0x80;
            }

            target[offset + count] = (byte)encodedByte;
            count++;
        }
        while (value > 0);

        return count;
    }
}
