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

        static void Main(string[] args)
        {

            try
            {
                var config = new TcpSocketClientConfiguration();
                //config.UseSsl = true;
                //config.SslTargetHost = "Cowboy";
                //config.SslClientCertificates.Add(new System.Security.Cryptography.X509Certificates.X509Certificate2(@"D:\\Cowboy.cer"));
                //config.SslPolicyErrorsBypassed = false;

                //config.FrameBuilder = new FixedLengthFrameBuilder(20000);
                //config.FrameBuilder = new RawBufferFrameBuilder();
                config.FrameBuilder = new LineBasedFrameBuilder();
                //config.FrameBuilder = new LengthPrefixedFrameBuilder();
                config.FrameBuilder = new LengthFieldBasedFrameBuilder();

                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 22222);
                _client = new TcpSocketClient(remoteEP, new SimpleEventDispatcher(), config);
                // 创建一个取消令牌源
                var cancellationTokenSource = new CancellationTokenSource();

                //// 设置连接的超时时间（可选）
                //cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(10));  // 设置超时为10秒

                // 调用 Connect 方法，传递 CancellationToken
                _client.Connect().ConfigureAwait(false).GetAwaiter().GetResult(); 
                Console.WriteLine("连接成功！");
            
                Console.WriteLine("TCP client has connected to server [{0}].", remoteEP);
                Console.WriteLine("Type something to send to server...");
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
                        //Logger.Get<Program>().Error(ex.Message, ex);
                    }
                }

                _client.Shutdown();
                Console.WriteLine("TCP client has disconnected from server [{0}].", remoteEP);
            }
            catch (Exception ex)
            {
                //Logger.Get<Program>().Error(ex.Message, ex);
            }

            Console.ReadKey();
        }
    }
}
