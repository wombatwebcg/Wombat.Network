using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO.Pipelines;
using Wombat.Network.Pipelines;
using Xunit;

namespace Wombat.NetworkTests
{
    /// <summary>
    /// PipelineSocketConnection测试类
    /// </summary>
    public class PipelineConnectionTests
    {
        private readonly ILogger _logger;
        
        public PipelineConnectionTests()
        {
            // 创建简单的控制台日志记录器
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });
            
            _logger = loggerFactory.CreateLogger<PipelineConnectionTests>();
        }
        
        [Fact]
        public async Task PipelineSocketConnection_BasicSendReceiveTest()
        {
            // 准备
            int port = 50003; // 使用固定端口
            string testMessage = "Hello, Pipeline Socket!";
            var receivedMessage = new TaskCompletionSource<string>();
            var clientReceivedData = new TaskCompletionSource<string>();
            
            // 创建监听器
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            
            try
            {
                // 在后台任务中接受连接
                var acceptTask = Task.Run(async () =>
                {
                    var serverSocket = await listener.AcceptSocketAsync();
                    _logger.LogInformation("服务器接受了连接");
                    
                    // 使用PipelineSocketConnection包装服务器Socket
                    var serverConnection = new PipelineSocketConnection(serverSocket, _logger);
                    serverConnection.Start();
                    
                    // 等待接收到消息
                    try
                    {
                        // 从管道读取数据
                        ReadResult result = await serverConnection.Input.ReadAsync();
                        if (!result.Buffer.IsEmpty)
                        {
                            // 复制数据到一个临时缓冲区
                            byte[] data = new byte[result.Buffer.Length];
                            CopyReadOnlySequenceToArray(result.Buffer, data);
                            
                            // 解码为字符串
                            string message = Encoding.UTF8.GetString(data);
                            _logger.LogInformation("服务器收到消息: {0}", message);
                            receivedMessage.TrySetResult(message);
                            
                            // 标记已处理的数据
                            serverConnection.Input.AdvanceTo(result.Buffer.End);
                            
                            // 发送回复
                            byte[] response = Encoding.UTF8.GetBytes($"Echo: {message}");
                            await serverConnection.SendDataAsync(response);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "服务器读取错误");
                    }
                    
                    return serverConnection; // 返回连接以便稍后清理
                });
                
                // 创建客户端Socket并连接
                var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await clientSocket.ConnectAsync(IPAddress.Loopback, port);
                _logger.LogInformation("客户端已连接到服务器");
                
                // 使用PipelineSocketConnection包装客户端Socket
                var clientConnection = new PipelineSocketConnection(clientSocket, _logger);
                clientConnection.Start();
                
                // 发送测试数据
                byte[] testData = Encoding.UTF8.GetBytes(testMessage);
                await clientConnection.SendDataAsync(testData);
                _logger.LogInformation("客户端发送消息: {0}", testMessage);
                
                // 接收服务器响应
                try
                {
                    // 从管道读取数据
                    ReadResult result = await clientConnection.Input.ReadAsync();
                    if (!result.Buffer.IsEmpty)
                    {
                        // 复制数据到一个临时缓冲区
                        byte[] data = new byte[result.Buffer.Length];
                        CopyReadOnlySequenceToArray(result.Buffer, data);
                        
                        // 解码为字符串
                        string response = Encoding.UTF8.GetString(data);
                        _logger.LogInformation("客户端收到响应: {0}", response);
                        clientReceivedData.TrySetResult(response);
                        
                        // 标记已处理的数据
                        clientConnection.Input.AdvanceTo(result.Buffer.End);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "客户端读取错误");
                }
                
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
                
                _logger.LogInformation("Pipeline Socket测试完成");
                
                // 清理服务器连接
                var serverConnection = await acceptTask;
                await serverConnection.StopAsync();
                
                // 清理客户端连接
                await clientConnection.StopAsync();
            }
            finally
            {
                // 清理资源
                listener.Stop();
            }
        }
        
        /// <summary>
        /// 将ReadOnlySequence<byte>复制到byte[]数组
        /// </summary>
        private static void CopyReadOnlySequenceToArray(ReadOnlySequence<byte> source, byte[] destination)
        {
            int position = 0;
            
            foreach (var segment in source)
            {
                segment.Span.CopyTo(new Span<byte>(destination, position, segment.Length));
                position += segment.Length;
            }
        }
    }
} 