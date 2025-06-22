using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Wombat.Network.WebSockets;
using Wombat.Network.Benchmark.BenchmarkHelpers;
using Wombat.Network.Benchmark.Utilities;

namespace Wombat.Network.Benchmark.Benchmarks
{
    /// <summary>
    /// WebSocket性能基准测试
    /// 测试WebSocket在不同场景下的性能表现
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob]
    public class WebSocketBenchmarks : NetworkBenchmarkBase
    {
        private WebSocketServer _server;
        private WebSocketClient _client;
        private BenchmarkWebSocketServerModuleCatalog _serverCatalog;
        private BenchmarkWebSocketClientDispatcher _clientDispatcher;
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
            
            // 创建服务器模块目录
            _serverCatalog = new BenchmarkWebSocketServerModuleCatalog();
            
            // 创建和启动WebSocket服务器
            var serverConfig = CreateHighPerformanceWebSocketServerConfiguration();
            _server = new WebSocketServer(_port, _serverCatalog, serverConfig);
            _server.Listen();
            
            await Task.Delay(100); // 等待服务器启动
        }

        [GlobalCleanup]
        public async Task GlobalCleanup()
        {
            if (_server?.IsListening == true)
            {
                _server.Shutdown();
            }
            
            await Task.Delay(100);
        }

        [IterationSetup]
        public void IterationSetup()
        {
            // 重置统计
            _serverCatalog?.ResetCounters();
            
            // 创建客户端
            _clientDispatcher = new BenchmarkWebSocketClientDispatcher();
            var clientConfig = CreateHighPerformanceWebSocketClientConfiguration();
            var uri = new Uri($"ws://localhost:{_port}/");
            _client = new WebSocketClient(uri, _clientDispatcher, clientConfig);
            
            _client.ConnectAsync().Wait();
            Task.Delay(50).Wait(); // 等待连接稳定
        }

        [IterationCleanup]
        public void IterationCleanup()
        {
            if (_client?.State == WebSocketState.Open)
            {
                _client.Close(WebSocketCloseCode.NormalClosure).Wait();
            }
            
            Task.Delay(50).Wait();
        }

        /// <summary>
        /// WebSocket文本消息发送吞吐量测试
        /// </summary>
        [Benchmark]
        public async Task WebSocketTextSendThroughput()
        {
            var textMessage = GenerateTextMessage(MessageSize);
            
            for (int i = 0; i < MessageCount; i++)
            {
                await _client.SendTextAsync(textMessage);
            }
            
            // 等待服务器接收完成
            await WaitForMessagesAsync(MessageCount);
        }

        /// <summary>
        /// WebSocket二进制消息发送吞吐量测试
        /// </summary>
        [Benchmark]
        public async Task WebSocketBinarySendThroughput()
        {
            var binaryData = GenerateTestData(MessageSize);
            
            for (int i = 0; i < MessageCount; i++)
            {
                await _client.SendBinaryAsync(binaryData);
            }
            
            // 等待服务器接收完成
            await WaitForMessagesAsync(MessageCount);
        }

        /// <summary>
        /// WebSocket往返时间(RTT)测试
        /// </summary>
        [Benchmark]
        public async Task WebSocketRoundTripTime()
        {
            var textMessage = GenerateTextMessage(MessageSize);
            
            for (int i = 0; i < MessageCount; i++)
            {
                await _client.SendTextAsync(textMessage);
                await WaitForClientResponse();
            }
        }

        /// <summary>
        /// WebSocket并发连接测试
        /// </summary>
        [Benchmark]
        public async Task WebSocketConcurrentConnections()
        {
            var clients = new List<WebSocketClient>();
            var tasks = new List<Task>();
            
            try
            {
                // 创建多个并发连接
                for (int i = 0; i < ConcurrentConnections; i++)
                {
                    var dispatcher = new BenchmarkWebSocketClientDispatcher();
                    var config = CreateHighPerformanceWebSocketClientConfiguration();
                    var uri = new Uri($"ws://localhost:{_port}/");
                    var client = new WebSocketClient(uri, dispatcher, config);
                    
                    clients.Add(client);
                    tasks.Add(client.ConnectAsync());
                }
                
                await Task.WhenAll(tasks);
                tasks.Clear();
                
                // 每个连接发送消息
                var textMessage = GenerateTextMessage(MessageSize);
                foreach (var client in clients)
                {
                    for (int i = 0; i < MessageCount / ConcurrentConnections; i++)
                    {
                        tasks.Add(client.SendTextAsync(textMessage));
                    }
                }
                
                await Task.WhenAll(tasks);
            }
            finally
            {
                // 清理连接
                var closeTasks = new List<Task>();
                foreach (var client in clients)
                {
                    if (client.State == WebSocketState.Open)
                    {
                        closeTasks.Add(client.Close(WebSocketCloseCode.NormalClosure));
                    }
                }
                await Task.WhenAll(closeTasks);
            }
        }

        /// <summary>
        /// WebSocket大文件传输测试
        /// </summary>
        [Benchmark]
        public async Task WebSocketLargeFileTransfer()
        {
            const int largeFileSize = 1024 * 1024; // 1MB
            var largeData = GenerateTestData(largeFileSize);
            
            // 分片传输大文件
            const int chunkSize = 8192;
            int chunks = largeFileSize / chunkSize;
            
            for (int i = 0; i < chunks; i++)
            {
                int offset = i * chunkSize;
                int size = Math.Min(chunkSize, largeFileSize - offset);
                
                await _client.SendBinaryAsync(largeData, offset, size);
            }
            
            // 等待传输完成
            await WaitForTotalBytesReceived(largeFileSize);
        }

        /// <summary>
        /// WebSocket批量操作测试
        /// </summary>
        [Benchmark]
        public async Task WebSocketBatchOperations()
        {
            var tasks = new List<Task>();
            var textMessage = GenerateTextMessage(MessageSize);
            
            // 批量发送文本消息
            for (int i = 0; i < MessageCount / 2; i++)
            {
                tasks.Add(_client.SendTextAsync(textMessage));
            }
            
            // 批量发送二进制消息
            var binaryData = GenerateTestData(MessageSize);
            for (int i = 0; i < MessageCount / 2; i++)
            {
                tasks.Add(_client.SendBinaryAsync(binaryData));
            }
            
            await Task.WhenAll(tasks);
            await WaitForMessagesAsync(MessageCount);
        }

        /// <summary>
        /// WebSocket流式传输测试
        /// </summary>
        [Benchmark]
        public async Task WebSocketStreamTransfer()
        {
            var streamData = GenerateTestData(MessageSize * MessageCount);
            
            using (var stream = new System.IO.MemoryStream(streamData))
            {
                await _client.SendStreamAsync(stream);
            }
            
            // 等待流传输完成
            await WaitForTotalBytesReceived(streamData.Length);
        }

        /// <summary>
        /// WebSocket连接建立性能测试
        /// </summary>
        [Benchmark]
        public async Task WebSocketConnectionEstablishment()
        {
            var clients = new List<WebSocketClient>();
            var connectTasks = new List<Task>();
            
            try
            {
                // 测试快速建立多个WebSocket连接的性能
                for (int i = 0; i < ConcurrentConnections; i++)
                {
                    var dispatcher = new BenchmarkWebSocketClientDispatcher();
                    var config = CreateHighPerformanceWebSocketClientConfiguration();
                    var uri = new Uri($"ws://localhost:{_port}/");
                    var client = new WebSocketClient(uri, dispatcher, config);
                    
                    clients.Add(client);
                    connectTasks.Add(client.ConnectAsync());
                }
                
                await Task.WhenAll(connectTasks);
            }
            finally
            {
                // 立即关闭所有连接
                var closeTasks = new List<Task>();
                foreach (var client in clients)
                {
                    if (client.State == WebSocketState.Open)
                    {
                        closeTasks.Add(client.Close(WebSocketCloseCode.NormalClosure));
                    }
                }
                await Task.WhenAll(closeTasks);
            }
        }

        /// <summary>
        /// WebSocket心跳机制测试
        /// </summary>
        [Benchmark]
        public async Task WebSocketHeartbeatPerformance()
        {
            // 启用心跳的配置
            var heartbeatConfig = CreateHighPerformanceWebSocketClientConfiguration();
            heartbeatConfig.KeepAliveInterval = TimeSpan.FromSeconds(1);
            
            var uri = new Uri($"ws://localhost:{_port}/");
            var heartbeatDispatcher = new BenchmarkWebSocketClientDispatcher();
            var heartbeatClient = new WebSocketClient(uri, heartbeatDispatcher, heartbeatConfig);
            
            try
            {
                await heartbeatClient.ConnectAsync();
                
                // 保持连接一段时间，测试心跳性能
                await Task.Delay(TimeSpan.FromSeconds(5));
                
                // 在心跳期间发送一些数据
                var textMessage = GenerateTextMessage(MessageSize);
                for (int i = 0; i < MessageCount; i++)
                {
                    await heartbeatClient.SendTextAsync(textMessage);
                    await Task.Delay(10); // 模拟实际使用场景
                }
            }
            finally
            {
                if (heartbeatClient.State == WebSocketState.Open)
                {
                    await heartbeatClient.Close(WebSocketCloseCode.NormalClosure);
                }
            }
        }

        /// <summary>
        /// 生成指定大小的文本消息
        /// </summary>
        private string GenerateTextMessage(int size)
        {
            var chars = new char[size];
            var random = new Random(42); // 固定种子确保可重复性
            
            for (int i = 0; i < size; i++)
            {
                chars[i] = (char)('A' + (random.Next() % 26));
            }
            
            return new string(chars);
        }

        /// <summary>
        /// 等待接收指定数量的消息
        /// </summary>
        private async Task WaitForMessagesAsync(int expectedCount)
        {
            var timeout = TimeSpan.FromSeconds(30);
            await WaitForConditionAsync(() => _serverCatalog.ReceivedMessages >= expectedCount, timeout);
        }

        /// <summary>
        /// 等待客户端响应
        /// </summary>
        private async Task WaitForClientResponse()
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
            await WaitForConditionAsync(() => _serverCatalog.ReceivedBytes >= expectedBytes, timeout);
        }
    }
} 