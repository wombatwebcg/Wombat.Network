using BenchmarkDotNet.Attributes;
using Wombat.Network.Mqtt.Protocol;

namespace Wombat.Network.Benchmark.Benchmarks;

[Config(typeof(BenchmarkConfig))]
public class MqttPacketCodecBenchmarks
{
    private MqttPacketCodec _codec = null!;
    private MqttPublishPacket _packet = null!;
    private byte[] _encoded = null!;

    [ParamsSource(nameof(PayloadSizes))]
    public int PayloadSize { get; set; }

    public IEnumerable<int> PayloadSizes =>
        BenchmarkConfig.QuickMode ? [128, 4096] : [32, 128, 4096, 32768];

    [GlobalSetup]
    public void Setup()
    {
        _codec = new MqttPacketCodec();
        _packet = new MqttPublishPacket("bench/topic", BenchmarkData.CreatePayload(PayloadSize), MqttQualityOfService.AtLeastOnce, 7);
        _encoded = _codec.Encode(_packet);
    }

    [Benchmark]
    public byte[] EncodePublish()
        => _codec.Encode(_packet);

    [Benchmark]
    public MqttPacket DecodePublish()
        => _codec.Decode(_encoded);
}
