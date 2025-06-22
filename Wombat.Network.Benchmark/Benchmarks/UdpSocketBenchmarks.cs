using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Wombat.Network.Benchmark.BenchmarkHelpers;
using Wombat.Network.Benchmark.Utilities;
using Wombat.Network.Sockets;

namespace Wombat.Network.Benchmark.Benchmarks
{
    /// <summary>
    /// UDP Socket性能基准测试
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob]
    public class UdpSocketBenchmarks : NetworkBenchmarkBase
    {
        private UdpSocketServer _server;
        private UdpSocketClient _client;
        private BenchmarkUdpServerEventDispatcher _serverDispatcher;
        private BenchmarkUdpClientEventDispatcher _clientDispatcher;
        private int _port;
        
        // 测试参数
        [Params(512, 1024, 4096, 8192)] // UDP建议的消息大小（避免分片）
        public int MessageSize { get; set; }
        
        [Params(1, 10, 100, 1000)] // 不同消息数量
        public int MessageCount { get; set; }
        
        [Params(1, 5, 10, 50)] // 不同并发客户端数
        public int ConcurrentClients { get; set; }

        [GlobalSetup]
        public async Task GlobalSetup()
        {
            _port = GetAvailablePort();
            _serverDispatcher = new BenchmarkUdpServerEventDispatcher();
            
            // 创建高性能UDP服务器
            var serverConfig = CreateHighPerformanceUdpServerConfiguration();
            _server = new UdpSocketServer(_port, _serverDispatcher, serverConfig);
            await _server.Listen();
            
            // 等待服务器启动
            await Task.Delay(100);
        }

        [GlobalCleanup]
        public async Task GlobalCleanup()
        {
            if (_client != null)
            {
                await _client.Close();
            }
            
            if (_server != null)
            {
                await _server.Close();
            }
        }

        [IterationSetup]
        public void IterationSetup()
        {
            _clientDispatcher = new BenchmarkUdpClientEventDispatcher();
            
            // 创建高性能UDP客户端
            var clientConfig = CreateHighPerformanceUdpClientConfiguration();
            _client = new UdpSocketClient(IPAddress.Loopback, _port, _clientDispatcher, clientConfig);
            
            _client.Connect().Wait();
            
            // 重置计数器
            _serverDispatcher.ResetCounters();
            _clientDispatcher.ResetCounters();
        }

        [IterationCleanup]
        public void IterationCleanup()
        {
            if (_client != null)
            {
                _client.Close().Wait();
                _client = null;
            }
        }

        /// <summary>
        /// UDP发送吞吐量测试
        /// </summary>
        [Benchmark]
        public async Task UdpSendThroughput()
        {
            var data = GenerateTestData(MessageSize);
            var tasks = new List<Task>();
            
            for (int i = 0; i < MessageCount; i++)
            {
                tasks.Add(_client.SendAsync(data));
            }
            
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// UDP接收吞吐量测试
        /// </summary>
        [Benchmark]
        public async Task UdpReceiveThroughput()
        {
            var data = GenerateTestData(MessageSize);
            
            // 启动接收任务
            var receiveTask = WaitForMessagesAsync(MessageCount);
            
            // 发送消息
            for (int i = 0; i < MessageCount; i++)
            {
                await _client.SendAsync(data);
                // UDP需要适当的间隔避免缓冲区溢出
                if (i % 10 == 0)
                {
                    await Task.Delay(1);
                }
            }
            
            // 等待接收完成
            await receiveTask;
        }

        /// <summary>
        /// UDP往返延迟测试（RTT）
        /// </summary>
        [Benchmark]
        public async Task UdpRoundTripTime()
        {
            var data = GenerateTestData(MessageSize);
            var rttTimes = new List<long>();
            
            for (int i = 0; i < MessageCount; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                
                // 发送消息并等待回复
                await _client.SendAsync(data);
                
                // 等待服务器处理并回复
                await WaitForServerResponse();
                
                stopwatch.Stop();
                rttTimes.Add(stopwatch.ElapsedTicks);
                
                // UDP需要间隔避免网络拥塞
                await Task.Delay(1);
            }
        }

        /// <summary>
        /// UDP并发客户端性能测试
        /// </summary>
        [Benchmark]
        public async Task UdpConcurrentClients()
        {
            var clients = new List<UdpSocketClient>();
            var tasks = new List<Task>();
            
            try
            {
                // 创建多个并发客户端
                for (int i = 0; i < ConcurrentClients; i++)
                {
                    var dispatcher = new BenchmarkUdpClientEventDispatcher();
                    var config = CreateHighPerformanceUdpClientConfiguration();
                    var client = new UdpSocketClient(IPAddress.Loopback, _port, dispatcher, config);
                    
                    clients.Add(client);
                    tasks.Add(client.Connect());
                }
                
                // 等待所有客户端连接
                await Task.WhenAll(tasks);
                
                // 每个客户端发送数据
                var data = GenerateTestData(MessageSize);
                var sendTasks = new List<Task>();
                
                foreach (var client in clients)
                {
                    for (int i = 0; i < MessageCount / ConcurrentClients; i++)
                    {
                        sendTasks.Add(SendWithDelay(client, data, i));
                    }
                }
                
                await Task.WhenAll(sendTasks);
            }
            finally
            {
                // 清理连接
                var closeTasks = new List<Task>();
                foreach (var client in clients)
                {
                    closeTasks.Add(client.Close());
                }
                await Task.WhenAll(closeTasks);
            }
        }

        /// <summary>
        /// UDP突发流量测试
        /// </summary>
        [Benchmark]
        public async Task UdpBurstTraffic()
        {
            var data = GenerateTestData(MessageSize);
            const int burstSize = 50;
            int burstCount = MessageCount / burstSize;
            
            for (int burst = 0; burst < burstCount; burst++)
            {
                var burstTasks = new List<Task>();
                
                // 突发发送
                for (int i = 0; i < burstSize; i++)
                {
                    burstTasks.Add(_client.SendAsync(data));
                }
                
                await Task.WhenAll(burstTasks);
                
                // 突发间隔
                await Task.Delay(10);
            }
        }

        /// <summary>
        /// UDP大包传输测试（接近MTU大小）
        /// </summary>
        [Benchmark]
        public async Task UdpLargePacketTransfer()
        {
            // 接近以太网MTU的数据包大小
            const int largePacketSize = 1400;
            var data = GenerateTestData(largePacketSize);
            
            var tasks = new List<Task>();
            
            for (int i = 0; i < MessageCount; i++)
            {
                tasks.Add(_client.SendAsync(data));
                
                // 大包需要更多间隔
                if (i % 5 == 0)
                {
                    await Task.Delay(1);
                }
            }
            
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// UDP小包高频率测试
        /// </summary>
        [Benchmark]
        public async Task UdpSmallPacketHighFrequency()
        {
            const int smallPacketSize = 64;
            var data = GenerateTestData(smallPacketSize);
            
            // 高频率发送小包
            for (int i = 0; i < MessageCount; i++)
            {
                await _client.SendAsync(data);
            }
        }

        /// <summary>
        /// UDP广播性能测试
        /// </summary>
        [Benchmark]
        public async Task UdpBroadcastPerformance()
        {
            // 创建支持广播的客户端配置
            var broadcastConfig = CreateHighPerformanceUdpClientConfiguration();
            broadcastConfig.Broadcast = true;
            
            var broadcastClient = new UdpSocketClient(IPAddress.Broadcast, _port, _clientDispatcher, broadcastConfig);
            
            try
            {
                await broadcastClient.Connect();
                
                var data = GenerateTestData(MessageSize);
                
                for (int i = 0; i < MessageCount; i++)
                {
                    await broadcastClient.SendAsync(data);
                    await Task.Delay(1); // 广播需要适当间隔
                }
            }
            finally
            {
                await broadcastClient.Close();
            }
        }

        /// <summary>
        /// UDP连接建立性能测试
        /// </summary>
        [Benchmark]
        public async Task UdpConnectionEstablishment()
        {
            var clients = new List<UdpSocketClient>();
            var connectTasks = new List<Task>();
            
            try
            {
                // 测试快速建立多个UDP"连接"的性能
                for (int i = 0; i < ConcurrentClients; i++)
                {
                    var dispatcher = new BenchmarkUdpClientEventDispatcher();
                    var config = CreateHighPerformanceUdpClientConfiguration();
                    var client = new UdpSocketClient(IPAddress.Loopback, _port, dispatcher, config);
                    
                    clients.Add(client);
                    connectTasks.Add(client.Connect());
                }
                
                await Task.WhenAll(connectTasks);
            }
            finally
            {
                // 立即关闭所有连接
                var closeTasks = new List<Task>();
                foreach (var client in clients)
                {
                    closeTasks.Add(client.Close());
                }
                await Task.WhenAll(closeTasks);
            }
        }

        /// <summary>
        /// 带延迟的发送（避免UDP缓冲区溢出）
        /// </summary>
        private async Task SendWithDelay(UdpSocketClient client, byte[] data, int index)
        {
            await client.SendAsync(data);
            
            // 根据索引添加微小延迟，避免网络拥塞
            if (index % 10 == 0)
            {
                await Task.Delay(1);
            }
        }

        /// <summary>
        /// 等待接收指定数量的消息
        /// </summary>
        private async Task WaitForMessagesAsync(int expectedCount)
        {
            var timeout = TimeSpan.FromSeconds(30);
            await WaitForConditionAsync(() => _serverDispatcher.ReceivedMessages >= expectedCount, timeout);
        }

        /// <summary>
        /// 等待服务器响应
        /// </summary>
        private async Task WaitForServerResponse()
        {
            var timeout = TimeSpan.FromSeconds(5);
            var initialCount = _clientDispatcher.ReceivedMessages;
            await WaitForConditionAsync(() => _clientDispatcher.ReceivedMessages > initialCount, timeout);
        }

        /// <summary>
        /// 等待接收指定字节数
        /// </summary>
        private async Task WaitForTotalBytesReceived(int expectedBytes)
        {
            var timeout = TimeSpan.FromSeconds(30);
            await WaitForConditionAsync(() => _serverDispatcher.ReceivedBytes >= expectedBytes, timeout);
        }
    }
} 