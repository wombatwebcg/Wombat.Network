using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Wombat.Network.Sockets;
using Wombat.Network;

namespace Wombat.Socket.TestTcpSocketServer
{
    public class SimpleEventDispatcher : ITcpSocketServerEventDispatcher
    {
        public async Task OnSessionStarted(TcpSocketSession session)
        {
            Console.WriteLine(string.Format("TCP session {0} has connected {1}.", session.RemoteEndPoint, session));
            await Task.CompletedTask;
        }
        
        public async Task OnSessionDataReceived(TcpSocketSession session, byte[] data, int offset, int count)
        {
            // 检查是否是心跳包
            if (HeartbeatManager.IsHeartbeatPacket(data, offset, count))
            {
                // 如果是心跳包，记录日志但不传递给应用层
                Console.WriteLine($"[Heartbeat] Received from {session.RemoteEndPoint} at {DateTime.Now:HH:mm:ss:fff}");
                
                // 回复心跳包 - 简化处理，总是回复心跳
                byte[] heartbeatResponse = HeartbeatManager.CreateHeartbeatPacket();
                await session.SendAsync(heartbeatResponse);
                Console.WriteLine($"[Heartbeat] Reply sent to {session.RemoteEndPoint} at {DateTime.Now:HH:mm:ss:fff}");
                
                return; // 不继续处理心跳包
            }
            
            // 处理普通数据包
            var text = Encoding.UTF8.GetString(data, offset, count);
            Console.Write($"SendToClient : {session.RemoteEndPoint} --> ");
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

        public async Task OnSessionClosed(TcpSocketSession session)
        {
            Console.WriteLine(string.Format("TCP session {0} has disconnected.", session));
            await Task.CompletedTask;
        }
    }
}
