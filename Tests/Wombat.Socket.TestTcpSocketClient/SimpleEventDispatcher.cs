using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Wombat.Network.Sockets;

namespace Wombat.Socket.TestTcpSocketClient
{
    public class SimpleEventDispatcher : ITcpSocketClientEventDispatcher
    {
        public async Task OnServerConnected(TcpSocketClient client)
        {
            Console.WriteLine(string.Format("TCP server {0} has connected.", client.RemoteEndPoint));
            await Task.CompletedTask;
        }

        public async Task OnServerDataReceived(TcpSocketClient client, byte[] data, int offset, int count)
        {
            var text = Encoding.UTF8.GetString(data, offset, count);
            Console.Write(string.Format("Server : {0} --> {1}:", client.RemoteEndPoint, client.LocalEndPoint));
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

        public async Task OnServerDisconnected(TcpSocketClient client)
        {
            Console.WriteLine(string.Format("TCP server {0} has disconnected.", client.RemoteEndPoint));
            await Task.CompletedTask;
        }
    }
}
