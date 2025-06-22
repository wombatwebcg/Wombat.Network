using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Wombat.Network.Sockets;
using Wombat.Network.WebSockets;
using Wombat.Network.Benchmark.BenchmarkHelpers;
using Wombat.Network.Benchmark.Utilities;

namespace Wombat.Network.Benchmark.Benchmarks
{
    /// <summary>
    /// 连接性能基准测试
    /// 专门测试不同协议的连接建立、维持和关闭性能
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob]
    public class ConnectionBenchmarks : NetworkBenchmarkBase
    {
        private TcpSocketServer _tcpServer;
        private UdpSocketServer _udpServer;
        private WebSocketServer _webSocketServer;
        private int _tcpPort;
        private int _udpPort;
        private int _webSocketPort;

        // 测试参数
        [Params(1, 5, 10, 25, 50)] // 不同连接数量
        public int ConnectionCount { get; set; }

        [Params(100, 500, 1000)] // 连接操作次数
        public int OperationCount { get; set; }

        [GlobalSetup]
        public async Task GlobalSetup()
        {
            // 获取可用端口
            _tcpPort = GetAvailablePort();
            _udpPort = GetAvailablePort();
            _webSocketPort = GetAvailablePort();

            // 启动TCP服务器
            var tcpServerDispatcher = new BenchmarkTcpServerEventDispatcher();
            var tcpServerConfig = CreateHighPerformanceTcpServerConfiguration();
            _tcpServer = new TcpSocketServer(_tcpPort, tcpServerDispatcher, tcpServerConfig);
            _tcpServer.Listen();

            // 启动UDP服务器
            var udpServerDispatcher = new BenchmarkUdpServerEventDispatcher();
            var udpServerConfig = CreateHighPerformanceUdpServerConfiguration();
            _udpServer = new UdpSocketServer(_udpPort, udpServerDispatcher, udpServerConfig);
            await _udpServer.Listen();

            // 启动WebSocket服务器
            var webSocketCatalog = new BenchmarkWebSocketServerModuleCatalog();
            var webSocketConfig = CreateHighPerformanceWebSocketServerConfiguration();
            _webSocketServer = new WebSocketServer(_webSocketPort, webSocketCatalog, webSocketConfig);
            _webSocketServer.Listen();

            await Task.Delay(200); // 等待所有服务器启动
        }

        [GlobalCleanup]
        public async Task GlobalCleanup()
        {
            _tcpServer?.Shutdown();
            
            if (_udpServer?.IsListening == true)
            {
                await _udpServer.Close();
            }
            
            _webSocketServer?.Shutdown();
            
            await Task.Delay(100);
        }

        /// <summary>
        /// TCP连接建立性能测试
        /// </summary>
        [Benchmark]
        public async Task TcpConnectionEstablishment()
        {
            var tasks = new List<Task>();
            
            for (int i = 0; i < OperationCount; i++)
            {
                tasks.Add(EstablishTcpConnection());
            }
            
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// TCP并发连接测试
        /// </summary>
        [Benchmark]
        public async Task TcpConcurrentConnections()
        {
            var clients = new List<TcpSocketClient>();
            var connectTasks = new List<Task>();
            
            try
            {
                // 并发建立多个连接
                for (int i = 0; i < ConnectionCount; i++)
                {
                    var dispatcher = new BenchmarkTcpClientEventDispatcher();
                    var config = CreateHighPerformanceTcpClientConfiguration();
                    var client = new TcpSocketClient(IPAddress.Loopback, _tcpPort, dispatcher, config);
                    
                    clients.Add(client);
                    connectTasks.Add(client.Connect());
                }
                
                await Task.WhenAll(connectTasks);
                
                // 保持连接一段时间
                await Task.Delay(100);
            }
            finally
            {
                // 并发关闭所有连接
                var closeTasks = new List<Task>();
                foreach (var client in clients)
                {
                    closeTasks.Add(client.Close());
                }
                await Task.WhenAll(closeTasks);
            }
        }

        /// <summary>
        /// UDP连接建立性能测试
        /// </summary>
        [Benchmark]
        public async Task UdpConnectionEstablishment()
        {
            var tasks = new List<Task>();
            
            for (int i = 0; i < OperationCount; i++)
            {
                tasks.Add(EstablishUdpConnection());
            }
            
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// UDP并发连接测试
        /// </summary>
        [Benchmark]
        public async Task UdpConcurrentConnections()
        {
            var clients = new List<UdpSocketClient>();
            var connectTasks = new List<Task>();
            
            try
            {
                // 并发建立多个UDP"连接"
                for (int i = 0; i < ConnectionCount; i++)
                {
                    var dispatcher = new BenchmarkUdpClientEventDispatcher();
                    var config = CreateHighPerformanceUdpClientConfiguration();
                    var client = new UdpSocketClient(IPAddress.Loopback, _udpPort, dispatcher, config);
                    
                    clients.Add(client);
                    connectTasks.Add(client.Connect());
                }
                
                await Task.WhenAll(connectTasks);
                
                // 保持连接一段时间
                await Task.Delay(100);
            }
            finally
            {
                // 并发关闭所有连接
                var closeTasks = new List<Task>();
                foreach (var client in clients)
                {
                    closeTasks.Add(client.Close());
                }
                await Task.WhenAll(closeTasks);
            }
        }

        /// <summary>
        /// WebSocket连接建立性能测试
        /// </summary>
        [Benchmark]
        public async Task WebSocketConnectionEstablishment()
        {
            var tasks = new List<Task>();
            
            for (int i = 0; i < OperationCount; i++)
            {
                tasks.Add(EstablishWebSocketConnection());
            }
            
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// WebSocket并发连接测试
        /// </summary>
        [Benchmark]
        public async Task WebSocketConcurrentConnections()
        {
            var clients = new List<WebSocketClient>();
            var connectTasks = new List<Task>();
            
            try
            {
                // 并发建立多个WebSocket连接
                for (int i = 0; i < ConnectionCount; i++)
                {
                    var dispatcher = new BenchmarkWebSocketClientDispatcher();
                    var config = CreateHighPerformanceWebSocketClientConfiguration();
                    var uri = new Uri($"ws://localhost:{_webSocketPort}/");
                    var client = new WebSocketClient(uri, dispatcher, config);
                    
                    clients.Add(client);
                    connectTasks.Add(client.ConnectAsync());
                }
                
                await Task.WhenAll(connectTasks);
                
                // 保持连接一段时间
                await Task.Delay(100);
            }
            finally
            {
                // 并发关闭所有连接
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
        /// 连接生命周期测试（建立-使用-关闭）
        /// </summary>
        [Benchmark]
        public async Task ConnectionLifecycle()
        {
            var tasks = new List<Task>();
            
            // TCP生命周期
            tasks.Add(TcpConnectionLifecycle());
            
            // UDP生命周期
            tasks.Add(UdpConnectionLifecycle());
            
            // WebSocket生命周期
            tasks.Add(WebSocketConnectionLifecycle());
            
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 连接池性能测试
        /// </summary>
        [Benchmark]
        public async Task ConnectionPoolPerformance()
        {
            const int poolSize = 10;
            const int operationsPerConnection = 100;
            
            // TCP连接池
            var tcpClients = new List<TcpSocketClient>();
            var tcpTasks = new List<Task>();
            
            try
            {
                // 创建TCP连接池
                for (int i = 0; i < poolSize; i++)
                {
                    var dispatcher = new BenchmarkTcpClientEventDispatcher();
                    var config = CreateHighPerformanceTcpClientConfiguration();
                    var client = new TcpSocketClient(IPAddress.Loopback, _tcpPort, dispatcher, config);
                    
                    await client.Connect();
                    tcpClients.Add(client);
                }
                
                // 使用连接池进行操作
                for (int i = 0; i < operationsPerConnection; i++)
                {
                    foreach (var client in tcpClients)
                    {
                        var data = GenerateTestData(1024);
                        tcpTasks.Add(client.SendAsync(data));
                    }
                }
                
                await Task.WhenAll(tcpTasks);
            }
            finally
            {
                // 清理TCP连接池
                foreach (var client in tcpClients)
                {
                    await client.Close();
                }
            }
        }

        /// <summary>
        /// 连接重用性能测试
        /// </summary>
        [Benchmark]
        public async Task ConnectionReusability()
        {
            // TCP连接重用
            var tcpDispatcher = new BenchmarkTcpClientEventDispatcher();
            var tcpConfig = CreateHighPerformanceTcpClientConfiguration();
            var tcpClient = new TcpSocketClient(IPAddress.Loopback, _tcpPort, tcpDispatcher, tcpConfig);
            
            try
            {
                await tcpClient.Connect();
                
                // 重复使用同一连接发送多条消息
                var data = GenerateTestData(1024);
                for (int i = 0; i < OperationCount; i++)
                {
                    await tcpClient.SendAsync(data);
                }
            }
            finally
            {
                await tcpClient.Close();
            }
        }

        /// <summary>
        /// 建立单个TCP连接
        /// </summary>
        private async Task EstablishTcpConnection()
        {
            var dispatcher = new BenchmarkTcpClientEventDispatcher();
            var config = CreateHighPerformanceTcpClientConfiguration();
            var client = new TcpSocketClient(IPAddress.Loopback, _tcpPort, dispatcher, config);
            
            try
            {
                await client.Connect();
            }
            finally
            {
                await client.Close();
            }
        }

        /// <summary>
        /// 建立单个UDP连接
        /// </summary>
        private async Task EstablishUdpConnection()
        {
            var dispatcher = new BenchmarkUdpClientEventDispatcher();
            var config = CreateHighPerformanceUdpClientConfiguration();
            var client = new UdpSocketClient(IPAddress.Loopback, _udpPort, dispatcher, config);
            
            try
            {
                await client.Connect();
            }
            finally
            {
                await client.Close();
            }
        }

        /// <summary>
        /// 建立单个WebSocket连接
        /// </summary>
        private async Task EstablishWebSocketConnection()
        {
            var dispatcher = new BenchmarkWebSocketClientDispatcher();
            var config = CreateHighPerformanceWebSocketClientConfiguration();
            var uri = new Uri($"ws://localhost:{_webSocketPort}/");
            var client = new WebSocketClient(uri, dispatcher, config);
            
            try
            {
                await client.ConnectAsync();
            }
            finally
            {
                if (client.State == WebSocketState.Open)
                {
                    await client.Close(WebSocketCloseCode.NormalClosure);
                }
            }
        }

        /// <summary>
        /// TCP连接完整生命周期
        /// </summary>
        private async Task TcpConnectionLifecycle()
        {
            var dispatcher = new BenchmarkTcpClientEventDispatcher();
            var config = CreateHighPerformanceTcpClientConfiguration();
            var client = new TcpSocketClient(IPAddress.Loopback, _tcpPort, dispatcher, config);
            
            try
            {
                // 建立连接
                await client.Connect();
                
                // 使用连接
                var data = GenerateTestData(1024);
                await client.SendAsync(data);
                
                // 短暂等待
                await Task.Delay(10);
            }
            finally
            {
                // 关闭连接
                await client.Close();
            }
        }

        /// <summary>
        /// UDP连接完整生命周期
        /// </summary>
        private async Task UdpConnectionLifecycle()
        {
            var dispatcher = new BenchmarkUdpClientEventDispatcher();
            var config = CreateHighPerformanceUdpClientConfiguration();
            var client = new UdpSocketClient(IPAddress.Loopback, _udpPort, dispatcher, config);
            
            try
            {
                // 建立连接
                await client.Connect();
                
                // 使用连接
                var data = GenerateTestData(1024);
                await client.SendAsync(data);
                
                // 短暂等待
                await Task.Delay(10);
            }
            finally
            {
                // 关闭连接
                await client.Close();
            }
        }

        /// <summary>
        /// WebSocket连接完整生命周期
        /// </summary>
        private async Task WebSocketConnectionLifecycle()
        {
            var dispatcher = new BenchmarkWebSocketClientDispatcher();
            var config = CreateHighPerformanceWebSocketClientConfiguration();
            var uri = new Uri($"ws://localhost:{_webSocketPort}/");
            var client = new WebSocketClient(uri, dispatcher, config);
            
            try
            {
                // 建立连接
                await client.ConnectAsync();
                
                // 使用连接
                await client.SendTextAsync("Hello WebSocket");
                
                // 短暂等待
                await Task.Delay(10);
            }
            finally
            {
                // 关闭连接
                if (client.State == WebSocketState.Open)
                {
                    await client.Close(WebSocketCloseCode.NormalClosure);
                }
            }
        }
    }
} 