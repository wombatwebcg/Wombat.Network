using System.Globalization;
using System.Text;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Parameters;
using BenchmarkDotNet.Reports;
using Wombat.Network.Protocols.Framing;

namespace Wombat.Network.Benchmark;

internal static class BenchmarkReportWriter
{
    public static void WriteToConsole(IReadOnlyList<Summary> summaries)
    {
        var rows = GetRows(summaries);
        Console.WriteLine();
        Console.WriteLine("Benchmark module summary");

        if (rows.Length == 0)
        {
            Console.WriteLine("No successful benchmark results.");
            return;
        }

        foreach (var module in rows.GroupBy(row => row.Module))
        {
            var moduleRows = module.ToArray();
            Console.WriteLine(
                $"{module.Key}: cases={moduleRows.Length}, avg={FormatNanoseconds(moduleRows.Average(row => row.MeanNs))}, ops={FormatOpsPerSecond(moduleRows.Average(row => row.OpsPerSecond))}, msg={FormatMessagesPerSecond(moduleRows.Average(row => row.MessagesPerSecond))}, io={FormatMegabytesPerSecond(moduleRows.Average(row => row.MegabytesPerSecond))}");
        }
    }

    public static string Write(IReadOnlyList<Summary> summaries)
    {
        if (summaries.Count == 0)
        {
            throw new InvalidOperationException("No benchmark summaries were produced.");
        }

        var resultsDirectory = summaries[0].ResultsDirectoryPath;
        Directory.CreateDirectory(resultsDirectory);

        var reportPath = Path.Combine(
            resultsDirectory,
            $"benchmark-report-{DateTime.Now:yyyyMMdd-HHmmss}.md");

        File.WriteAllText(reportPath, BuildMarkdown(summaries), Encoding.UTF8);
        return reportPath;
    }

    private static string BuildMarkdown(IReadOnlyList<Summary> summaries)
    {
        var rows = GetRows(summaries);

        var builder = new StringBuilder();
        builder.AppendLine("# Benchmark Report");
        builder.AppendLine();
        builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine();

        if (rows.Length == 0)
        {
            builder.AppendLine("No successful benchmark results.");
            return builder.ToString();
        }

        builder.AppendLine("## Module Summary");
        builder.AppendLine();
        builder.AppendLine("| Module | Cases | Avg Mean | Avg Ops | Avg Msg | Avg MB/s | Fastest | Slowest |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var module in rows.GroupBy(row => row.Module))
        {
            var moduleRows = module.ToArray();
            builder.Append("| ")
                .Append(module.Key)
                .Append(" | ")
                .Append(moduleRows.Length)
                .Append(" | ")
                .Append(FormatNanoseconds(moduleRows.Average(row => row.MeanNs)))
                .Append(" | ")
                .Append(FormatOpsPerSecond(moduleRows.Average(row => row.OpsPerSecond)))
                .Append(" | ")
                .Append(FormatMessagesPerSecond(moduleRows.Average(row => row.MessagesPerSecond)))
                .Append(" | ")
                .Append(FormatMegabytesPerSecond(moduleRows.Average(row => row.MegabytesPerSecond)))
                .Append(" | ")
                .Append(FormatNanoseconds(moduleRows.Min(row => row.MeanNs)))
                .Append(" | ")
                .Append(FormatNanoseconds(moduleRows.Max(row => row.MeanNs)))
                .AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("## Benchmark Details");
        builder.AppendLine();
        builder.AppendLine("| Module | Benchmark | Mean | StdDev | P95 | P99 | Min | Max | Ops | Msg/s | MB/s |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var row in rows)
        {
            builder.Append("| ")
                .Append(row.Module)
                .Append(" | ")
                .Append(row.Benchmark)
                .Append(" | ")
                .Append(FormatNanoseconds(row.MeanNs))
                .Append(" | ")
                .Append(FormatNanoseconds(row.StdDevNs))
                .Append(" | ")
                .Append(FormatNanoseconds(row.P95Ns))
                .Append(" | ")
                .Append(FormatNanoseconds(row.P99Ns))
                .Append(" | ")
                .Append(FormatNanoseconds(row.MinNs))
                .Append(" | ")
                .Append(FormatNanoseconds(row.MaxNs))
                .Append(" | ")
                .Append(FormatOpsPerSecond(row.OpsPerSecond))
                .Append(" | ")
                .Append(FormatMessagesPerSecond(row.MessagesPerSecond))
                .Append(" | ")
                .Append(FormatMegabytesPerSecond(row.MegabytesPerSecond))
                .AppendLine(" |");
        }

        return builder.ToString();
    }

    private static BenchmarkRow[] GetRows(IReadOnlyList<Summary> summaries) =>
        summaries
            .SelectMany(summary => summary.Reports)
            .Where(report => report.Success && report.ResultStatistics is not null)
            .Select(CreateRow)
            .OrderBy(row => row.Module)
            .ThenBy(row => row.Benchmark)
            .ToArray();

    private static BenchmarkRow CreateRow(BenchmarkReport report)
    {
        var benchmarkCase = report.BenchmarkCase;
        var stats = report.ResultStatistics!;
        var benchmarkType = benchmarkCase.Descriptor.Type.Name;
        var benchmarkMethod = benchmarkCase.Descriptor.WorkloadMethod.Name;
        var benchmarkName = benchmarkMethod;
        var parameterText = benchmarkCase.Parameters.DisplayInfo;
        var workloadUnits = EstimateWorkloadUnits(benchmarkType, benchmarkMethod, benchmarkCase.Parameters);
        var workloadSamples = GetWorkloadSamples(report);
        var opsPerSecond = stats.Mean <= 0 ? 0 : 1_000_000_000d / stats.Mean;

        if (!string.IsNullOrWhiteSpace(parameterText))
        {
            benchmarkName = $"{benchmarkName} [{parameterText}]";
        }

        return new BenchmarkRow(
            benchmarkType.Replace("Benchmarks", string.Empty, StringComparison.Ordinal),
            benchmarkName,
            stats.Mean,
            stats.StandardDeviation,
            GetPercentile(workloadSamples, 0.95),
            GetPercentile(workloadSamples, 0.99),
            stats.Min,
            stats.Max,
            opsPerSecond,
            opsPerSecond * workloadUnits.MessagesPerOperation,
            opsPerSecond * workloadUnits.BytesPerOperation / (1024d * 1024d));
    }

    private static double[] GetWorkloadSamples(BenchmarkReport report) =>
        report.AllMeasurements
            .Where(measurement => measurement.IterationMode == IterationMode.Workload && measurement.IterationStage == IterationStage.Result)
            .Select(measurement => measurement.Operations <= 0 ? 0d : measurement.Nanoseconds / measurement.Operations)
            .Where(sample => sample > 0)
            .OrderBy(sample => sample)
            .ToArray();

    private static double GetPercentile(double[] sortedSamples, double percentile)
    {
        if (sortedSamples.Length == 0)
        {
            return 0;
        }

        if (sortedSamples.Length == 1)
        {
            return sortedSamples[0];
        }

        var position = percentile * (sortedSamples.Length - 1);
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return sortedSamples[lowerIndex];
        }

        var weight = position - lowerIndex;
        return sortedSamples[lowerIndex] + (sortedSamples[upperIndex] - sortedSamples[lowerIndex]) * weight;
    }

    private static WorkloadUnits EstimateWorkloadUnits(string benchmarkType, string benchmarkMethod, ParameterInstances parameters)
    {
        var payloadSize = GetIntParameter(parameters, "PayloadSize");
        var clientCount = GetIntParameter(parameters, "ClientCount", 1);

        return benchmarkType switch
        {
            "TcpChannelBenchmarks" => benchmarkMethod switch
            {
                "SingleClientRoundTrip" => new WorkloadUnits(2, payloadSize * 2d),
                "ConcurrentClientsRoundTrip" => new WorkloadUnits(clientCount * 2d, payloadSize * clientCount * 2d),
                "SingleClientBurst100" => new WorkloadUnits(100, payloadSize * 100d),
                _ => new WorkloadUnits(1, payloadSize)
            },
            "UdpChannelBenchmarks" => benchmarkMethod switch
            {
                "SingleDatagramRoundTrip" => new WorkloadUnits(2, payloadSize * 2d),
                "ConcurrentClientsSend" => new WorkloadUnits(clientCount, payloadSize * clientCount),
                "SingleClientBurst100" => new WorkloadUnits(100, payloadSize * 100d),
                _ => new WorkloadUnits(1, payloadSize)
            },
            "WebSocketChannelBenchmarks" => benchmarkMethod switch
            {
                "BinaryRoundTrip" => new WorkloadUnits(2, payloadSize * 2d),
                "TextRoundTrip" => new WorkloadUnits(2, payloadSize * 2d),
                "ConcurrentClientsBinaryRoundTrip" => new WorkloadUnits(clientCount * 2d, payloadSize * clientCount * 2d),
                _ => new WorkloadUnits(1, payloadSize)
            },
            "WebSocketFrameCodecBenchmarks" => EstimateWebSocketFrameCodecUnits(benchmarkMethod, parameters, payloadSize),
            "LengthFieldMessagePipeBenchmarks" => EstimateLengthFieldUnits(benchmarkMethod, parameters, payloadSize),
            _ => new WorkloadUnits(1, payloadSize)
        };
    }

    private static WorkloadUnits EstimateWebSocketFrameCodecUnits(string benchmarkMethod, ParameterInstances parameters, int payloadSize)
    {
        var masked = GetBoolParameter(parameters, "Masked");
        var frameSize = payloadSize + GetWebSocketFrameHeaderSize(payloadSize, masked);

        return benchmarkMethod switch
        {
            "EncodeBinary" => new WorkloadUnits(1, frameSize),
            "EncodeText" => new WorkloadUnits(1, frameSize),
            "TryDecodeBinary" => new WorkloadUnits(1, frameSize),
            "TryDecodeText" => new WorkloadUnits(1, frameSize),
            _ => new WorkloadUnits(1, payloadSize)
        };
    }

    private static WorkloadUnits EstimateLengthFieldUnits(string benchmarkMethod, ParameterInstances parameters, int payloadSize)
    {
        var lengthField = GetEnumParameter(parameters, "LengthField", LengthField.FourBytes);
        var frameSize = payloadSize + GetLengthFieldSize(lengthField);

        return benchmarkMethod switch
        {
            "WriteAsync" => new WorkloadUnits(1, frameSize),
            "TryReadSingleFrame" => new WorkloadUnits(1, frameSize),
            "TryReadMultiFrame" => new WorkloadUnits(1000, frameSize * 1000d),
            _ => new WorkloadUnits(1, payloadSize)
        };
    }

    private static int GetIntParameter(ParameterInstances parameters, string name, int defaultValue = 0) =>
        parameters[name] is int value ? value : defaultValue;

    private static bool GetBoolParameter(ParameterInstances parameters, string name, bool defaultValue = false) =>
        parameters[name] is bool value ? value : defaultValue;

    private static TEnum GetEnumParameter<TEnum>(ParameterInstances parameters, string name, TEnum defaultValue)
        where TEnum : struct, Enum =>
        parameters[name] is TEnum value ? value : defaultValue;

    private static int GetLengthFieldSize(LengthField lengthField) =>
        lengthField switch
        {
            LengthField.OneByte => 1,
            LengthField.TwoBytes => 2,
            LengthField.FourBytes => 4,
            LengthField.EigthBytes => 8,
            _ => 4
        };

    private static int GetWebSocketFrameHeaderSize(int payloadSize, bool masked)
    {
        var baseHeader = payloadSize switch
        {
            <= 125 => 2,
            <= ushort.MaxValue => 4,
            _ => 10
        };

        return baseHeader + (masked ? 4 : 0);
    }

    private static string FormatNanoseconds(double nanoseconds)
    {
        const double nsPerMicrosecond = 1_000d;
        const double nsPerMillisecond = 1_000_000d;
        const double nsPerSecond = 1_000_000_000d;

        return nanoseconds switch
        {
            >= nsPerSecond => $"{(nanoseconds / nsPerSecond).ToString("F3", CultureInfo.InvariantCulture)} s",
            >= nsPerMillisecond => $"{(nanoseconds / nsPerMillisecond).ToString("F3", CultureInfo.InvariantCulture)} ms",
            >= nsPerMicrosecond => $"{(nanoseconds / nsPerMicrosecond).ToString("F3", CultureInfo.InvariantCulture)} us",
            _ => $"{nanoseconds.ToString("F3", CultureInfo.InvariantCulture)} ns"
        };
    }

    private static string FormatOpsPerSecond(double opsPerSecond) =>
        $"{opsPerSecond.ToString("N2", CultureInfo.InvariantCulture)} ops/s";

    private static string FormatMessagesPerSecond(double messagesPerSecond) =>
        $"{messagesPerSecond.ToString("N2", CultureInfo.InvariantCulture)} msg/s";

    private static string FormatMegabytesPerSecond(double megabytesPerSecond) =>
        $"{megabytesPerSecond.ToString("N2", CultureInfo.InvariantCulture)} MB/s";

    private sealed record BenchmarkRow(
        string Module,
        string Benchmark,
        double MeanNs,
        double StdDevNs,
        double P95Ns,
        double P99Ns,
        double MinNs,
        double MaxNs,
        double OpsPerSecond,
        double MessagesPerSecond,
        double MegabytesPerSecond);

    private sealed record WorkloadUnits(
        double MessagesPerOperation,
        double BytesPerOperation);
}
