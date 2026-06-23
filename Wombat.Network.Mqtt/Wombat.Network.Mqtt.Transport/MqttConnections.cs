using System;
using System.Buffers;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Channels;
using Wombat.Network.Mqtt.Abstractions;
using Wombat.Network.Mqtt.Protocol;
using Wombat.Network.Protocols.WebSocket;
using Wombat.Network.Transports.Abstractions;
using Wombat.Network.Transports.Tcp;
using Wombat.Network.Transports.Tls;

namespace Wombat.Network.Mqtt.Transport;

public sealed class MqttConnectionFactory : IMqttConnectionFactory
{
    public async Task<IMqttConnection> ConnectAsync(MqttEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        if (endpoint == null)
        {
            throw new ArgumentNullException(nameof(endpoint));
        }

        var tcpConnection = await TcpTransportConnection.ConnectAsync(new DnsEndPoint(endpoint.Host, endpoint.Port), cancellationToken: cancellationToken).ConfigureAwait(false);
        await tcpConnection.StartAsync(cancellationToken).ConfigureAwait(false);

        switch (endpoint.Scheme)
        {
            case MqttTransportScheme.Tcp:
                return new MqttPipeConnection(tcpConnection);
            case MqttTransportScheme.Tls:
                return await CreateTlsPipeAsync(tcpConnection, endpoint, cancellationToken).ConfigureAwait(false);
            case MqttTransportScheme.WebSocket:
                return await MqttWebSocketConnection.CreateClientAsync(tcpConnection, endpoint.Host, endpoint.Path, cancellationToken).ConfigureAwait(false);
            case MqttTransportScheme.WebSocketSecure:
                var tlsConnection = await CreateTlsTransportAsync(tcpConnection, endpoint, cancellationToken).ConfigureAwait(false);
                return await MqttWebSocketConnection.CreateClientAsync(tlsConnection, endpoint.Host, endpoint.Path, cancellationToken).ConfigureAwait(false);
            default:
                throw new NotSupportedException("Unsupported transport scheme: " + endpoint.Scheme);
        }
    }

    private static async Task<IMqttConnection> CreateTlsPipeAsync(TcpTransportConnection tcpConnection, MqttEndpoint endpoint, CancellationToken cancellationToken)
    {
        var tlsConnection = await CreateTlsTransportAsync(tcpConnection, endpoint, cancellationToken).ConfigureAwait(false);
        return new MqttPipeConnection(tlsConnection);
    }

    private static async Task<TlsTransportConnection> CreateTlsTransportAsync(TcpTransportConnection tcpConnection, MqttEndpoint endpoint, CancellationToken cancellationToken)
    {
        var tlsConnection = TlsTransportConnection.CreateClient(tcpConnection, endpoint.Host);
        await tlsConnection.StartAsync(cancellationToken).ConfigureAwait(false);
        return tlsConnection;
    }
}

public sealed class MqttPipeConnection : IMqttConnection
{
    private readonly ITransportConnection _connection;
    private readonly MqttPacketCodec _codec;

    public MqttPipeConnection(ITransportConnection connection, MqttPacketCodec codec = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _codec = codec ?? new MqttPacketCodec();
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        _connection.Transport.Output.Write(packet.Span);
        await _connection.Transport.Output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var result = await _connection.Transport.Input.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;
            var workingBuffer = buffer;

            try
            {
                if (_codec.TryReadPacketBytes(ref workingBuffer, out var packet))
                {
                    _connection.Transport.Input.AdvanceTo(workingBuffer.Start, workingBuffer.End);
                    return packet.ToArray();
                }

                if (result.IsCompleted)
                {
                    _connection.Transport.Input.AdvanceTo(buffer.End);
                    return null;
                }

                _connection.Transport.Input.AdvanceTo(buffer.Start, buffer.End);
            }
            catch
            {
                _connection.Transport.Input.AdvanceTo(buffer.End);
                throw;
            }
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
        => _connection.CloseAsync(cancellationToken);
}

public sealed class MqttWebSocketConnection : IMqttConnection
{
    private readonly ITransportConnection _connection;
    private readonly WebSocketMessageChannel _channel;

    private MqttWebSocketConnection(ITransportConnection connection, bool isClient)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _channel = new WebSocketMessageChannel(connection, isClient);
    }

    public static async Task<MqttWebSocketConnection> CreateClientAsync(ITransportConnection connection, string host, string path = "/mqtt", CancellationToken cancellationToken = default)
    {
        await connection.StartAsync(cancellationToken).ConfigureAwait(false);
        await WebSocketHandshakeMiddleware.AcceptClientAsync(connection, host, path, cancellationToken).ConfigureAwait(false);
        return new MqttWebSocketConnection(connection, true);
    }

    public static async Task<MqttWebSocketConnection> CreateServerAsync(ITransportConnection connection, CancellationToken cancellationToken = default)
    {
        await connection.StartAsync(cancellationToken).ConfigureAwait(false);
        await WebSocketHandshakeMiddleware.AcceptServerAsync(connection, cancellationToken).ConfigureAwait(false);
        return new MqttWebSocketConnection(connection, false);
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
        => _channel.SendBinaryAsync(packet, cancellationToken);

    public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var message = await _channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (!message.HasValue)
            {
                return null;
            }

            if (message.Value.MessageType == WebSocketMessageType.Binary)
            {
                return message.Value.Payload.ToArray();
            }

            if (message.Value.MessageType == WebSocketMessageType.Ping)
            {
                await _channel.SendPongAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (message.Value.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
        => _connection.CloseAsync(cancellationToken);
}
