using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wombat.Network.Sockets;
using Wombat.Network.WebSockets;
using Xunit;

namespace Wombat.NetworkTests
{
    /// <summary>
    /// 服务器和客户端互操作测试
    /// </summary>
    public class ServerClientTests
    {
        private readonly ILogger _logger;
        
        public ServerClientTests()
        {
            // 创建简单的控制台日志记录器
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });
            
            _logger = loggerFactory.CreateLogger<ServerClientTests>();
        }
        
        [Fact]
        public async Task TcpSocketTest_ServerClientInteroperation()
        {
            // 准备
            int port = 50001; // 使用固定端口而不是随机端口
            string testMessage = "Hello, TCP Server!";
            var receivedMessage = new TaskCompletionSource<string>();
            
            // 创建服务器
            var server = new TcpSocketServer(
                IPAddress.Loopback, port,
                onSessionDataReceived: (session, data, offset, count) =>
                {
                    var message = Encoding.UTF8.GetString(data, offset, count);
                    _logger.LogInformation("服务器收到消息: {0}", message);
                    receivedMessage.TrySetResult(message);
                    
                    // 回复客户端
                    var response = Encoding.UTF8.GetBytes($"Server received: {message}");
                    return session.SendAsync(response);
                });
            
            server.UseLogger(_logger);
            
            try
            {
                // 启动服务器
                server.Listen();
                _logger.LogInformation("TCP服务器已启动");
                
                // 创建并连接客户端
                var clientReceivedData = new TaskCompletionSource<string>();
                
                var client = new TcpSocketClient(
                    IPAddress.Loopback, port,
                    onServerDataReceived: (client, data, offset, count) =>
                    {
                        var message = Encoding.UTF8.GetString(data, offset, count);
                        _logger.LogInformation("客户端收到响应: {0}", message);
                        clientReceivedData.TrySetResult(message);
                        return Task.CompletedTask;
                    });
                
                client.UseLogger(_logger);
                
                // 连接到服务器
                await client.Connect();
                _logger.LogInformation("客户端已连接到服务器");
                
                // 发送测试数据
                var testData = Encoding.UTF8.GetBytes(testMessage);
                await client.SendAsync(testData);
                _logger.LogInformation("客户端已发送消息");
                
                // 等待数据交换完成
                var timeout = TimeSpan.FromSeconds(5);
                
                using (var cts = new CancellationTokenSource(timeout))
                {
                    var serverTask = await Task.WhenAny(receivedMessage.Task, Task.Delay(timeout, cts.Token));
                    var clientTask = await Task.WhenAny(clientReceivedData.Task, Task.Delay(timeout, cts.Token));
                    
                    // 验证服务器收到的消息
                    if (serverTask == receivedMessage.Task)
                    {
                        var serverReceivedMsg = await receivedMessage.Task;
                        Assert.Equal(testMessage, serverReceivedMsg);
                        _logger.LogInformation("服务器成功接收并验证了消息");
                    }
                    else
                    {
                        Assert.True(false, "服务器未在超时时间内收到消息");
                    }
                    
                    // 验证客户端收到的响应
                    if (clientTask == clientReceivedData.Task)
                    {
                        var clientReceivedMsg = await clientReceivedData.Task;
                        Assert.Contains(testMessage, clientReceivedMsg);
                        _logger.LogInformation("客户端成功接收并验证了响应");
                    }
                    else
                    {
                        Assert.True(false, "客户端未在超时时间内收到响应");
                    }
                }
                
                _logger.LogInformation("TCP Socket测试完成");
            }
            finally
            {
                // 确保资源被释放
                server.Shutdown();
            }
        }
        
        [Fact]
        public async Task WebSocketTest_ServerClientInteroperation()
        {
            // 准备
            int port = 50002; // 使用固定端口而不是随机端口
            string testMessage = "Hello, WebSocket Server!";
            
            // 创建WebSocket服务器模块目录
            var moduleCatalog = new AsyncWebSocketServerModuleCatalog();
            
            // 添加测试模块
            var handler = new TestWebSocketModule();
            moduleCatalog.RegisterModule(handler);
            
            // 创建服务器
            var server = new WebSocketServer(
                IPAddress.Loopback, port, 
                moduleCatalog);
            
            server.UsgLogger(_logger);
            
            try
            {
                // 启动服务器
                server.Listen();
                _logger.LogInformation("WebSocket服务器已启动");
                
                // 创建客户端连接
                var uri = new Uri($"ws://127.0.0.1:{port}/test");
                var clientReceivedText = new TaskCompletionSource<string>();
                
                // 使用自定义的消息调度器
                var dispatcher = new TestWebSocketClientMessageDispatcher(
                    onTextReceived: (client, text) => {
                        _logger.LogInformation("WebSocket客户端收到文本: {0}", text);
                        clientReceivedText.TrySetResult(text);
                        return Task.CompletedTask;
                    }
                );
                
                var client = new WebSocketClient(uri, dispatcher);
                if (_logger != null) client.UseLogger(_logger);
                
                // 连接到服务器
                await client.ConnectAsync();
                _logger.LogInformation("WebSocket客户端已连接到服务器");
                
                // 发送测试消息
                await client.SendTextAsync(testMessage);
                _logger.LogInformation("WebSocket客户端已发送消息");
                
                // 等待客户端接收响应
                var timeout = TimeSpan.FromSeconds(5);
                
                using (var cts = new CancellationTokenSource(timeout))
                {
                    var task = await Task.WhenAny(clientReceivedText.Task, Task.Delay(timeout, cts.Token));
                    
                    if (task == clientReceivedText.Task)
                    {
                        var response = await clientReceivedText.Task;
                        Assert.Contains(testMessage, response);
                        _logger.LogInformation("WebSocket测试成功完成");
                    }
                    else
                    {
                        Assert.True(false, "WebSocket客户端未在超时时间内收到响应");
                    }
                }
                
                // 关闭连接
                await client.Close(WebSocketCloseCode.NormalClosure);
            }
            finally
            {
                // 确保资源被释放
                server.Shutdown();
            }
        }
        
        /// <summary>
        /// 测试用WebSocket模块
        /// </summary>
        private class TestWebSocketModule : AsyncWebSocketServerModule
        {
            public TestWebSocketModule() : base("test")
            {
                // 构造函数指定模块路径为"test"
            }
            
            public override Task OnSessionTextReceived(WebSocketSession session, string text)
            {
                // 将接收到的文本消息回显给客户端
                return session.SendTextAsync($"Echo: {text}");
            }
            
            public override Task OnSessionBinaryReceived(WebSocketSession session, byte[] data, int offset, int count)
            {
                // 将接收到的二进制消息回显给客户端
                return session.SendBinaryAsync(data, offset, count);
            }
        }
        
        /// <summary>
        /// 测试用WebSocket客户端消息调度器
        /// </summary>
        private class TestWebSocketClientMessageDispatcher : IWebSocketClientMessageDispatcher
        {
            private readonly Func<WebSocketClient, string, Task> _onTextReceived;
            
            public TestWebSocketClientMessageDispatcher(Func<WebSocketClient, string, Task> onTextReceived)
            {
                _onTextReceived = onTextReceived;
            }
            
            public Task OnServerTextReceived(WebSocketClient client, string text)
            {
                return _onTextReceived?.Invoke(client, text) ?? Task.CompletedTask;
            }
            
            public Task OnServerBinaryReceived(WebSocketClient client, byte[] data, int offset, int count)
            {
                return Task.CompletedTask;
            }
            
            public Task OnServerConnected(WebSocketClient client)
            {
                return Task.CompletedTask;
            }
            
            public Task OnServerDisconnected(WebSocketClient client)
            {
                return Task.CompletedTask;
            }
            
            public Task OnServerFragmentationStreamOpened(WebSocketClient client, byte[] data, int offset, int count)
            {
                return Task.CompletedTask;
            }
            
            public Task OnServerFragmentationStreamContinued(WebSocketClient client, byte[] data, int offset, int count)
            {
                return Task.CompletedTask;
            }
            
            public Task OnServerFragmentationStreamClosed(WebSocketClient client, byte[] data, int offset, int count)
            {
                return Task.CompletedTask;
            }
        }
    }
} 