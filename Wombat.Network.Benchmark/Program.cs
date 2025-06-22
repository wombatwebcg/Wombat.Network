using BenchmarkDotNet.Running;
using Wombat.Network.Benchmark;

namespace Wombat.Network.Benchmark
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // 运行所有基准测试
            var summary = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
