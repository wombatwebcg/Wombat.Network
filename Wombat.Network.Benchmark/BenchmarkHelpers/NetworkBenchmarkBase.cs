using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Wombat.Network.Benchmark.BenchmarkHelpers;

public abstract class NetworkBenchmarkBase
{
    protected byte[] GenerateTestData(int size)
    {
        var data = new byte[size];
        new Random(42).NextBytes(data);
        return data;
    }

    protected int GetAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint).Port;
    }

    protected async Task<bool> WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return false;
    }
}
