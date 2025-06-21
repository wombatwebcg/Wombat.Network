using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network;
using Wombat.Network.Sockets;

namespace Wombat.Socket.TestTcpSocketClient 
{
    class Program
    {
        static TcpSocketClient _client;
        static ILogger _logger;
        static TcpSocketClientConfiguration _config;

        static void Main(string[] args)
        {

            try
            {
                // 配置日志记录器
                ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Debug);
                });
                _logger = loggerFactory.CreateLogger<Program>();

                _config = new TcpSocketClientConfiguration();
                //config.UseSsl = true;
                //config.SslTargetHost = "Cowboy";
                //config.SslClientCertificates.Add(new System.Security.Cryptography.X509Certificates.X509Certificate2(@"D:\\Cowboy.cer"));
                //config.SslPolicyErrorsBypassed = false;

                //config.FrameBuilder = new FixedLengthFrameBuilder(20000);
                //config.FrameBuilder = new RawBufferFrameBuilder();
                _config.FrameBuilder = new LineBasedFrameBuilder();
                //config.FrameBuilder = new LengthPrefixedFrameBuilder();
                _config.FrameBuilder = new LengthFieldBasedFrameBuilder();

                // 配置心跳
                _config.EnableHeartbeat = true;
                _config.HeartbeatInterval = TimeSpan.FromSeconds(15);   // 每5秒发送一次心跳
                _config.HeartbeatTimeout = TimeSpan.FromSeconds(30);   // 15秒未收到心跳则超时
                _config.MaxMissedHeartbeats = 2;                       // 允许最多3次心跳丢失

                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 22222);
                _client = new TcpSocketClient(remoteEP, new SimpleEventDispatcher(), _config);
                // 设置日志记录器
                _client.UseLogger(_logger);

                // 创建一个取消令牌源
                var cancellationTokenSource = new CancellationTokenSource();

                //// 设置连接的超时时间（可选）
                //cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(10));  // 设置超时为10秒

                // 调用 Connect 方法，传递 CancellationToken
                _client.Connect().ConfigureAwait(false).GetAwaiter().GetResult(); 
                Console.WriteLine("连接成功！");
            
                Console.WriteLine("TCP client has connected to server [{0}].", remoteEP);
                Console.WriteLine("Type something to send to server...");
                Console.WriteLine("Special commands:");
                Console.WriteLine("  quit - Exit the program");
                Console.WriteLine("  heartbeat - Show heartbeat status");
                Console.WriteLine("  heartbeat-on - Enable heartbeat");
                Console.WriteLine("  heartbeat-off - Disable heartbeat");
                Console.WriteLine("  heartbeat-interval <seconds> - Set heartbeat interval");
                Console.WriteLine("  buffer - Show buffer statistics");
                Console.WriteLine("  force-heartbeat - Force send a heartbeat packet immediately");
                Console.WriteLine("  connection-status - Show connection status");

                while (true)
                {
                    try
                    {
                        string text = Console.ReadLine();
                        if (text == "quit")
                            break;

                        // 处理特殊命令
                        if (text == "heartbeat")
                        {
                            Console.WriteLine("Heartbeat configuration:");
                            Console.WriteLine($"  Enabled: {_config.EnableHeartbeat}");
                            Console.WriteLine($"  Interval: {_config.HeartbeatInterval.TotalSeconds} seconds");
                            Console.WriteLine($"  Timeout: {_config.HeartbeatTimeout.TotalSeconds} seconds");
                            Console.WriteLine($"  Max Missed: {_config.MaxMissedHeartbeats}");
                            Console.WriteLine("Heartbeat status is displayed in log messages.");
                            continue;
                        }
                        else if (text == "heartbeat-on")
                        {
                            _config.EnableHeartbeat = true;
                            Console.WriteLine("Heartbeat enabled");
                            // 注意：需要重新连接才能生效
                            Console.WriteLine("Note: Reconnection may be needed for changes to take effect");
                            continue;
                        }
                        else if (text == "heartbeat-off")
                        {
                            _config.EnableHeartbeat = false;
                            Console.WriteLine("Heartbeat disabled");
                            // 注意：需要重新连接才能生效 
                            Console.WriteLine("Note: Reconnection may be needed for changes to take effect");
                            continue;
                        }
                        else if (text.StartsWith("heartbeat-interval "))
                        {
                            string[] parts = text.Split(' ');
                            if (parts.Length == 2 && int.TryParse(parts[1], out int seconds))
                            {
                                _config.HeartbeatInterval = TimeSpan.FromSeconds(seconds);
                                Console.WriteLine($"Heartbeat interval set to {seconds} seconds");
                                // 注意：需要重新连接才能生效
                                Console.WriteLine("Note: Reconnection may be needed for changes to take effect");
                            }
                            else
                            {
                                Console.WriteLine("Invalid format. Use: heartbeat-interval <seconds>");
                            }
                            continue;
                        }
                        else if (text == "buffer")
                        {
                            Console.WriteLine("Buffer statistics:");
                            Console.WriteLine($"  Bytes Available: {_client.BytesAvailable}");
                            Console.WriteLine($"  Buffer Usage: {_client.BufferUsage:F2}%");
                            Console.WriteLine($"  Buffer Full: {_client.IsBufferFull}");
                            continue;
                        }
                        else if (text == "force-heartbeat")
                        {
                            // 发送一个心跳包用于测试
                            Task.Run(async () =>
                            {
                                try
                                {
                                    byte[] heartbeatPacket = HeartbeatManager.CreateHeartbeatPacket();
                                    await _client.SendAsync(heartbeatPacket);
                                    Console.WriteLine("Forced heartbeat packet sent");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error sending heartbeat: {ex.Message}");
                                }
                            });
                            continue;
                        }
                        else if (text == "connection-status")
                        {
                            Console.WriteLine($"Connection status: {_client.State}");
                            Console.WriteLine($"Connected: {_client.Connected}");
                            Console.WriteLine($"Remote endpoint: {_client.RemoteEndPoint}");
                            Console.WriteLine($"Local endpoint: {_client.LocalEndPoint}");
                            continue;
                        }

                        Task.Run(async () =>
                        {
                            if (text == "many")
                            {
                                text = new string('x', 8192);
                                for (int i = 0; i < 1000000; i++)
                                {
                                    await _client.SendAsync(Encoding.UTF8.GetBytes(text),cancellationTokenSource.Token);
                                    Console.WriteLine("Client [{0}] send text -> [{1}].", _client.LocalEndPoint, text);
                                }
                            }
                            else if (text == "big1k")
                            {
                                text = new string('x', 1024 * 1);
                                await _client.SendAsync(Encoding.UTF8.GetBytes(text), cancellationTokenSource.Token);
                                Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
                            }
                            else if (text == "big10k")
                            {
                                text = new string('x', 1024 * 10);
                                await _client.SendAsync(Encoding.UTF8.GetBytes(text), cancellationTokenSource.Token);
                                Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
                                Thread.Sleep(2000);
                                var read = _client.ReadAllBytes();
                            }
                            else if (text == "big100k")
                            {
                                text = new string('x', 1024 * 100);
                                await _client.SendAsync(Encoding.UTF8.GetBytes(text), cancellationTokenSource.Token);
                                Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
                            }
                            else if (text == "big1m")
                            {
                                text = new string('x', 1024 * 1024 * 1);
                                await _client.SendAsync(Encoding.UTF8.GetBytes(text), cancellationTokenSource.Token);
                                Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
                            }
                            else if (text == "big10m")
                            {
                                text = new string('x', 1024 * 1024 * 10);
                                await _client.SendAsync(Encoding.UTF8.GetBytes(text), cancellationTokenSource.Token);
                                Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
                            }
                            else if (text == "big100m")
                            {
                                text = new string('x', 1024 * 1024 * 100);
                                await _client.SendAsync(Encoding.UTF8.GetBytes(text), cancellationTokenSource.Token);
                                Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
                            }
                            else if (text == "big1g")
                            {
                                try
                                {
                                    // 每个字符为 2 个字节（UTF-16 编码）
                                    long targetSizeInBytes = 1L * 1024 * 1024 * 1024; // 1GB
                                    long targetLength = targetSizeInBytes/2 ; // 字符串长度

                                    char fillChar = 'A'; // 用于填充的字符

                                    // 使用 String 构造函数创建大字符串
                                    string largeString = new string(fillChar, (int)Math.Min(targetLength, int.MaxValue));

                                    // 如果需要更大的字符串，可以分块拼接
                                    while (largeString.Length < targetLength)
                                    {
                                        largeString += largeString;
                                        if (largeString.Length > targetLength)
                                        {
                                            largeString = largeString.Substring(0, (int)targetLength);
                                        }
                                    }


                                    //StringBuilder sb = new StringBuilder();
                                    //sb.Append('x', 1024 * 1024 * 1024);  // 1GB
                                    //text = sb.ToString();
                                    await _client.SendAsync(Encoding.UTF8.GetBytes(largeString), cancellationTokenSource.Token);
                                    Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client.LocalEndPoint, largeString.Length);
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogError(ex, "发送大数据时出错：{Message}", ex.Message);
                                }
                            }
                            else
                            {
                                await _client.SendAsync(Encoding.UTF8.GetBytes(text), cancellationTokenSource.Token);
                                Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "处理命令时出错：{Message}", ex.Message);
                    }
                }

                _client.Shutdown();
                Console.WriteLine("TCP client has disconnected from server [{0}].", remoteEP);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"客户端异常: {ex.Message}");
                _logger?.LogError(ex, "Client error: {Message}", ex.Message);
            }

            Console.ReadKey();
        }
    }
}
