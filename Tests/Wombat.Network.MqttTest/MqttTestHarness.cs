using Wombat.Network.Mqtt.Abstractions;
using Wombat.Network.Mqtt.Broker;
using Wombat.Network.Mqtt.Client;
using Wombat.Network.TestHelper;

namespace Wombat.Network.MqttTest;

internal static class MqttTestHarness
{
    public static async Task RunTcpBrokerAsync(
        Func<int, MqttBroker, CancellationToken, Task> testAction,
        CancellationToken cancellationToken)
    {
        var port = NetworkTestHelper.GetAvailablePort();
        var broker = new MqttBroker(new MqttBrokerOptions().ListenTcp(port));
        await broker.StartAsync(cancellationToken);

        try
        {
            await testAction(port, broker, cancellationToken);
        }
        finally
        {
            await broker.StopAsync(CancellationToken.None);
        }
    }

    public static MqttClient CreateTcpClient(string clientId, int port, bool cleanStart = true)
        => new(new MqttClientOptions
        {
            ClientId = clientId,
            CleanStart = cleanStart,
            Endpoint = new MqttEndpoint("127.0.0.1", port, MqttTransportScheme.Tcp),
        });
}
