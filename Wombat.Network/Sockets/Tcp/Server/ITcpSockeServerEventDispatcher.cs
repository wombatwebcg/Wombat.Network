using System.Threading.Tasks;

namespace Wombat.Network.Sockets
{
    public interface ITcpSocketServerEventDispatcher
    {
        Task OnSessionStarted(TcpSocketSession client);

        Task OnSessionDataReceived(TcpSocketSession client, byte[] data, int offset, int count);

        Task OnSessionClosed(TcpSocketSession client);
    }
}
