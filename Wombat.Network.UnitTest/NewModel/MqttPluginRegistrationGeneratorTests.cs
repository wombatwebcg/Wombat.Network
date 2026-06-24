using System.Threading.Tasks;
using FluentAssertions;
using Wombat.Network.Mqtt.Abstractions;
using Wombat.Network.Mqtt.Broker;
using Wombat.Network.Mqtt.Protocol;
using Xunit;

namespace Wombat.Network.UnitTest.NewModel;

public class MqttPluginRegistrationGeneratorTests
{
    [Fact]
    public async Task RegisterGeneratedPlugins_ShouldRegisterAttributedPlugins()
    {
        GeneratedPluginProbe.Reset();
        var broker = new MqttBroker(new MqttBrokerOptions().RegisterGeneratedPlugins());
        var client = new QueueMqttConnection(
            new MqttConnectPacket("generator-plugin-client"),
            new MqttDisconnectPacket());

        await broker.RunConnectionAsync(client);

        GeneratedPluginProbe.ConnectedClients.Should().ContainSingle("generator-plugin-client");
    }

    internal static class GeneratedPluginProbe
    {
        public static System.Collections.Generic.List<string> ConnectedClients { get; } = new System.Collections.Generic.List<string>();

        public static void Reset()
            => ConnectedClients.Clear();
    }

    private sealed class QueueMqttConnection : IMqttConnection
    {
        private readonly System.Collections.Generic.Queue<ReadOnlyMemory<byte>> _incoming;
        private readonly MqttPacketCodec _codec = new MqttPacketCodec();

        public QueueMqttConnection(params MqttPacket[] packets)
        {
            _incoming = new System.Collections.Generic.Queue<ReadOnlyMemory<byte>>(System.Linq.Enumerable.Select(packets, x => (ReadOnlyMemory<byte>)_codec.Encode(x)));
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> packet, System.Threading.CancellationToken cancellationToken = default)
            => default;

        public ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(System.Threading.CancellationToken cancellationToken = default)
            => new ValueTask<ReadOnlyMemory<byte>?>(_incoming.Count > 0 ? _incoming.Dequeue() : (ReadOnlyMemory<byte>?)null);

        public Task CloseAsync(System.Threading.CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

[MqttBrokerPlugin]
public sealed class GeneratedProbePlugin : MqttBrokerPlugin
{
    public override Task OnClientConnectedAsync(MqttBrokerConnectionContext context, System.Threading.CancellationToken cancellationToken = default)
    {
        MqttPluginRegistrationGeneratorTests.GeneratedPluginProbe.ConnectedClients.Add(context.Session.ClientId);
        return Task.CompletedTask;
    }
}
