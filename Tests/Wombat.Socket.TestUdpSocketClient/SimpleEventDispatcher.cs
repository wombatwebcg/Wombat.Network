using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Wombat.Network.Sockets;
using Wombat.Network;

namespace Wombat.Socket.TestUdpSocketClient
{
    public class SimpleEventDispatcher : IUdpSocketClientEventDispatcher
    {
        public async Task OnServerConnected(UdpSocketClient client)
        {
            Console.WriteLine(string.Format("UDP server {0} has connected.", client.RemoteEndPoint));
            await Task.CompletedTask;
        }

        public async Task OnServerDataReceived(UdpSocketClient client, byte[] data, int offset, int count, IPEndPoint remoteEndPoint)
        {
            // 检查是否是心跳包
            if (HeartbeatManager.IsHeartbeatPacket(data, offset, count))
            {
                // 如果是心跳包，记录日志但不传递给应用层
                Console.WriteLine($"[Heartbeat] Received from server {remoteEndPoint} at {DateTime.Now:HH:mm:ss:fff}");
                
                // 客户端通常不需要回复服务器的心跳包，因为客户端会定期发送自己的心跳
                
                return; // 不继续处理心跳包
            }
            
            // 处理普通数据包
            var text = Encoding.UTF8.GetString(data, offset, count);
            Console.Write(string.Format("Receive:Server : {0} --> {1}:", remoteEndPoint, client.LocalEndPoint));
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

        public async Task OnServerDisconnected(UdpSocketClient client)
        {
            Console.WriteLine(string.Format("UDP server {0} has disconnected.", client.RemoteEndPoint));
            await Task.CompletedTask;
        }
    }
}
