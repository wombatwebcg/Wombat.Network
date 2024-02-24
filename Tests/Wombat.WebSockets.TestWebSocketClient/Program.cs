using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Wombat.Network.WebSockets;
using Wombat.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Wombat.WebSockets.TestWebSocketClient
{
    class Program
    {
        static WebSocketClient _client;

        static void Main(string[] args)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)//CategoryName以System开头的所有日志输出级别为Warning
                    .AddFilter<ConsoleLoggerProvider>("Wombat.WebSockets.TestWebSocketClient", LogLevel.Debug)
                    .AddConsole();//在loggerFactory中添加 ConsoleProvider
            });

            ILogger logger = loggerFactory.CreateLogger<Program>();
            logger.LogInformation("Example log message");

            Task.Run(async () =>
            {
                try
                {
                    var config = new WebSocketClientConfiguration();

                    //config.SslTargetHost = "Cowboy";
                    //config.SslClientCertificates.Add(new System.Security.Cryptography.X509Certificates.X509Certificate2(@"D:\\Cowboy.cer"));
                    //config.SslPolicyErrorsBypassed = true;

                    //var uri = new Uri("ws://echo.websocket.org/");   // connect to websocket.org website
                    //var uri = new Uri("wss://127.0.0.1:22222/test"); // use wss with ssl
                    var uri = new Uri("ws://127.0.0.1:22222/test");    // connect to localhost
                    _client = new WebSocketClient(uri,
                        OnServerTextReceived,
                        OnServerBinaryReceived,
                        OnServerConnected,
                        OnServerDisconnected,
                        config);
                    _client.UsgLogger(logger);
                    await _client.ConnectAsync();

                    Console.WriteLine("WebSocket client has connected to server [{0}].", uri);
                    Console.WriteLine("Type something to send to server...");
                    while (_client.State == ConnectionState.Connected)
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
                                    text = "";
                                    for (int i = 0; i < 10000; i++)
                                    {
                                        text += $"{i},";
                                    }
                                    Stopwatch watch = Stopwatch.StartNew();
                                    for (int i = 0; i <= 1000; i++)
                                    {
                                         _client.SendBinary(Encoding.UTF8.GetBytes(text));
                                        Console.WriteLine("Client [{0}] send binary -> Sequence[{1}] -> TextLength[{2}].",
                                            _client.LocalEndPoint, text, text.Length);
                                    }
                                    watch.Stop();
                                    Console.WriteLine("Client [{0}] send binary -> Count[{1}] -> Cost[{2}] -> PerSecond[{3}].",
                                        _client.LocalEndPoint, text.Length, watch.ElapsedMilliseconds / 1000, text.Length / (watch.ElapsedMilliseconds / 1000));
                                }
                                else if (text == "big1")
                                {
                                    text = new string('x', 1024 * 1024 * 1);
                                    await _client.SendBinaryAsync(Encoding.UTF8.GetBytes(text));
                                    Console.WriteLine("Client [{0}] send binary -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
                                }
                                else if (text == "big10")
                                {
                                    text = new string('x', 1024 * 1024 * 10);
                                    await _client.SendBinaryAsync(Encoding.UTF8.GetBytes(text));
                                    Console.WriteLine("Client [{0}] send binary -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
                                }
                                else if (text == "big100")
                                {
                                    text = new string('x', 1024 * 1024 * 100);
                                    await _client.SendBinaryAsync(Encoding.UTF8.GetBytes(text));
                                    Console.WriteLine("Client [{0}] send binary -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
                                }
                                else if (text == "big1000")
                                {
                                    text = new string('x', 1024 * 1024 * 1000);
                                    await _client.SendBinaryAsync(Encoding.UTF8.GetBytes(text));
                                    Console.WriteLine("Client [{0}] send binary -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
                                }
                                else
                                {
                                    await _client.SendBinaryAsync(Encoding.UTF8.GetBytes(text));
                                    Console.WriteLine("Client [{0}] send binary -> [{1}].", _client.LocalEndPoint, text);

                                    //await _client.SendTextAsync(text);
                                    //Console.WriteLine("Client [{0}] send text -> [{1}].", _client.LocalEndPoint, text);
                                }
                            }).Forget();
                        }
                        catch (Exception ex)
                        {
                           logger.LogError(ex.Message, ex);
                        }
                    }

                    await _client.Close(WebSocketCloseCode.NormalClosure);
                    Console.WriteLine("WebSocket client has disconnected from server [{0}].", uri);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.Message, ex);
                }
            }).Wait();

            Console.ReadKey();
        }

        private static async Task OnServerConnected(WebSocketClient client)
        {
            Console.WriteLine(string.Format("WebSocket server [{0}] has connected.", client.RemoteEndPoint));
            await Task.CompletedTask;
        }

        private static async Task OnServerTextReceived(WebSocketClient client, string text)
        {
            Console.Write(string.Format("WebSocket server [{0}] received Text --> ", client.RemoteEndPoint));
            Console.WriteLine(string.Format("{0}", text));

            await Task.CompletedTask;
        }

        private static async Task OnServerBinaryReceived(WebSocketClient client, byte[] data, int offset, int count)
        {
            var text = Encoding.UTF8.GetString(data, offset, count);
            Console.Write(string.Format("WebSocket server [{0}] received Binary --> ", client.RemoteEndPoint));
            if (count < 1024 * 1024 * 1)
            {
                Console.WriteLine(text);
            }
            else
            {
                Console.WriteLine("{0} Bytes", count);
            }

            await Task.CompletedTask;
        }

        private static async Task OnServerDisconnected(WebSocketClient client)
        {
            Console.WriteLine(string.Format("WebSocket server [{0}] has disconnected.", client.RemoteEndPoint));
            await Task.CompletedTask;
        }
    }
}
