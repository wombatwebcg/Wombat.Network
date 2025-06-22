using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Wombat.Network.Sockets;
using Wombat.Network.WebSockets;

namespace Wombat.Network.Benchmark.BenchmarkHelpers
{
    /// <summary>
    /// 网络基准测试基类
    /// 提供通用的网络测试工具方法和配置创建方法
    /// </summary>
    public abstract class NetworkBenchmarkBase
    {
        protected readonly Random _random = new();
        protected const int TestPortRangeStart = 40000;
        protected const int TestPortRangeEnd = 45000;

        /// <summary>
        /// 获取可用端口
        /// </summary>
        protected int GetAvailablePort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

        /// <summary>
        /// 检查端口是否可用
        /// </summary>
        protected bool IsPortAvailable(int port)
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 生成测试数据
        /// </summary>
        /// <param name="size">数据大小（字节）</param>
        /// <returns>测试数据</returns>
        protected byte[] GenerateTestData(int size)
        {
            var data = new byte[size];
            var random = new Random(42); // 使用固定种子确保可重复性
            random.NextBytes(data);
            return data;
        }

        /// <summary>
        /// 创建高性能TCP客户端配置
        /// </summary>
        protected TcpSocketClientConfiguration CreateHighPerformanceTcpClientConfiguration()
        {
            return new TcpSocketClientConfiguration
            {
                ConnectTimeout = TimeSpan.FromSeconds(5),
                SendTimeout = TimeSpan.FromSeconds(5),
                ReceiveTimeout = TimeSpan.FromSeconds(5),
                OperationTimeout = TimeSpan.FromSeconds(5),
                SendBufferSize = 64 * 1024,
                ReceiveBufferSize = 64 * 1024,
                MaxReceiveBufferSize = 1024 * 1024,
                NoDelay = true,
                KeepAlive = false,
                EnableHeartbeat = false,
                BufferManager = new SegmentBufferManager(1024, 8192),
                FrameBuilder = new LengthPrefixedFrameBuilder()
            };
        }

        /// <summary>
        /// 创建高性能TCP服务器配置
        /// </summary>
        protected TcpSocketServerConfiguration CreateHighPerformanceTcpServerConfiguration()
        {
            return new TcpSocketServerConfiguration
            {
                SendTimeout = TimeSpan.FromSeconds(5),
                ReceiveTimeout = TimeSpan.FromSeconds(5),
                SendBufferSize = 64 * 1024,
                ReceiveBufferSize = 64 * 1024,
                PendingConnectionBacklog = 200,
                AllowNatTraversal = false,
                ReuseAddress = true,
                BufferManager = new SegmentBufferManager(1024, 8192),
                FrameBuilder = new LengthPrefixedFrameBuilder()
            };
        }

        /// <summary>
        /// 创建高性能UDP客户端配置
        /// </summary>
        protected UdpSocketClientConfiguration CreateHighPerformanceUdpClientConfiguration()
        {
            return new UdpSocketClientConfiguration
            {
                SendTimeout = TimeSpan.FromSeconds(5),
                ReceiveTimeout = TimeSpan.FromSeconds(5),
                OperationTimeout = TimeSpan.FromSeconds(5),
                SendBufferSize = 64 * 1024,
                ReceiveBufferSize = 64 * 1024,
                MaxReceiveBufferSize = 1024 * 1024,
                ConnectedMode = true,
                DontFragment = false,
                Broadcast = false,
                ReuseAddress = true,
                EnableHeartbeat = false,
                BufferManager = new SegmentBufferManager(1024, 8192),
                FrameBuilder = new LengthPrefixedFrameBuilder()
            };
        }

        /// <summary>
        /// 创建高性能UDP服务器配置
        /// </summary>
        protected UdpSocketServerConfiguration CreateHighPerformanceUdpServerConfiguration()
        {
            return new UdpSocketServerConfiguration
            {
                SendTimeout = TimeSpan.FromSeconds(5),
                ReceiveTimeout = TimeSpan.FromSeconds(5),
                OperationTimeout = TimeSpan.FromSeconds(5),
                SendBufferSize = 64 * 1024,
                ReceiveBufferSize = 64 * 1024,
                MaxReceiveBufferSize = 1024 * 1024,
                MaxClients = 1000,
                ClientTimeout = TimeSpan.FromMinutes(5),
                CleanupInterval = TimeSpan.FromMinutes(1),
                DontFragment = false,
                Broadcast = false,
                ReuseAddress = true,
                BufferManager = new SegmentBufferManager(1024, 8192),
                FrameBuilder = new LengthPrefixedFrameBuilder()
            };
        }

        /// <summary>
        /// 创建高性能WebSocket客户端配置
        /// </summary>
        protected WebSocketClientConfiguration CreateHighPerformanceWebSocketClientConfiguration()
        {
            return new WebSocketClientConfiguration
            {
                ConnectTimeout = TimeSpan.FromSeconds(10),
                CloseTimeout = TimeSpan.FromSeconds(5),
                SendTimeout = TimeSpan.FromSeconds(5),
                ReceiveTimeout = TimeSpan.FromSeconds(30),
                OperationTimeout = TimeSpan.FromSeconds(5),
                SendBufferSize = 64 * 1024,
                ReceiveBufferSize = 64 * 1024,
                KeepAliveInterval = TimeSpan.FromSeconds(30),
                KeepAliveTimeout = TimeSpan.FromSeconds(5),
                NoDelay = true,
                BufferManager = new SegmentBufferManager(1024, 8192),
                ReasonableFragmentSize = 16 * 1024
            };
        }

        /// <summary>
        /// 创建高性能WebSocket服务器配置
        /// </summary>
        protected WebSocketServerConfiguration CreateHighPerformanceWebSocketServerConfiguration()
        {
            return new WebSocketServerConfiguration
            {
                SendTimeout = TimeSpan.FromSeconds(5),
                ReceiveTimeout = TimeSpan.FromSeconds(30),
                SendBufferSize = 64 * 1024,
                ReceiveBufferSize = 64 * 1024,
                PendingConnectionBacklog = 200,
                AllowNatTraversal = false,
                BufferManager = new SegmentBufferManager(1024, 8192),
                ReasonableFragmentSize = 16 * 1024
            };
        }

        /// <summary>
        /// 等待条件成立
        /// </summary>
        protected async Task<bool> WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
        {
            var endTime = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < endTime)
            {
                if (condition())
                    return true;
                await Task.Delay(10);
            }
            return false;
        }

        /// <summary>
        /// 获取本机可用的网络接口信息
        /// </summary>
        protected NetworkInterface[] GetAvailableNetworkInterfaces()
        {
            return NetworkInterface.GetAllNetworkInterfaces();
        }

        /// <summary>
        /// 计算网络延迟
        /// </summary>
        protected TimeSpan CalculateLatency(DateTime startTime, DateTime endTime)
        {
            return endTime - startTime;
        }

        /// <summary>
        /// 计算吞吐量（字节/秒）
        /// </summary>
        protected double CalculateThroughput(long totalBytes, TimeSpan elapsed)
        {
            return totalBytes / elapsed.TotalSeconds;
        }

        /// <summary>
        /// 计算消息处理速率（消息/秒）
        /// </summary>
        protected double CalculateMessageRate(int messageCount, TimeSpan elapsed)
        {
            return messageCount / elapsed.TotalSeconds;
        }
    }


} 