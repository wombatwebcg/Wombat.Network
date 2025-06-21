using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Wombat.Network.Sockets;
using Wombat.Network;

namespace Wombat.Socket.TestUdpSocketServer
{
    public class SimpleEventDispatcher : IUdpSocketServerEventDispatcher
    {
        public async Task OnSessionStarted(UdpSocketSession session)
        {
            Console.WriteLine(string.Format("UDP session {0} has connected {1}.", session.ClientEndPoint, session));
            await Task.CompletedTask;
        }
        
        public async Task OnSessionDataReceived(UdpSocketSession session, byte[] data, int offset, int count)
        {
            // 检查是否是心跳包
            if (HeartbeatManager.IsHeartbeatPacket(data, offset, count))
            {
                // 如果是心跳包，记录日志但不传递给应用层
                Console.WriteLine($"[Heartbeat] Received from {session.ClientEndPoint} at {DateTime.Now:HH:mm:ss:fff}");
                
                // 回复心跳包 - 简化处理，总是回复心跳
                byte[] heartbeatResponse = HeartbeatManager.CreateHeartbeatPacket();
                await session.SendAsync(heartbeatResponse);
                Console.WriteLine($"[Heartbeat] Reply sent to {session.ClientEndPoint} at {DateTime.Now:HH:mm:ss:fff}");
                
                return; // 不继续处理心跳包
            }
            
            // 处理普通数据包
            var text = Encoding.UTF8.GetString(data, offset, count);
            Console.Write($"SendToClient : {session.ClientEndPoint} --> ");
            if (count < 1024 * 1024 * 1)
            {
                Console.WriteLine(text + DateTime.Now.ToString("HH:mm:ss:fff"));
            }
            else
            {
                Console.WriteLine($"{count} Bytes{DateTime.Now.ToString("HH:mm:ss:fff")}");
            }
            await session.SendAsync(Encoding.UTF8.GetBytes(text));
        }

        public async Task OnSessionClosed(UdpSocketSession session)
        {
            Console.WriteLine(string.Format("UDP session {0} has disconnected.", session));
            await Task.CompletedTask;
        }
    }
}
