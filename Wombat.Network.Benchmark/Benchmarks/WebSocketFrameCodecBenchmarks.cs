using System.Buffers;
using BenchmarkDotNet.Attributes;
using Wombat.Network.Protocols.WebSocket;

namespace Wombat.Network.Benchmark.Benchmarks;

[Config(typeof(BenchmarkConfig))]
public class WebSocketFrameCodecBenchmarks
{
    private WebSocketFrameCodec _codec = null!;
    private byte[] _payload = null!;
    private string _text = null!;
    private byte[] _binaryFrame = null!;
    private byte[] _textFrame = null!;

    [ParamsSource(nameof(PayloadSizes))]
    public int PayloadSize { get; set; }

    [ParamsSource(nameof(MaskedValues))]
    public bool Masked { get; set; }

    public IEnumerable<int> PayloadSizes =>
        BenchmarkConfig.QuickMode ? [512, 4096] : [64, 512, 4096, 32768];

    public IEnumerable<bool> MaskedValues =>
        BenchmarkConfig.QuickMode ? [false] : [true, false];

    [GlobalSetup]
    public void Setup()
    {
        _codec = new WebSocketFrameCodec();
        _payload = BenchmarkData.CreatePayload(PayloadSize);
        _text = BenchmarkData.CreateText(PayloadSize);
        _binaryFrame = BenchmarkData.CreateWebSocketBinaryFrame(PayloadSize, Masked);
        _textFrame = BenchmarkData.CreateWebSocketTextFrame(PayloadSize, Masked);
    }

    [Benchmark]
    public int EncodeBinary() => _codec.EncodeBinary(_payload, Masked).Length;

    [Benchmark]
    public int EncodeText() => _codec.EncodeText(_text, Masked).Length;

    [Benchmark]
    public int TryDecodeBinary()
    {
        var buffer = new ReadOnlySequence<byte>(_binaryFrame);
        return _codec.TryDecode(ref buffer, out var message) ? (int)message.Payload.Length : -1;
    }

    [Benchmark]
    public int TryDecodeText()
    {
        var buffer = new ReadOnlySequence<byte>(_textFrame);
        return _codec.TryDecode(ref buffer, out var message) ? (int)message.Payload.Length : -1;
    }
}
