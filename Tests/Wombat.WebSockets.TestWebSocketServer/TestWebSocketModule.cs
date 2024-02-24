using System;
using System.Text;
using System.Threading.Tasks;
using Wombat.Network.WebSockets;

namespace Wombat.WebSockets.TestWebSocketServer
{
    public class TestWebSocketModule : AsyncWebSocketServerModule
    {
        public TestWebSocketModule()
            : base(@"/test")
        {
        }

        public override async Task OnSessionStarted(WebSocketSession session)
        {
            Console.WriteLine(string.Format("WebSocket session [{0}] has connected.", session.RemoteEndPoint));
            await Task.CompletedTask;
        }

        public override async Task OnSessionTextReceived(WebSocketSession session, string text)
        {
            Console.Write(string.Format("WebSocket session [{0}] received Text --> ", session.RemoteEndPoint));
            Console.WriteLine(string.Format("{0}", text));

            await session.SendTextAsync(text);
        }

        public override async Task OnSessionBinaryReceived(WebSocketSession session, byte[] data, int offset, int count)
        {
            var text = Encoding.UTF8.GetString(data, offset, count);
            Console.Write(string.Format("WebSocket session [{0}] received Binary --> ", session.RemoteEndPoint));
            if (count < 1024 * 1024 * 1)
            {
                Console.WriteLine(text);
            }
            else
            {
                Console.WriteLine("{0} Bytes", count);
            }

            await session.SendBinaryAsync(Encoding.UTF8.GetBytes(text));
        }

        public override async Task OnSessionClosed(WebSocketSession session)
        {
            Console.WriteLine(string.Format("WebSocket session [{0}] has disconnected.", session.RemoteEndPoint));
            await Task.CompletedTask;
        }
    }
}
