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
