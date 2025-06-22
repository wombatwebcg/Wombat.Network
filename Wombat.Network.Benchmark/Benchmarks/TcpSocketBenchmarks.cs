using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Wombat.Network.Benchmark.BenchmarkHelpers;
using Wombat.Network.Sockets;
using BenchmarkEventDispatchers = Wombat.Network.Benchmark.Utilities;

namespace Wombat.Network.Benchmark.Benchmarks
{
    /// <summary>
    /// TCP Socket性能基准测试
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob]
    public class TcpSocketBenchmarks : NetworkBenchmarkBase
    {
        private TcpSocketServer _server;
        private TcpSocketClient _client;
        private BenchmarkEventDispatchers.BenchmarkTcpServerEventDispatcher _serverDispatcher;
        private BenchmarkEventDispatchers.BenchmarkTcpClientEventDispatcher _clientDispatcher;
        private int _port;
        
        // 测试参数
        [Params(1024, 4096, 16384, 65536)] // 不同消息大小
        public int MessageSize { get; set; }
        
        [Params(1, 10, 100, 1000)] // 不同消息数量
        public int MessageCount { get; set; }
        
        [Params(1, 5, 10, 20)] // 不同并发连接数
        public int ConcurrentConnections { get; set; }

        [GlobalSetup]
        public async Task GlobalSetup()
        {
            _port = GetAvailablePort();
            _serverDispatcher = new BenchmarkEventDispatchers.BenchmarkTcpServerEventDispatcher();
            
            // 创建高性能TCP服务器
            var serverConfig = CreateHighPerformanceTcpServerConfiguration();
            _server = new TcpSocketServer(_port, _serverDispatcher, serverConfig);
            _server.Listen();
            
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
                _server.Shutdown();
            }
        }

        [IterationSetup]
        public void IterationSetup()
        {
            _clientDispatcher = new BenchmarkEventDispatchers.BenchmarkTcpClientEventDispatcher();
            
            // 创建高性能TCP客户端
            var clientConfig = CreateHighPerformanceTcpClientConfiguration();
            _client = new TcpSocketClient(IPAddress.Loopback, _port, _clientDispatcher, clientConfig);
            
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
        /// TCP发送吞吐量测试
        /// </summary>
        [Benchmark]
        public async Task TcpSendThroughput()
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
        /// TCP接收吞吐量测试
        /// </summary>
        [Benchmark]
        public async Task TcpReceiveThroughput()
        {
            var data = GenerateTestData(MessageSize);
            
            // 启动接收任务
            var receiveTask = WaitForMessagesAsync(MessageCount);
            
            // 发送消息
            for (int i = 0; i < MessageCount; i++)
            {
                await _client.SendAsync(data);
            }
            
            // 等待接收完成
            await receiveTask;
        }

        /// <summary>
        /// TCP往返延迟测试（RTT）
        /// </summary>
        [Benchmark]
        public async Task TcpRoundTripTime()
        {
            var data = GenerateTestData(MessageSize);
            var rttTimes = new List<long>();
            
            for (int i = 0; i < MessageCount; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                
                // 发送消息并等待回复
                await _client.SendAsync(data);
                
                // 等待服务器处理并回复（这里简化为等待接收到数据）
                await WaitForServerResponse();
                
                stopwatch.Stop();
                rttTimes.Add(stopwatch.ElapsedTicks);
            }
        }

        /// <summary>
        /// TCP并发连接性能测试
        /// </summary>
        [Benchmark]
        public async Task TcpConcurrentConnections()
        {
            var clients = new List<TcpSocketClient>();
            var tasks = new List<Task>();
            
            try
            {
                // 创建多个并发连接
                for (int i = 0; i < ConcurrentConnections; i++)
                {
                    var dispatcher = new BenchmarkEventDispatchers.BenchmarkTcpClientEventDispatcher();
                    var config = CreateHighPerformanceTcpClientConfiguration();
                    var client = new TcpSocketClient(IPAddress.Loopback, _port, dispatcher, config);
                    
                    clients.Add(client);
                    tasks.Add(client.Connect());
                }
                
                // 等待所有连接建立
                await Task.WhenAll(tasks);
                
                // 每个连接发送数据
                var data = GenerateTestData(MessageSize);
                var sendTasks = new List<Task>();
                
                foreach (var client in clients)
                {
                    for (int i = 0; i < MessageCount / ConcurrentConnections; i++)
                    {
                        sendTasks.Add(client.SendAsync(data));
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
        /// TCP大文件传输性能测试
        /// </summary>
        [Benchmark]
        public async Task TcpLargeFileTransfer()
        {
            const int chunkSize = 64 * 1024; // 64KB chunks
            const int totalSize = 1024 * 1024; // 1MB total
            var chunksCount = totalSize / chunkSize;
            
            var chunk = GenerateTestData(chunkSize);
            
            for (int i = 0; i < chunksCount; i++)
            {
                await _client.SendAsync(chunk);
            }
            
            // 等待所有数据被服务器接收
            await WaitForTotalBytesReceived(totalSize);
        }

        /// <summary>
        /// TCP批量操作性能测试
        /// </summary>
        [Benchmark]
        public async Task TcpBatchOperations()
        {
            var data = GenerateTestData(MessageSize);
            const int batchSize = 100;
            var batches = MessageCount / batchSize;
            
            for (int batch = 0; batch < batches; batch++)
            {
                var tasks = new List<Task>();
                
                for (int i = 0; i < batchSize; i++)
                {
                    tasks.Add(_client.SendAsync(data));
                }
                
                await Task.WhenAll(tasks);
            }
        }

        /// <summary>
        /// TCP连接建立性能测试
        /// </summary>
        [Benchmark]
        public async Task TcpConnectionEstablishment()
        {
            var connections = new List<TcpSocketClient>();
            var connectTasks = new List<Task>();
            
            try
            {
                // 测试快速建立多个连接的性能
                for (int i = 0; i < ConcurrentConnections; i++)
                {
                    var dispatcher = new BenchmarkEventDispatchers.BenchmarkTcpClientEventDispatcher();
                    var config = CreateHighPerformanceTcpClientConfiguration();
                    var client = new TcpSocketClient(IPAddress.Loopback, _port, dispatcher, config);
                    
                    connections.Add(client);
                    connectTasks.Add(client.Connect());
                }
                
                await Task.WhenAll(connectTasks);
            }
            finally
            {
                // 立即关闭所有连接
                var closeTasks = new List<Task>();
                foreach (var client in connections)
                {
                    closeTasks.Add(client.Close());
                }
                await Task.WhenAll(closeTasks);
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