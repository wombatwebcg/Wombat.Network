using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;

namespace Wombat.Network.Benchmark;

public sealed class BenchmarkConfig : ManualConfig
{
    public static bool QuickMode { get; private set; }

    public BenchmarkConfig()
        : this(quickMode: false)
    {
    }

    public BenchmarkConfig(bool quickMode)
    {
        QuickMode = quickMode;
        AddJob(quickMode ? CreateQuickJob() : Job.Default);
        AddLogger(ConsoleLogger.Default);
        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(HtmlExporter.Default);
        WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));
    }

    private static Job CreateQuickJob() =>
        Job.Default
            .WithId("Quick")
            .WithStrategy(RunStrategy.Throughput)
            .WithLaunchCount(1)
            .WithWarmupCount(1)
            .WithIterationCount(3);
}
