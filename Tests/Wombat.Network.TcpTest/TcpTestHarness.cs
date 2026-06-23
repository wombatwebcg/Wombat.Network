using System.Net;
using System.Net.Sockets;
using Wombat.Network.Channels;
using Wombat.Network.Protocols.Framing;
using Wombat.Network.Transports.Abstractions;
using Wombat.Network.Transports.Tcp;

namespace Wombat.Network.TcpTest;

internal static class TcpTestHarness
{
    public static async Task RunConnectedPairAsync(
        Func<StreamMessageChannel, CancellationToken, Task> serverAction,
        Func<StreamMessageChannel, CancellationToken, Task> clientAction,
        CancellationToken cancellationToken)
    {
        var endPoint = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
        using var listener = new TcpTransportListener(endPoint);
        await listener.StartAsync(cancellationToken);

        var serverTask = Task.Run(async () =>
        {
            var accepted = await listener.AcceptAsync(cancellationToken);
            await accepted.StartAsync(cancellationToken);
            var serverChannel = CreateChannel(accepted);

            try
            {
                await serverAction(serverChannel, cancellationToken);
            }
            finally
            {
                await serverChannel.CloseAsync(cancellationToken);
            }
        }, cancellationToken);

        var clientTask = Task.Run(async () =>
        {
            using var client = await TcpTransportConnection.ConnectAsync(endPoint, cancellationToken: cancellationToken);
            await client.StartAsync(cancellationToken);
            var clientChannel = CreateChannel(client);

            try
            {
                await clientAction(clientChannel, cancellationToken);
            }
            finally
            {
                await clientChannel.CloseAsync(cancellationToken);
            }
        }, cancellationToken);

        await Task.WhenAll(serverTask, clientTask);
        await listener.CloseAsync(cancellationToken);
    }

    public static StreamMessageChannel CreateChannel(ITransportConnection connection)
        => new(connection, new LengthFieldMessagePipe(LengthField.FourBytes));

    public static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
