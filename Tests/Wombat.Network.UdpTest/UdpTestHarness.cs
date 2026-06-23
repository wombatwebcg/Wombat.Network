using System.Net;
using Wombat.Network.Channels;
using Wombat.Network.Protocols;
using Wombat.Network.TestHelper;
using Wombat.Network.Transports.Udp;

namespace Wombat.Network.UdpTest;

internal static class UdpTestHarness
{
    public static async Task RunConnectedPairAsync(
        Func<DatagramMessageChannel, CancellationToken, Task> serverAction,
        Func<DatagramMessageChannel, CancellationToken, Task> clientAction,
        IMessagePipe? messagePipe,
        CancellationToken cancellationToken)
    {
        var serverEndPoint = new IPEndPoint(IPAddress.Loopback, NetworkTestHelper.GetAvailablePort());
        using var serverTransport = new UdpDatagramTransport(serverEndPoint);
        using var clientTransport = new UdpDatagramTransport(defaultRemoteEndPoint: serverEndPoint);

        await serverTransport.StartAsync(cancellationToken);
        await clientTransport.StartAsync(cancellationToken);

        var serverChannel = new DatagramMessageChannel(serverTransport, messagePipe: messagePipe);
        var clientChannel = new DatagramMessageChannel(
            clientTransport,
            (IPEndPoint)serverEndPoint,
            messagePipe);

        var serverTask = Task.Run(() => serverAction(serverChannel, cancellationToken), cancellationToken);
        var clientTask = Task.Run(() => clientAction(clientChannel, cancellationToken), cancellationToken);

        await Task.WhenAll(serverTask, clientTask);
        await serverChannel.CloseAsync(cancellationToken);
        await clientChannel.CloseAsync(cancellationToken);
    }
}
