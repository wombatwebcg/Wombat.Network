using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Logger;
using Wombat.Network.Sockets;

namespace Wombat.Socket.TestTcpSocketClient 
{ 
    class Program
    {
        static TcpSocketClient _client1;
        static TcpSocketClient _client2;
        static void Main(string[] args)
        {
            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(level: LogLevel.Trace);
                builder.AddConsole();
                builder.AddDefalutFileLogger();
            });
            ServiceProvider sp = services.BuildServiceProvider();
            // create logger
            ILogger<Program> logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();

            logger.LogInformation("Example log message");

            try
            {
                var config = new TcpSocketClientConfiguration();
                //config.UseSsl = true;
                //config.SslTargetHost = "Cowboy";
                //config.SslClientCertificates.Add(new System.Security.Cryptography.X509Certificates.X509Certificate2(@"D:\\Cowboy.cer"));
                //config.SslPolicyErrorsBypassed = false;

                //config.FrameBuilder = new FixedLengthFrameBuilder(20000);
                //config.FrameBuilder = new RawBufferFrameBuilder();
                //config.FrameBuilder = new LineBasedFrameBuilder();
                //config.FrameBuilder = new LengthPrefixedFrameBuilder();
                //config.FrameBuilder = new LengthFieldBasedFrameBuilder();

                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 22222);
                _client1 = new TcpSocketClient(dispatcher: new SimpleEventDispatcher(),configuration: new TcpSocketClientConfiguration(),frameBuilder:new RawBufferFrameBuilder());
                _client2 = new TcpSocketClient(dispatcher: new SimpleEventDispatcher(),configuration: new TcpSocketClientConfiguration(), frameBuilder: new RawBufferFrameBuilder());
                _client2.UsgLogger(logger);
                _client1.UsgLogger(logger);
                _client1.UseSubscribe();
                _client2.UseSubscribe();
                _client1.Connect(remoteEP);
                _client2.Connect(remoteEP);
                //Console.WriteLine("TCP client has connected to server [{0}].", remoteEP);
                //Console.WriteLine("Type something to send to server...");
                while (true)
                {
                    try
                    {
                        string text = Console.ReadLine();
                        //if (text == "quit")
                        //    break;
                        //Task.Run(async () =>
                        //{
                        //    if (text == "many")
                        //    {
                        //        text = "";
                        //        for (int i = 0; i < 100; i++)
                        //        {
                        //            text += $"{i},";
                        //        }

                        //        //text = new string('123456789', 10);
                        //        for (int i = 0; i < 100; i++)
                        //        {
                        //            byte[] buff1 = new byte[200] ;
                        //            await _client1.SendAsync(Encoding.UTF8.GetBytes(text));
                        //            var c1 = _client1.ReceiveAsync(buff1,0, buff1.Length).Result;
                        //            byte[] buff2 = new byte[200];

                        //            await _client2.SendAsync(Encoding.UTF8.GetBytes(text));
                        //            var c2 =  _client2.ReceiveAsync(buff2, 0, buff2.Length).Result;
                        //            Thread.Sleep(100);
                        //            logger.LogDebug("Client [{0}] send text -> [{1}].", _client1.LocalEndPoint, Encoding.ASCII.GetString(buff1).Trim());
                        //            Thread.Sleep(100);
                        //            logger.LogDebug("Client [{0}] send text -> [{1}].", _client2.LocalEndPoint, Encoding.ASCII.GetString(buff2).Trim());

                        //        }
                        //    }
                        //    else if (text == "big1k")
                        //    {
                        //        text = new string('x', 1024 * 1);
                        //        await _client1.SendAsync(Encoding.UTF8.GetBytes(text));
                        //        Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client1.LocalEndPoint, text.Length);
                        //    }
                        //    else if (text == "big10k")
                        //    {
                        //        text = new string('x', 1024 * 10);
                        //        await _client1.SendAsync(Encoding.UTF8.GetBytes(text));
                        //        Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client1.LocalEndPoint, text.Length);
                        //    }
                        //    else if (text == "big100k")
                        //    {
                        //        text = new string('x', 1024 * 100);
                        //        await _client1.SendAsync(Encoding.UTF8.GetBytes(text));
                        //        Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client1.LocalEndPoint, text.Length);
                        //    }
                        //    else if (text == "big1m")
                        //    {
                        //        text = new string('x', 1024 * 1024 * 1);
                        //        await _client1.SendAsync(Encoding.UTF8.GetBytes(text));
                        //        Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client1.LocalEndPoint, text.Length);
                        //    }
                        //    else if (text == "big10m")
                        //    {
                        //        text = new string('x', 1024 * 1024 * 10);
                        //        await _client1.SendAsync(Encoding.UTF8.GetBytes(text));
                        //        Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client1.LocalEndPoint, text.Length);
                        //    }
                        //    else if (text == "big100m")
                        //    {
                        //        text = new string('x', 1024 * 1024 * 100);
                        //        await _client1.SendAsync(Encoding.UTF8.GetBytes(text));
                        //        Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client1.LocalEndPoint, text.Length);
                        //    }
                        //    else if (text == "big1g")
                        //    {
                        //        text = new string('x', 1024 * 1024 * 1024);
                        //        await _client1.SendAsync(Encoding.UTF8.GetBytes(text));
                        //        Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client1.LocalEndPoint, text.Length);
                        //    }
                        //    else
                        //    {
                        //        await _client1.SendAsync(Encoding.UTF8.GetBytes(text));
                        //        Console.WriteLine("Client [{0}] send text -> [{1} Bytes].", _client1.LocalEndPoint, text.Length);
                        //    }
                        //});
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex.Message, ex);
                    }
                }

                _client1.Shutdown();
                _client2.Shutdown();

                Console.WriteLine("TCP client has disconnected from server [{0}].", remoteEP);
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message, ex);
            }

            Console.ReadKey();
        }
    }
}
