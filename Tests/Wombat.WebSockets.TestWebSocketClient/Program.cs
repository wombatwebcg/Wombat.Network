using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Wombat.Network.WebSockets;
using Wombat.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System.Security.Cryptography;
using System.Collections.Generic;
using Wombat.Network.WebSockets.Extensions;
using Wombat.Network.WebSockets.SubProtocols;
using System.Text.Json.Serialization;
using Wombat.Extensions.DataTypeExtensions;
using Newtonsoft.Json;

namespace Wombat.WebSockets.TestWebSocketClient
{

    class Program
    {
        static WebSocketClient _client;

        static async Task Main(string[] args)
        {

            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)//CategoryName以System开头的所有日志输出级别为Warning
                    .AddFilter<ConsoleLoggerProvider>("Wombat.WebSockets.TestWebSocketClient", LogLevel.Debug)
                    .AddConsole();//在loggerFactory中添加 ConsoleProvider
            });

            ILogger logger = loggerFactory.CreateLogger<Program>();
            logger.LogInformation("Example log message");


            try
            {
                var config = new WebSocketClientConfiguration();


                //var uri = new Uri("ws://echo.websocket.org/");   // connect to websocket.org website
                //var uri = new Uri("wss://127.0.0.1:22222/test"); // use wss with ssl
                //var uri = new Uri("ws://192.168.5.66/DisWebSocket/websocket");    // connect to localhost
                var uri = new Uri("ws://127.0.0.1:801/DisWebSocket/websocket");    // connect to localhost

                //var uri = new Uri("ws://192.168.1.88:81/websocket/message"+$"?access_key={accessKey}&sign={sign}&timestamp={timestamp}");    // connect to localhost

                _client = new WebSocketClient(uri,
                    OnServerTextReceived,
                    OnServerBinaryReceived,
                    OnServerConnected,
                    OnServerDisconnected,
                    config);
                _client.UsgLogger(logger);
                try
                {
                    await _client.ConnectAsync();

                    var loginRequest = new
                    {
                        host = "192.168.5.66",
                        method = "login",
                        username = "admin",
                        password = "admin246"
                    };
                    Console.WriteLine(loginRequest.ToJson());
                    //await _client.SendBinaryAsync(Encoding.UTF8.GetBytes(loginRequest.ToJson()));
                    for(; ; )
                    {
                        await _client.SendTextAsync(loginRequest.ToJson());
                        Task.Delay(1000).Wait();
                    }

                }
                catch (Exception ex)
                {
                    logger.LogError(ex.Message, ex);
                }

                Console.ReadKey();


                Console.WriteLine("WebSocket client has disconnected from server [{0}].", uri);
            }
            catch (Exception ex)
            {
                logger.LogError(ex.StackTrace);
                logger.LogError(ex.Message, ex);
            }

        }

        private static async Task OnServerConnected(WebSocketClient client)
        {
            Console.WriteLine(string.Format("WebSocket server [{0}] has connected.", client.RemoteEndPoint));
            await Task.CompletedTask;
        }

        private static async Task OnServerTextReceived(WebSocketClient client, string text)
        {
            // 解析 JSON 数据为 dynamic 类型
            dynamic data = JsonConvert.DeserializeObject<dynamic>(text);

            // 提取 sys_user_id
            string sysUserId = data.sys_user_id;
            Console.WriteLine(sysUserId);
            await Task.CompletedTask;
        }

        private static async Task OnServerBinaryReceived(WebSocketClient client, byte[] data, int offset, int count)
        {
            var text = Encoding.UTF8.GetString(data, offset, count);
            // 解析 JSON 数据为 dynamic 类型
            dynamic content = JsonConvert.DeserializeObject<dynamic>(text);

            // 提取 sys_user_id
            string sysUserId = content.sys_user_id;
            Console.WriteLine(sysUserId);

            await Task.CompletedTask;
        }

        private static async Task OnServerDisconnected(WebSocketClient client)
        {
            Console.WriteLine(string.Format("WebSocket server [{0}] has disconnected.", client.RemoteEndPoint));
            await Task.CompletedTask;
        }

    }


    //class Program
    //{
    //    static WebSocketClient _client;

    //    static void Main(string[] args)
    //    {
    //       var loggerFactory = LoggerFactory.Create(builder =>
    //        {
    //            builder
    //                .AddFilter("Microsoft", LogLevel.Warning)
    //                .AddFilter("System", LogLevel.Warning)//CategoryName以System开头的所有日志输出级别为Warning
    //                .AddFilter<ConsoleLoggerProvider>("Wombat.WebSockets.TestWebSocketClient", LogLevel.Debug)
    //                .AddConsole();//在loggerFactory中添加 ConsoleProvider
    //        });

    //        ILogger logger = loggerFactory.CreateLogger<Program>();
    //        logger.LogInformation("Example log message");




    //        // 配置公共参数
    //        string accessKey = "6nvwlwkzz7cigvs6uy3x";
    //        string secretKey = "926mdw36lzyb7t3suxcs";
    //        long timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
    //        string sign = GenerateSign(accessKey, timestamp, secretKey);

    //        Task.Run(async () =>
    //        {
    //            try
    //            {
    //                var config = new WebSocketClientConfiguration();

    //                //增加压缩传输功能
    //                config.EnabledExtensions.Add(PerMessageCompressionExtension.RegisteredToken, new PerMessageCompressionExtensionNegotiator());
    //                //启用压缩传输协议
    //                config.OfferedExtensions.Add(new WebSocketExtensionOfferDescription(PerMessageCompressionExtension.RegisteredToken));
    //                //config.SslTargetHost = "Cowboy";
    //                //config.SslClientCertificates.Add(new System.Security.Cryptography.X509Certificates.X509Certificate2(@"D:\\Cowboy.cer"));
    //                //config.SslPolicyErrorsBypassed = true;

    //                //var uri = new Uri("ws://echo.websocket.org/");   // connect to websocket.org website
    //                //var uri = new Uri("wss://127.0.0.1:22222/test"); // use wss with ssl
    //                var uri = new Uri("ws://127.0.0.1:5001/ws1");    // connect to localhost

    //                //var uri = new Uri("ws://192.168.1.88:81/websocket/message"+$"?access_key={accessKey}&sign={sign}&timestamp={timestamp}");    // connect to localhost

    //                _client = new WebSocketClient(uri,
    //                    OnServerTextReceived,
    //                    OnServerBinaryReceived,
    //                    OnServerConnected,
    //                    OnServerDisconnected,
    //                    config);
    //                _client.UsgLogger(logger);
    //                await _client.ConnectAsync();

    //                Console.WriteLine("WebSocket client has connected to server [{0}].", uri);
    //                Console.WriteLine("Type something to send to server...");
    //                while (_client.State == ConnectionState.Connected)
    //                {
    //                    try
    //                    {
    //                        string text = Console.ReadLine();
    //                        if (text == "quit")
    //                            break;
    //                        Task.Run(async () =>
    //                        {
    //                            if (text == "many")
    //                            {
    //                                text = "";
    //                                for (int i = 0; i < 100; i++)
    //                                {
    //                                    text += $"{i}a,";
    //                                }
    //                                Stopwatch watch = Stopwatch.StartNew();
    //                                for (int i = 0; i <= 100; i++)
    //                                {
    //                                    _client.SendBinary(Encoding.UTF8.GetBytes(text));
    //                                    Console.WriteLine("Client [{0}] send binary -> Sequence[{1}] -> TextLength[{2}].",
    //                                        _client.LocalEndPoint, text, text.Length);
    //                                }
    //                                watch.Stop();
    //                                Console.WriteLine("Client [{0}] send binary -> Count[{1}] -> Cost[{2}] -> PerSecond[{3}].",
    //                                    _client.LocalEndPoint, text.Length, watch.ElapsedMilliseconds / 1000, text.Length / (watch.ElapsedMilliseconds / 1000));
    //                            }
    //                            else if (text == "big1")
    //                            {
    //                                text = new string('x', 1024 * 1024 * 1);
    //                                await _client.SendBinaryAsync(Encoding.UTF8.GetBytes(text));
    //                                Console.WriteLine("Client [{0}] send binary -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
    //                            }
    //                            else if (text == "big10")
    //                            {
    //                                text = new string('x', 1024 * 1024 * 10);
    //                                await _client.SendBinaryAsync(Encoding.UTF8.GetBytes(text));
    //                                Console.WriteLine("Client [{0}] send binary -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
    //                            }
    //                            else if (text == "big100")
    //                            {
    //                                text = new string('x', 1024 * 1024 * 100);
    //                                await _client.SendBinaryAsync(Encoding.UTF8.GetBytes(text));
    //                                Console.WriteLine("Client [{0}] send binary -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
    //                            }
    //                            else if (text == "big1000")
    //                            {
    //                                text = new string('x', 1024 * 1024 * 1000);
    //                                await _client.SendBinaryAsync(Encoding.UTF8.GetBytes(text));
    //                                Console.WriteLine("Client [{0}] send binary -> [{1} Bytes].", _client.LocalEndPoint, text.Length);
    //                            }
    //                            else
    //                            {
    //                                await _client.SendBinaryAsync(Encoding.UTF8.GetBytes(text));
    //                                Console.WriteLine("Client [{0}] send binary -> [{1}].", _client.LocalEndPoint, text);

    //                                //await _client.SendTextAsync(text);
    //                                //Console.WriteLine("Client [{0}] send text -> [{1}].", _client.LocalEndPoint, text);
    //                            }
    //                        }).Forget();
    //                    }
    //                    catch (Exception ex)
    //                    {
    //                        logger.LogError(ex.Message, ex);
    //                    }
    //                }

    //                await _client.Close(WebSocketCloseCode.NormalClosure);
    //                Console.WriteLine("WebSocket client has disconnected from server [{0}].", uri);
    //            }
    //            catch (Exception ex)
    //            {
    //                logger.LogError(ex.StackTrace);
    //                logger.LogError(ex.Message, ex);
    //            }
    //        }).Wait();

    //        Console.ReadKey();
    //    }

    //    static string GenerateSign(string accessKey, long timestamp, string secretKey)
    //    {
    //        using (MD5 md5 = MD5.Create())
    //        {
    //            string input = accessKey + timestamp + secretKey;
    //            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
    //            byte[] hashBytes = md5.ComputeHash(inputBytes);
    //            StringBuilder sb = new StringBuilder();
    //            foreach (byte b in hashBytes)
    //            {
    //                sb.Append(b.ToString("x2"));
    //            }
    //            return sb.ToString();
    //        }
    //    }

    //    private static async Task OnServerConnected(WebSocketClient client)
    //    {
    //        //Console.WriteLine(string.Format("WebSocket server [{0}] has connected.", client.RemoteEndPoint));
    //        await Task.CompletedTask;
    //    }

    //    private static async Task OnServerTextReceived(WebSocketClient client, string text)
    //    {
    //        //Console.Write(string.Format("WebSocket server [{0}] received Text --> ", client.RemoteEndPoint));
    //        //Console.WriteLine(string.Format("{0}", text));
    //        Console.WriteLine(text);
    //        await Task.CompletedTask;
    //    }

    //    private static async Task OnServerBinaryReceived(WebSocketClient client, byte[] data, int offset, int count)
    //    {
    //        var text = Encoding.UTF8.GetString(data, offset, count);
    //        Console.Write(string.Format("WebSocket server [{0}] received Binary --> ", client.RemoteEndPoint));
    //        if (count < 1024 * 1024 * 1)
    //        {
    //            Console.WriteLine(text);
    //        }
    //        else
    //        {
    //            Console.WriteLine("{0} Bytes", count);
    //        }

    //        await Task.CompletedTask;
    //    }

    //    private static async Task OnServerDisconnected(WebSocketClient client)
    //    {
    //        Console.WriteLine(string.Format("WebSocket server [{0}] has disconnected.", client.RemoteEndPoint));
    //        await Task.CompletedTask;
    //    }
    //}
}
