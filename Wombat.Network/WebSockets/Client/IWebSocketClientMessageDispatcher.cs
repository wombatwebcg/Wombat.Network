using System.Threading.Tasks;

namespace Wombat.Network.WebSockets
{
    public interface IWebSocketClientMessageDispatcher
    {
        Task OnServerConnected(WebSocketClient client);
        Task OnServerTextReceived(WebSocketClient client, string text);
        Task OnServerBinaryReceived(WebSocketClient client, byte[] data, int offset, int count);
        Task OnServerDisconnected(WebSocketClient client);

        Task OnServerFragmentationStreamOpened(WebSocketClient client, byte[] data, int offset, int count);
        Task OnServerFragmentationStreamContinued(WebSocketClient client, byte[] data, int offset, int count);
        Task OnServerFragmentationStreamClosed(WebSocketClient client, byte[] data, int offset, int count);
    }
}
