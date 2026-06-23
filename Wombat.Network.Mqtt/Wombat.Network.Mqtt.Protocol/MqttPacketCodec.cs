using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace Wombat.Network.Mqtt.Protocol;

public sealed class MqttPacketCodec
{
    private static readonly byte[] ProtocolName = Encoding.UTF8.GetBytes("MQTT");

    public byte[] Encode(MqttPacket packet)
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
                WriteConnAck(body, connAck);
                break;
            case MqttPublishPacket publish:
                WritePublish(body, publish);
                break;
            case MqttPubAckPacket pubAck:
                WritePubAck(body, pubAck);
                break;
            case MqttSubscribePacket subscribe:
                WriteSubscribe(body, subscribe);
                break;
            case MqttSubAckPacket subAck:
                WriteSubAck(body, subAck);
                break;
            case MqttPingReqPacket _:
                break;
            case MqttPingRespPacket _:
                break;
            case MqttDisconnectPacket disconnect:
                WriteDisconnect(body, disconnect);
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
                return ReadConnAck(packetBytes, offset);
            case MqttPacketType.Publish:
                return ReadPublish(packetBytes, offset, end, flags);
            case MqttPacketType.PubAck:
                return ReadPubAck(packetBytes, offset, end);
            case MqttPacketType.Subscribe:
                return ReadSubscribe(packetBytes, offset, end);
            case MqttPacketType.SubAck:
                return ReadSubAck(packetBytes, offset, end);
            case MqttPacketType.PingReq:
                return new MqttPingReqPacket();
            case MqttPacketType.PingResp:
                return new MqttPingRespPacket();
            case MqttPacketType.Disconnect:
                return ReadDisconnect(packetBytes, offset, end);
            default:
                throw new NotSupportedException("Unsupported packet type: " + packetType);
        }
    }

    private static void WriteConnect(List<byte> body, MqttConnectPacket packet)
    {
        WriteString(body, ProtocolName);
        body.Add(5);
        body.Add(packet.CleanStart ? (byte)0x02 : (byte)0x00);
        WriteUInt16(body, packet.KeepAliveSeconds);
        body.Add(0);
        WriteString(body, packet.ClientId);
    }

    private static void WriteConnAck(List<byte> body, MqttConnAckPacket packet)
    {
        body.Add(packet.SessionPresent ? (byte)0x01 : (byte)0x00);
        body.Add(packet.ReasonCode);
        body.Add(0);
    }

    private static void WritePublish(List<byte> body, MqttPublishPacket packet)
    {
        WriteString(body, packet.Topic);
        if (packet.QualityOfService != MqttQualityOfService.AtMostOnce)
        {
            WriteUInt16(body, packet.PacketIdentifier);
        }

        body.Add(0);
        if (!packet.Payload.IsEmpty)
        {
            body.AddRange(packet.Payload.ToArray());
        }
    }

    private static void WritePubAck(List<byte> body, MqttPubAckPacket packet)
    {
        WriteUInt16(body, packet.PacketIdentifier);
        body.Add(packet.ReasonCode);
        body.Add(0);
    }

    private static void WriteSubscribe(List<byte> body, MqttSubscribePacket packet)
    {
        WriteUInt16(body, packet.PacketIdentifier);
        body.Add(0);
        for (var i = 0; i < packet.Subscriptions.Count; i++)
        {
            var subscription = packet.Subscriptions[i];
            WriteString(body, subscription.TopicFilter);
            body.Add((byte)subscription.QualityOfService);
        }
    }

    private static void WriteSubAck(List<byte> body, MqttSubAckPacket packet)
    {
        WriteUInt16(body, packet.PacketIdentifier);
        body.Add(0);
        for (var i = 0; i < packet.ReasonCodes.Count; i++)
        {
            body.Add(packet.ReasonCodes[i]);
        }
    }

    private static void WriteDisconnect(List<byte> body, MqttDisconnectPacket packet)
    {
        body.Add(packet.ReasonCode);
        body.Add(0);
    }

    private static MqttPacket ReadConnect(ReadOnlySpan<byte> packetBytes, int offset)
    {
        var protocolName = ReadString(packetBytes, ref offset);
        if (!string.Equals(protocolName, "MQTT", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported protocol name.");
        }

        var version = packetBytes[offset++];
        if (version != 5)
        {
            throw new InvalidOperationException("Unsupported MQTT version.");
        }

        var flags = packetBytes[offset++];
        var keepAlive = ReadUInt16(packetBytes, ref offset);
        SkipProperties(packetBytes, ref offset);
        var clientId = ReadString(packetBytes, ref offset);
        return new MqttConnectPacket(clientId, (flags & 0x02) != 0, keepAlive);
    }

    private static MqttPacket ReadConnAck(ReadOnlySpan<byte> packetBytes, int offset)
    {
        var acknowledgeFlags = packetBytes[offset++];
        var reasonCode = packetBytes[offset++];
        SkipProperties(packetBytes, ref offset);
        return new MqttConnAckPacket(reasonCode, (acknowledgeFlags & 0x01) != 0);
    }

    private static MqttPacket ReadPublish(ReadOnlySpan<byte> packetBytes, int offset, int end, byte flags)
    {
        var topic = ReadString(packetBytes, ref offset);
        var qos = (MqttQualityOfService)((flags >> 1) & 0x03);
        ushort packetIdentifier = 0;
        if (qos != MqttQualityOfService.AtMostOnce)
        {
            packetIdentifier = ReadUInt16(packetBytes, ref offset);
        }

        SkipProperties(packetBytes, ref offset);
        var payloadLength = end - offset;
        var payload = payloadLength == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(packetBytes.Slice(offset, payloadLength).ToArray());
        return new MqttPublishPacket(topic, payload, qos, packetIdentifier, (flags & 0x01) != 0, (flags & 0x08) != 0);
    }

    private static MqttPacket ReadPubAck(ReadOnlySpan<byte> packetBytes, int offset, int end)
    {
        var packetIdentifier = ReadUInt16(packetBytes, ref offset);
        var reasonCode = offset < end ? packetBytes[offset++] : (byte)0;
        if (offset < end)
        {
            SkipProperties(packetBytes, ref offset);
        }

        return new MqttPubAckPacket(packetIdentifier, reasonCode);
    }

    private static MqttPacket ReadSubscribe(ReadOnlySpan<byte> packetBytes, int offset, int end)
    {
        var packetIdentifier = ReadUInt16(packetBytes, ref offset);
        SkipProperties(packetBytes, ref offset);
        var subscriptions = new List<MqttSubscription>();
        while (offset < end)
        {
            var topicFilter = ReadString(packetBytes, ref offset);
            var options = packetBytes[offset++];
            subscriptions.Add(new MqttSubscription(topicFilter, (MqttQualityOfService)(options & 0x03)));
        }

        return new MqttSubscribePacket(packetIdentifier, subscriptions);
    }

    private static MqttPacket ReadSubAck(ReadOnlySpan<byte> packetBytes, int offset, int end)
    {
        var packetIdentifier = ReadUInt16(packetBytes, ref offset);
        SkipProperties(packetBytes, ref offset);
        var reasonCodes = new List<byte>();
        while (offset < end)
        {
            reasonCodes.Add(packetBytes[offset++]);
        }

        return new MqttSubAckPacket(packetIdentifier, reasonCodes);
    }

    private static MqttPacket ReadDisconnect(ReadOnlySpan<byte> packetBytes, int offset, int end)
    {
        if (offset >= end)
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

    private static string ReadString(ReadOnlySpan<byte> source, ref int offset)
    {
        var length = ReadUInt16(source, ref offset);
        var value = Encoding.UTF8.GetString(source.Slice(offset, length).ToArray());
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
