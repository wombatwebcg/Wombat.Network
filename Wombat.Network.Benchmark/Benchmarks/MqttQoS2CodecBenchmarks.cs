using BenchmarkDotNet.Attributes;
using Wombat.Network.Mqtt.Protocol;

namespace Wombat.Network.Benchmark.Benchmarks;

[Config(typeof(BenchmarkConfig))]
public class MqttQoS2CodecBenchmarks
{
    private MqttPacketCodec _codec = null!;
    private MqttPublishPacket _publish = null!;
    private byte[] _publishEncoded = null!;
    private byte[] _pubRelEncoded = null!;

    [ParamsSource(nameof(PayloadSizes))]
    public int PayloadSize { get; set; }

    public IEnumerable<int> PayloadSizes =>
        BenchmarkConfig.QuickMode ? [128, 4096] : [32, 128, 4096, 32768];

    [GlobalSetup]
    public void Setup()
    {
        _codec = new MqttPacketCodec();
        _publish = new MqttPublishPacket("bench/qos2", BenchmarkData.CreatePayload(PayloadSize), MqttQualityOfService.ExactlyOnce, 17);
        _publishEncoded = _codec.Encode(_publish);
        _pubRelEncoded = _codec.Encode(new MqttPubRelPacket(17));
    }

    [Benchmark]
    public byte[] EncodeQoS2Publish()
        => _codec.Encode(_publish);

    [Benchmark]
    public MqttPacket DecodeQoS2Publish()
        => _codec.Decode(_publishEncoded);

    [Benchmark]
    public MqttPacket DecodePubRel()
        => _codec.Decode(_pubRelEncoded);
}
