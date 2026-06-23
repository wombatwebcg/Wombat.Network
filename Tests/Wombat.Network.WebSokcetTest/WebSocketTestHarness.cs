using System.Net;
using Wombat.Network.Channels;
using Wombat.Network.Protocols.WebSocket;
using Wombat.Network.TestHelper;
using Wombat.Network.Transports.Tcp;

namespace Wombat.Network.WebSokcetTest;

internal static class WebSocketTestHarness
{
    public static async Task RunConnectedPairAsync(
        Func<WebSocketMessageChannel, WebSocketHandshakeRequest, CancellationToken, Task> serverAction,
        Func<WebSocketMessageChannel, CancellationToken, Task> clientAction,
        string path,
        CancellationToken cancellationToken)
    {
        var endPoint = new IPEndPoint(IPAddress.Loopback, NetworkTestHelper.GetAvailablePort());
        using var listener = new TcpTransportListener(endPoint);
        await listener.StartAsync(cancellationToken);

        var serverTask = Task.Run(async () =>
        {
            using var accepted = (TcpTransportConnection)await listener.AcceptAsync(cancellationToken);
            await accepted.StartAsync(cancellationToken);
            var request = await WebSocketHandshakeMiddleware.AcceptServerAsync(accepted, cancellationToken);
            var serverChannel = new WebSocketMessageChannel(accepted, isClient: false);

            try
            {
                await serverAction(serverChannel, request, cancellationToken);
            }
            finally
            {
                await accepted.CloseAsync(cancellationToken);
            }
        }, cancellationToken);

        var clientTask = Task.Run(async () =>
        {
            using var client = await TcpTransportConnection.ConnectAsync(endPoint, cancellationToken: cancellationToken);
            await client.StartAsync(cancellationToken);
            await WebSocketHandshakeMiddleware.AcceptClientAsync(client, $"127.0.0.1:{endPoint.Port}", path, cancellationToken);
            var clientChannel = new WebSocketMessageChannel(client, isClient: true);

            try
            {
                await clientAction(clientChannel, cancellationToken);
            }
            finally
            {
                await client.CloseAsync(cancellationToken);
            }
        }, cancellationToken);

        await Task.WhenAll(serverTask, clientTask);
        await listener.CloseAsync(cancellationToken);
    }
}
