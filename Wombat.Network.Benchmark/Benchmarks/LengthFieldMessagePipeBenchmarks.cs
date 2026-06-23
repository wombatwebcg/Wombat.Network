using System;
using System.Buffers;
using System.IO.Pipelines;
using BenchmarkDotNet.Attributes;
using Wombat.Network.Protocols.Framing;

namespace Wombat.Network.Benchmark.Benchmarks;

[Config(typeof(BenchmarkConfig))]
public class LengthFieldMessagePipeBenchmarks
{
    private Pipe _pipe = null!;
    private byte[] _payload = null!;
    private byte[] _singleFrame = null!;
    private byte[] _multiFrame = null!;
    private LengthFieldMessagePipe _codec = null!;

    [ParamsSource(nameof(PayloadSizes))]
    public int PayloadSize { get; set; }

    [ParamsSource(nameof(LengthFields))]
    public LengthField LengthField { get; set; }

    public IEnumerable<int> PayloadSizes =>
        BenchmarkConfig.QuickMode ? [512, 4096] : [64, 512, 4096, 32768];

    public IEnumerable<LengthField> LengthFields =>
        BenchmarkConfig.QuickMode
            ? [LengthField.TwoBytes, LengthField.FourBytes]
            : [LengthField.OneByte, LengthField.TwoBytes, LengthField.FourBytes, LengthField.EigthBytes];

    [GlobalSetup]
    public void Setup()
    {
        if (LengthField == LengthField.OneByte && PayloadSize > byte.MaxValue)
        {
            throw new InvalidOperationException("OneByte length field only supports payload <= 255.");
        }

        if (LengthField == LengthField.TwoBytes && PayloadSize > ushort.MaxValue)
        {
            throw new InvalidOperationException("TwoBytes length field only supports payload <= 65535.");
        }

        _codec = new LengthFieldMessagePipe(LengthField);
        _payload = BenchmarkData.CreatePayload(PayloadSize);
        _singleFrame = BenchmarkData.CreateLengthFieldFrame(LengthField, PayloadSize, 1);
        _multiFrame = BenchmarkData.CreateLengthFieldFrame(LengthField, PayloadSize, 1000);
        _pipe = new Pipe();
    }

    [IterationSetup(Target = nameof(WriteAsync))]
    public void SetupWrite() => _pipe = new Pipe();

    [Benchmark]
    public void WriteAsync()
    {
        _codec.WriteAsync(_pipe.Writer, _payload).GetAwaiter().GetResult();
        _pipe.Writer.Complete();

        var result = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();
        _pipe.Reader.AdvanceTo(result.Buffer.End);
        _pipe.Reader.Complete();
    }

    [Benchmark]
    public int TryReadSingleFrame()
    {
        var buffer = new ReadOnlySequence<byte>(_singleFrame);
        return _codec.TryRead(ref buffer, out var payload) ? (int)payload.Length : -1;
    }

    [Benchmark]
    public int TryReadMultiFrame()
    {
        var buffer = new ReadOnlySequence<byte>(_multiFrame);
        var count = 0;

        while (_codec.TryRead(ref buffer, out _))
        {
            count++;
        }

        return count;
    }
}
