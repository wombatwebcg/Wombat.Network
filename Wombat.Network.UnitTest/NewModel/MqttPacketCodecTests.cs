using FluentAssertions;
using System;
using System.Buffers;
using System.Text;
using Wombat.Network.Mqtt.Protocol;
using Xunit;

namespace Wombat.Network.UnitTest.NewModel;

public class MqttPacketCodecTests
{
    [Fact]
    public void EncodeDecode_Connect_ShouldRoundTrip()
    {
        var codec = new MqttPacketCodec();
        var packet = new MqttConnectPacket("client-a", true, 45);

        var encoded = codec.Encode(packet);
        var decoded = codec.Decode(encoded);

        decoded.Should().BeOfType<MqttConnectPacket>();
        var connect = (MqttConnectPacket)decoded;
        connect.ClientId.Should().Be("client-a");
        connect.CleanStart.Should().BeTrue();
        connect.KeepAliveSeconds.Should().Be(45);
    }

    [Fact]
    public void EncodeDecode_Publish_ShouldRoundTrip()
    {
        var codec = new MqttPacketCodec();
        var packet = new MqttPublishPacket("demo/topic", Encoding.UTF8.GetBytes("payload"), MqttQualityOfService.AtLeastOnce, 9, retain: true);

        var encoded = codec.Encode(packet);
        var decoded = (MqttPublishPacket)codec.Decode(encoded);

        decoded.Topic.Should().Be("demo/topic");
        decoded.PacketIdentifier.Should().Be(9);
        decoded.Retain.Should().BeTrue();
        Encoding.UTF8.GetString(decoded.Payload.ToArray()).Should().Be("payload");
    }

    [Fact]
    public void EncodeDecode_ConnectWithWill_ShouldRoundTrip()
    {
        var codec = new MqttPacketCodec();
        var packet = new MqttConnectPacket("client-a", true, 45, new MqttPublishPacket("client/will", Encoding.UTF8.GetBytes("bye"), MqttQualityOfService.AtLeastOnce, retain: true));

        var encoded = codec.Encode(packet);
        var decoded = (MqttConnectPacket)codec.Decode(encoded);

        decoded.WillMessage.Should().NotBeNull();
        decoded.WillMessage.Topic.Should().Be("client/will");
        decoded.WillMessage.QualityOfService.Should().Be(MqttQualityOfService.AtLeastOnce);
        decoded.WillMessage.Retain.Should().BeTrue();
        Encoding.UTF8.GetString(decoded.WillMessage.Payload.ToArray()).Should().Be("bye");
    }

    [Fact]
    public void EncodeDecode_ConnectV311_ShouldRoundTrip()
    {
        var codec = new MqttPacketCodec();
        var packet = new MqttConnectPacket("client-311", false, 60, protocolVersion: MqttProtocolVersion.V311);

        var encoded = codec.Encode(packet);
        var decoded = (MqttConnectPacket)codec.Decode(encoded);

        decoded.ClientId.Should().Be("client-311");
        decoded.CleanStart.Should().BeFalse();
        decoded.KeepAliveSeconds.Should().Be(60);
        decoded.ProtocolVersion.Should().Be(MqttProtocolVersion.V311);
    }

    [Fact]
    public void EncodeDecode_QoS2Acks_ShouldRoundTrip()
    {
        var codec = new MqttPacketCodec();

        ((MqttPubRecPacket)codec.Decode(codec.Encode(new MqttPubRecPacket(9)))).PacketIdentifier.Should().Be(9);
        ((MqttPubRelPacket)codec.Decode(codec.Encode(new MqttPubRelPacket(10)))).PacketIdentifier.Should().Be(10);
        ((MqttPubCompPacket)codec.Decode(codec.Encode(new MqttPubCompPacket(11)))).PacketIdentifier.Should().Be(11);
    }

    [Fact]
    public void EncodeDecode_V311Packets_ShouldOmitV500Properties()
    {
        var codec = new MqttPacketCodec();
        var publishBytes = codec.Encode(new MqttPublishPacket("demo/topic", Encoding.UTF8.GetBytes("abc"), MqttQualityOfService.AtLeastOnce, 9, retain: true), MqttProtocolVersion.V311);
        var subAckBytes = codec.Encode(new MqttSubAckPacket(7, new byte[] { 1 }), MqttProtocolVersion.V311);
        var disconnectBytes = codec.Encode(new MqttDisconnectPacket(), MqttProtocolVersion.V311);

        ((MqttPublishPacket)codec.Decode(publishBytes, MqttProtocolVersion.V311)).PacketIdentifier.Should().Be(9);
        ((MqttSubAckPacket)codec.Decode(subAckBytes, MqttProtocolVersion.V311)).PacketIdentifier.Should().Be(7);
        codec.Decode(disconnectBytes, MqttProtocolVersion.V311).Should().BeOfType<MqttDisconnectPacket>();

        publishBytes[1].Should().Be(17);
        subAckBytes[1].Should().Be(3);
        disconnectBytes[1].Should().Be(0);
    }

    [Fact]
    public void TryReadPacketBytes_WithTwoFrames_ShouldReadOneByOne()
    {
        var codec = new MqttPacketCodec();
        var first = codec.Encode(new MqttPingReqPacket());
        var second = codec.Encode(new MqttDisconnectPacket());
        var combined = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, combined, 0, first.Length);
        Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);
        ReadOnlySequence<byte> buffer = new ReadOnlySequence<byte>(combined);

        var firstRead = codec.TryReadPacketBytes(ref buffer, out var firstPacket);
        var secondRead = codec.TryReadPacketBytes(ref buffer, out var secondPacket);

        firstRead.Should().BeTrue();
        secondRead.Should().BeTrue();
        codec.Decode(firstPacket.ToArray()).Should().BeOfType<MqttPingReqPacket>();
        codec.Decode(secondPacket.ToArray()).Should().BeOfType<MqttDisconnectPacket>();
        buffer.Length.Should().Be(0);
    }
}
