using BenchmarkDotNet.Running;
using Wombat.Network.Benchmark;
using Wombat.Network.Benchmark.Benchmarks;

var quickMode = args.Any(static arg => string.Equals(arg, "--quick", StringComparison.OrdinalIgnoreCase));
var benchmarkArgs = args.Where(static arg => !string.Equals(arg, "--quick", StringComparison.OrdinalIgnoreCase)).ToArray();

var summaries = BenchmarkSwitcher.FromTypes(
        [
            typeof(LengthFieldMessagePipeBenchmarks),
            typeof(MqttPacketCodecBenchmarks),
            typeof(WebSocketFrameCodecBenchmarks),
            typeof(TcpChannelBenchmarks),
            typeof(UdpChannelBenchmarks),
            typeof(WebSocketChannelBenchmarks)
        ])
    .Run(benchmarkArgs.Length == 0 ? ["--filter", "*"] : benchmarkArgs, new BenchmarkConfig(quickMode))
    .ToArray();

if (summaries.Length > 0)
{
    BenchmarkReportWriter.WriteToConsole(summaries);
    var reportPath = BenchmarkReportWriter.Write(summaries);
    Console.WriteLine();
    Console.WriteLine($"Benchmark report: {reportPath}");
}
