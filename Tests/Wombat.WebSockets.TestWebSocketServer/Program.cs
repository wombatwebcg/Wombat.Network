using System;
using Wombat.Network.WebSockets;
using System.Threading.Tasks;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System.Net;

namespace Wombat.WebSockets.TestWebSocketServer
{
    class Program
    {
        static WebSocketServer _server;

        static void Main(string[] args)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)//CategoryName以System开头的所有日志输出级别为Warning
                    .AddFilter<ConsoleLoggerProvider>("Wombat.WebSockets.TestWebSocketServer", LogLevel.Debug)
                    .AddConsole();//在loggerFactory中添加 ConsoleProvider
            });

            ILogger logger = loggerFactory.CreateLogger<Program>();
            logger.LogInformation("Example log message");

            try
            {
                var catalog = new AsyncWebSocketServerModuleCatalog();
                catalog.RegisterModule(new TestWebSocketModule());

                var config = new WebSocketServerConfiguration();
                //config.SslEnabled = true;
                //config.SslServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(@"D:\\Cowboy.pfx", "Cowboy");
                //config.SslPolicyErrorsBypassed = true;
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 22222);

                _server = new WebSocketServer(remoteEP, catalog, config);
                _server.UsgLogger(logger);
                _server.Listen();

                Console.WriteLine("WebSocket server has been started on [{0}].", _server.ListenedEndPoint);
                Console.WriteLine("Type something to send to clients...");
                while (true)
                {
                    try
                    {
                        string text = Console.ReadLine();
                        if (text == "quit")
                            break;
                        Task.Run(async () =>
                        {
                            if (text == "many")
                            {
                                text = new string('x', 8192);
                                for (int i = 0; i < 1000000; i++)
                                {
                                    await _server.BroadcastBinaryAsync(Encoding.UTF8.GetBytes(text));
                                    Console.WriteLine("WebSocket server [{0}] broadcasts binary -> [{1}].", _server.ListenedEndPoint, text);
                                }
                            }
                            else if (text == "big1")
                            {
                                text = new string('x', 1024 * 1024 * 1);
                                await _server.BroadcastBinaryAsync(Encoding.UTF8.GetBytes(text));
                                Console.WriteLine("WebSocket server [{0}] broadcasts binary -> [{1} Bytes].", _server.ListenedEndPoint, text.Length);
                            }
                            else if (text == "big10")
                            {
                                text = new string('x', 1024 * 1024 * 10);
                                await _server.BroadcastBinaryAsync(Encoding.UTF8.GetBytes(text));
                                Console.WriteLine("WebSocket server [{0}] broadcasts binary -> [{1} Bytes].", _server.ListenedEndPoint, text.Length);
                            }
                            else if (text == "big100")
                            {
                                text = new string('x', 1024 * 1024 * 100);
                                await _server.BroadcastBinaryAsync(Encoding.UTF8.GetBytes(text));
                                Console.WriteLine("WebSocket server [{0}] broadcasts binary -> [{1} Bytes].", _server.ListenedEndPoint, text.Length);
                            }
                            else if (text == "big1000")
                            {
                                text = new string('x', 1024 * 1024 * 1000);
                                await _server.BroadcastBinaryAsync(Encoding.UTF8.GetBytes(text));
                                Console.WriteLine("WebSocket server [{0}] broadcasts binary -> [{1} Bytes].", _server.ListenedEndPoint, text.Length);
                            }
                            else
                            {
                                await _server.BroadcastBinaryAsync(Encoding.UTF8.GetBytes(text));
                                Console.WriteLine("WebSocket server [{0}] broadcasts binary -> [{1}].", _server.ListenedEndPoint, text);
                            }

                            //await _server.BroadcastText(text);
                            //Console.WriteLine("WebSocket server [{0}] broadcasts text -> [{1}].", _server.ListenedEndPoint, text);
                        });
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex.Message, ex);
                    }
                }

                _server.Shutdown();
                Console.WriteLine("WebSocket server has been stopped on [{0}].", _server.ListenedEndPoint);
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
            }

            Console.ReadKey();
        }
    }
}
