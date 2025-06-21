using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Wombat.Network.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Wombat.Network;

namespace Wombat.Socket.TestTcpSocketServer
{
    class Program
    {

        static TcpSocketServer _server;
        static ILogger _logger;
        static TcpSocketServerConfiguration _config;

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

                _config = new TcpSocketServerConfiguration();
                //config.UseSsl = true;
                //config.SslServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(@"D:\\Cowboy.pfx", "Cowboy");
                //config.SslPolicyErrorsBypassed = false;

                //config.FrameBuilder = new FixedLengthFrameBuilder(20000);
                //config.FrameBuilder = new RawBufferFrameBuilder();
                //config.FrameBuilder = new LineBasedFrameBuilder();
                //config.FrameBuilder = new LengthPrefixedFrameBuilder();
                _config.FrameBuilder = new LengthFieldBasedFrameBuilder();

                // 启用心跳
                _config.EnableHeartbeat = true;
                _config.HeartbeatInterval = TimeSpan.FromSeconds(10);  // 每10秒发送一次心跳
                _config.HeartbeatTimeout = TimeSpan.FromSeconds(30);   // 30秒未收到心跳则超时
                _config.MaxMissedHeartbeats = 3;                       // 允许最多3次心跳丢失

                _server = new TcpSocketServer(22222, new SimpleEventDispatcher(), _config);
                // 设置日志记录器
                _server.UseLogger(_logger);
                _server.Listen();

                Console.WriteLine("TCP server has been started on [{0}].", _server.ListenedEndPoint);
                Console.WriteLine("Type something to send to clients...");
                Console.WriteLine("Special commands:");
                Console.WriteLine("  quit - Exit the program");
                Console.WriteLine("  heartbeat - Show heartbeat status");
                Console.WriteLine("  heartbeat-on - Enable heartbeat (for new connections)");
                Console.WriteLine("  heartbeat-off - Disable heartbeat (for new connections)");
                Console.WriteLine("  heartbeat-interval <seconds> - Set heartbeat interval (for new connections)");
                Console.WriteLine("  sessions - Show active client sessions");
                Console.WriteLine("  session-info <sessionKey> - Show detailed session information");
                Console.WriteLine("  close-session <sessionKey> - Close a specific session");

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
                            Console.WriteLine("Heartbeat enabled for new connections");
                            continue;
                        }
                        else if (text == "heartbeat-off")
                        {
                            _config.EnableHeartbeat = false;
                            Console.WriteLine("Heartbeat disabled for new connections");
                            continue;
                        }
                        else if (text.StartsWith("heartbeat-interval "))
                        {
                            string[] parts = text.Split(' ');
                            if (parts.Length == 2 && int.TryParse(parts[1], out int seconds))
                            {
                                _config.HeartbeatInterval = TimeSpan.FromSeconds(seconds);
                                Console.WriteLine($"Heartbeat interval set to {seconds} seconds for new connections");
                            }
                            else
                            {
                                Console.WriteLine("Invalid format. Use: heartbeat-interval <seconds>");
                            }
                            continue;
                        }
                        else if (text == "sessions")
                        {
                            Console.WriteLine($"Active sessions: {_server.SessionCount}");
                            continue;
                        }
                        else if (text.StartsWith("session-info "))
                        {
                            string[] parts = text.Split(' ');
                            if (parts.Length == 2)
                            {
                                string sessionKey = parts[1];
                                var session = _server.GetSession(sessionKey);
                                if (session != null)
                                {
                                    Console.WriteLine($"Session [{sessionKey}]:");
                                    Console.WriteLine($"  Remote endpoint: {session.RemoteEndPoint}");
                                    Console.WriteLine($"  Local endpoint: {session.LocalEndPoint}");
                                    Console.WriteLine($"  State: {session.State}");
                                    Console.WriteLine($"  Start time: {session.StartTime}");
                                }
                                else
                                {
                                    Console.WriteLine($"Session [{sessionKey}] not found");
                                }
                            }
                            else
                            {
                                Console.WriteLine("Invalid format. Use: session-info <sessionKey>");
                            }
                            continue;
                        }
                        else if (text.StartsWith("close-session "))
                        {
                            string[] parts = text.Split(' ');
                            if (parts.Length == 2)
                            {
                                string sessionKey = parts[1];
                                if (_server.HasSession(sessionKey))
                                {
                                    Task.Run(async () =>
                                    {
                                        await _server.CloseSession(sessionKey);
                                        Console.WriteLine($"Session [{sessionKey}] closed");
                                    });
                                }
                                else
                                {
                                    Console.WriteLine($"Session [{sessionKey}] not found");
                                }
                            }
                            else
                            {
                                Console.WriteLine("Invalid format. Use: close-session <sessionKey>");
                            }
                            continue;
                        }

                        Task.Run(async () =>
                        {
                            if (text == "many")
                            {
                                text = new string('x', 8192);
                                for (int i = 0; i < 1000000; i++)
                                {
                                    await _server.BroadcastAsync(Encoding.UTF8.GetBytes(text));
                                    Console.WriteLine("Server [{0}] broadcasts text -> [{1}].", _server.ListenedEndPoint, text);
                                }
                            }
                            else if (text == "big1k")
                            {
                                text = new string('x', 1024 * 1);
                                await _server.BroadcastAsync(Encoding.UTF8.GetBytes(text));
                                Console.WriteLine("Server [{0}] broadcasts text -> [{1} Bytes].", _server.ListenedEndPoint, text.Length);
                            }
                            else if (text == "big10k")
                            {
                                text = new string('x', 1024 * 10);
                                await _server.BroadcastAsync(Encoding.UTF8.GetBytes(text));
                                Console.WriteLine("Server [{0}] broadcasts text -> [{1} Bytes].", _server.ListenedEndPoint, text.Length);
                            }
                            else if (text == "big100k")
                            {
                                text = new string('x', 1024 * 100);
                                await _server.BroadcastAsync(Encoding.UTF8.GetBytes(text));
                                Console.WriteLine("Server [{0}] broadcasts text -> [{1} Bytes].", _server.ListenedEndPoint, text.Length);
                            }
                            else if (text == "big1m")
                            {
                                text = new string('x', 1024 * 1024 * 1);
                                await _server.BroadcastAsync(Encoding.UTF8.GetBytes(text));
                                Console.WriteLine("Server [{0}] broadcasts text -> [{1} Bytes].", _server.ListenedEndPoint, text.Length);
                            }
                            else if (text == "big10m")
                            {
                                text = new string('x', 1024 * 1024 * 10);
                                await _server.BroadcastAsync(Encoding.UTF8.GetBytes(text));
                                Console.WriteLine("Server [{0}] broadcasts text -> [{1} Bytes].", _server.ListenedEndPoint, text.Length);
                            }
                            else if (text == "big100m")
                            {
                                text = new string('x', 1024 * 1024 * 100);
                                await _server.BroadcastAsync(Encoding.UTF8.GetBytes(text));
                                Console.WriteLine("Server [{0}] broadcasts text -> [{1} Bytes].", _server.ListenedEndPoint, text.Length);
                            }
                            else if (text == "big1g")
                            {
                                text = new string('x', 1024 * 1024 * 1024);
                                await _server.BroadcastAsync(Encoding.UTF8.GetBytes(text));
                                Console.WriteLine("Server [{0}] broadcasts text -> [{1} Bytes].", _server.ListenedEndPoint, text.Length);
                            }
                            else
                            {
                                await _server.BroadcastAsync(Encoding.UTF8.GetBytes(text));
                                Console.WriteLine("Server [{0}] broadcasts text -> [{1} Bytes].", _server.ListenedEndPoint, text.Length);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error processing command: {Message}", ex.Message);
                    }
                }

                _server.Shutdown();
                Console.WriteLine("TCP server has been stopped on [{0}].", _server.ListenedEndPoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"服务器异常: {ex.Message}");
                _logger?.LogError(ex, "Server error: {Message}", ex.Message);
            }

            Console.ReadKey();
        }
    }
}
