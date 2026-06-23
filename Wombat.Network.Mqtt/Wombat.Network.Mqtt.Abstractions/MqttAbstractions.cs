using System;
using System.Threading;
using System.Threading.Tasks;

namespace Wombat.Network.Mqtt.Abstractions;

public enum MqttTransportScheme
{
    Tcp = 0,
    Tls = 1,
    WebSocket = 2,
    WebSocketSecure = 3,
}

public sealed class MqttEndpoint
{
    public MqttEndpoint(string host, int port, MqttTransportScheme scheme = MqttTransportScheme.Tcp, string path = "/mqtt")
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host is required.", nameof(host));
        }

        if (port <= 0 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        Host = host;
        Port = port;
        Scheme = scheme;
        Path = string.IsNullOrWhiteSpace(path) ? "/" : path;
    }

    public string Host { get; }

    public int Port { get; }

    public MqttTransportScheme Scheme { get; }

    public string Path { get; }
}

public interface IMqttConnection
{
    ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default);

    ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}

public interface IMqttConnectionFactory
{
    Task<IMqttConnection> ConnectAsync(MqttEndpoint endpoint, CancellationToken cancellationToken = default);
}
