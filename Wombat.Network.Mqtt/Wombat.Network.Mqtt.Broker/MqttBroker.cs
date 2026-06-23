using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wombat.Network.Mqtt.Abstractions;
using Wombat.Network.Mqtt.Protocol;
using Wombat.Network.Mqtt.Transport;
using Wombat.Network.Transports.Abstractions;
using Wombat.Network.Transports.Tcp;

namespace Wombat.Network.Mqtt.Broker;

public sealed class MqttBrokerOptions
{
    internal List<MqttListenerRegistration> Listeners { get; } = new List<MqttListenerRegistration>();

    internal Func<IMqttSessionStore, IMqttSessionStore> SessionStoreFactory { get; private set; }

    internal List<object> Plugins { get; } = new List<object>();

    public MqttBrokerOptions ListenTcp(int port)
        => Listen(MqttTransportScheme.Tcp, port);

    public MqttBrokerOptions ListenTls(int port)
        => Listen(MqttTransportScheme.Tls, port);

    public MqttBrokerOptions ListenWebSocket(int port, string path = "/mqtt")
        => Listen(MqttTransportScheme.WebSocket, port, path);

    public MqttBrokerOptions ListenWebSocketSecure(int port, string path = "/mqtt")
        => Listen(MqttTransportScheme.WebSocketSecure, port, path);

    public MqttBrokerOptions UsePlugin(object plugin)
    {
        if (plugin != null)
        {
            Plugins.Add(plugin);
        }

        return this;
    }

    public MqttBrokerOptions UseSessionStore(IMqttSessionStore sessionStore)
    {
        SessionStoreFactory = _ => sessionStore ?? new InMemoryMqttSessionStore();
        return this;
    }

    private MqttBrokerOptions Listen(MqttTransportScheme scheme, int port, string path = "/mqtt")
    {
        Listeners.Add(new MqttListenerRegistration(scheme, port, path));
        return this;
    }
}

public sealed class MqttListenerRegistration
{
    public MqttListenerRegistration(MqttTransportScheme scheme, int port, string path)
    {
        Scheme = scheme;
        Port = port;
        Path = string.IsNullOrWhiteSpace(path) ? "/" : path;
    }

    public MqttTransportScheme Scheme { get; }

    public int Port { get; }

    public string Path { get; }
}

public interface IMqttConnectionAcceptor
{
    Task<IMqttConnection> AcceptAsync(CancellationToken cancellationToken = default);
}

public interface IMqttSessionStore
{
    MqttSessionState Get(string clientId);

    void Save(MqttSessionState session);

    void Remove(string clientId);
}

public sealed class InMemoryMqttSessionStore : IMqttSessionStore
{
    private readonly ConcurrentDictionary<string, MqttSessionState> _sessions = new ConcurrentDictionary<string, MqttSessionState>(StringComparer.Ordinal);

    public MqttSessionState Get(string clientId)
    {
        _sessions.TryGetValue(clientId ?? string.Empty, out var session);
        return session;
    }

    public void Save(MqttSessionState session)
    {
        if (session == null || string.IsNullOrEmpty(session.ClientId))
        {
            return;
        }

        _sessions[session.ClientId] = session;
    }

    public void Remove(string clientId)
    {
        if (string.IsNullOrEmpty(clientId))
        {
            return;
        }

        _sessions.TryRemove(clientId, out _);
    }
}

public sealed class MqttSessionState
{
    public MqttSessionState(string clientId)
    {
        ClientId = clientId ?? string.Empty;
        Subscriptions = new List<MqttSubscription>();
    }

    public string ClientId { get; }

    public List<MqttSubscription> Subscriptions { get; }

    public MqttPublishPacket WillMessage { get; set; }

    public bool Connected { get; set; }
}

public sealed class MqttBroker
{
    private readonly IMqttSessionStore _sessionStore;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, ClientConnectionState> _connections = new ConcurrentDictionary<string, ClientConnectionState>(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MqttPublishPacket> _retainedMessages = new ConcurrentDictionary<string, MqttPublishPacket>(StringComparer.Ordinal);
    private readonly List<BrokerListenerRuntime> _listeners = new List<BrokerListenerRuntime>();
    private readonly object _gate = new object();
    private CancellationTokenSource _listenerCancellationTokenSource;
    private Task _listenerTask;
    private bool _started;

    public MqttBroker(MqttBrokerOptions options = null, ILogger logger = null)
    {
        Options = options ?? new MqttBrokerOptions();
        _sessionStore = Options.SessionStoreFactory == null ? new InMemoryMqttSessionStore() : Options.SessionStoreFactory(new InMemoryMqttSessionStore());
        _logger = logger ?? NullLogger.Instance;
    }

    public MqttBrokerOptions Options { get; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_started)
            {
                throw new InvalidOperationException("Broker already started.");
            }

            _listenerCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listeners.AddRange(Options.Listeners.Select(CreateListenerRuntime));
            _listenerTask = RunListenersAsync(_listenerCancellationTokenSource.Token);
            _started = true;
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task listenerTask = null;
        List<BrokerListenerRuntime> listeners;
        CancellationTokenSource listenerCts = null;

        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            listeners = _listeners.ToList();
            _listeners.Clear();
            listenerTask = _listenerTask;
            _listenerTask = null;
            listenerCts = _listenerCancellationTokenSource;
            _listenerCancellationTokenSource = null;
        }

        listenerCts.Cancel();

        foreach (var listener in listeners)
        {
            await listener.Listener.CloseAsync(cancellationToken).ConfigureAwait(false);
        }

        if (listenerTask != null)
        {
            try
            {
                await listenerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        listenerCts.Dispose();
    }

    public Task RunConnectionAsync(IMqttConnection connection, CancellationToken cancellationToken = default)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        return RunConnectionCoreAsync(connection, cancellationToken);
    }

    private async Task RunListenersAsync(CancellationToken cancellationToken)
    {
        foreach (var listener in _listeners)
        {
            await listener.Listener.StartAsync(cancellationToken).ConfigureAwait(false);
            listener.AcceptLoopTask = RunAcceptLoopAsync(listener, cancellationToken);
        }

        await Task.WhenAll(_listeners.Select(x => x.AcceptLoopTask)).ConfigureAwait(false);
    }

    private async Task RunAcceptLoopAsync(BrokerListenerRuntime listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ITransportConnection transportConnection = null;

            try
            {
                transportConnection = await listener.Listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                var mqttConnection = await listener.AcceptAsync(transportConnection, cancellationToken).ConfigureAwait(false);
                _ = RunAcceptedConnectionAsync(mqttConnection, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (transportConnection != null)
                {
                    await transportConnection.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }

                break;
            }
            catch (Exception ex)
            {
                if (transportConnection != null)
                {
                    try { await transportConnection.CloseAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                }

                _logger.LogError(ex, "MQTT broker accept loop failed on port {Port}.", listener.Registration.Port);
            }
        }
    }

    private async Task RunAcceptedConnectionAsync(IMqttConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await RunConnectionCoreAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT broker connection failed.");
        }
        finally
        {
            try { await connection.CloseAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        }
    }

    private async Task RunConnectionCoreAsync(IMqttConnection connection, CancellationToken cancellationToken)
    {
        var codec = new MqttPacketCodec();
        ClientConnectionState state = null;
        var gracefulDisconnect = false;

        try
        {
            while (true)
            {
                var payload = await connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (!payload.HasValue)
                {
                    break;
                }

                var packet = codec.Decode(payload.Value.Span);
                switch (packet)
                {
                    case MqttConnectPacket connect:
                        state = await HandleConnectAsync(connection, codec, connect, cancellationToken).ConfigureAwait(false);
                        break;
                    case MqttSubscribePacket subscribe:
                        await HandleSubscribeAsync(state, connection, codec, subscribe, cancellationToken).ConfigureAwait(false);
                        break;
                    case MqttPublishPacket publish:
                        await HandlePublishAsync(state, connection, codec, publish, cancellationToken).ConfigureAwait(false);
                        break;
                    case MqttPingReqPacket _:
                        await connection.SendAsync(codec.Encode(new MqttPingRespPacket()), cancellationToken).ConfigureAwait(false);
                        break;
                    case MqttDisconnectPacket _:
                        gracefulDisconnect = true;
                        await HandleDisconnectAsync(state).ConfigureAwait(false);
                        await connection.CloseAsync(cancellationToken).ConfigureAwait(false);
                        return;
                    default:
                        throw new MqttProtocolException("Unsupported packet in broker: " + packet.GetType().Name);
                }
            }
        }
        catch (Exception ex) when (!(ex is MqttProtocolException))
        {
            _logger.LogError(ex, "MQTT broker connection failed.");
            throw;
        }
        finally
        {
            await OnConnectionClosedAsync(state, gracefulDisconnect, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ClientConnectionState> HandleConnectAsync(IMqttConnection connection, MqttPacketCodec codec, MqttConnectPacket connect, CancellationToken cancellationToken)
    {
        var clientId = string.IsNullOrWhiteSpace(connect.ClientId) ? Guid.NewGuid().ToString("N") : connect.ClientId;
        var existing = _sessionStore.Get(clientId);
        var session = connect.CleanStart || existing == null ? new MqttSessionState(clientId) : existing;
        session.Connected = true;
        session.WillMessage = connect.WillMessage;
        _sessionStore.Save(session);

        var state = new ClientConnectionState(clientId, connection, session);
        _connections[clientId] = state;
        await connection.SendAsync(codec.Encode(new MqttConnAckPacket(0, !connect.CleanStart && existing != null)), cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("MQTT broker accepted client {ClientId}.", clientId);

        foreach (var retained in SnapshotRetainedMessages())
        {
            if (SessionMatches(session, retained.Topic))
            {
                await state.Connection.SendAsync(codec.Encode(retained), cancellationToken).ConfigureAwait(false);
            }
        }

        return state;
    }

    private async Task HandleSubscribeAsync(ClientConnectionState state, IMqttConnection connection, MqttPacketCodec codec, MqttSubscribePacket subscribe, CancellationToken cancellationToken)
    {
        EnsureConnected(state);
        foreach (var subscription in subscribe.Subscriptions)
        {
            var existing = state.Session.Subscriptions.FindIndex(x => string.Equals(x.TopicFilter, subscription.TopicFilter, StringComparison.Ordinal));
            if (existing >= 0)
            {
                state.Session.Subscriptions[existing] = subscription;
            }
            else
            {
                state.Session.Subscriptions.Add(subscription);
            }
        }

        _sessionStore.Save(state.Session);
        await connection.SendAsync(codec.Encode(new MqttSubAckPacket(subscribe.PacketIdentifier, subscribe.Subscriptions.Select(x => (byte)x.QualityOfService).ToArray())), cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("MQTT broker updated subscriptions for {ClientId}.", state.ClientId);

        foreach (var retained in SnapshotRetainedMessages())
        {
            if (subscribe.Subscriptions.Any(x => MqttTopicFilterMatcher.IsMatch(x.TopicFilter, retained.Topic)))
            {
                await state.Connection.SendAsync(codec.Encode(retained), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandlePublishAsync(ClientConnectionState state, IMqttConnection connection, MqttPacketCodec codec, MqttPublishPacket publish, CancellationToken cancellationToken)
    {
        EnsureConnected(state);
        if (publish.Retain)
        {
            if (publish.Payload.IsEmpty)
            {
                _retainedMessages.TryRemove(publish.Topic, out _);
            }
            else
            {
                _retainedMessages[publish.Topic] = publish;
            }
        }

        foreach (var subscriber in _connections.Values)
        {
            if (!subscriber.Session.Connected || !SessionMatches(subscriber.Session, publish.Topic))
            {
                continue;
            }

            var deliveryQos = GetDeliveryQualityOfService(subscriber.Session, publish);
            var deliveryPacket = new MqttPublishPacket(
                publish.Topic,
                publish.Payload,
                deliveryQos,
                deliveryQos == MqttQualityOfService.AtLeastOnce ? publish.PacketIdentifier : (ushort)0,
                publish.Retain,
                publish.Duplicate);

            await subscriber.Connection.SendAsync(codec.Encode(deliveryPacket), cancellationToken).ConfigureAwait(false);
        }

        if (publish.QualityOfService == MqttQualityOfService.AtLeastOnce)
        {
            await connection.SendAsync(codec.Encode(new MqttPubAckPacket(publish.PacketIdentifier)), cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("MQTT broker routed publish {Topic}.", publish.Topic);
    }

    private Task HandleDisconnectAsync(ClientConnectionState state)
    {
        if (state == null)
        {
            return Task.CompletedTask;
        }

        state.Session.Connected = false;
        state.Session.WillMessage = null;
        _sessionStore.Save(state.Session);
        _connections.TryRemove(state.ClientId, out _);
        _logger.LogInformation("MQTT broker disconnected client {ClientId}.", state.ClientId);
        return Task.CompletedTask;
    }

    private async Task OnConnectionClosedAsync(ClientConnectionState state, bool gracefulDisconnect, CancellationToken cancellationToken)
    {
        if (state == null)
        {
            return;
        }

        _connections.TryRemove(state.ClientId, out _);
        state.Session.Connected = false;
        _sessionStore.Save(state.Session);

        if (!gracefulDisconnect && state.Session.WillMessage != null)
        {
            var will = state.Session.WillMessage;
            state.Session.WillMessage = null;
            await PublishInternalAsync(will, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("MQTT broker published will for {ClientId}.", state.ClientId);
        }
    }

    private async Task PublishInternalAsync(MqttPublishPacket publish, CancellationToken cancellationToken)
    {
        var codec = new MqttPacketCodec();
        if (publish.Retain)
        {
            _retainedMessages[publish.Topic] = publish;
        }

        foreach (var subscriber in _connections.Values)
        {
            if (!subscriber.Session.Connected || !SessionMatches(subscriber.Session, publish.Topic))
            {
                continue;
            }

            await subscriber.Connection.SendAsync(codec.Encode(new MqttPublishPacket(publish.Topic, publish.Payload, GetDeliveryQualityOfService(subscriber.Session, publish), retain: publish.Retain)), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool SessionMatches(MqttSessionState session, string topic)
        => session != null && session.Subscriptions.Any(x => MqttTopicFilterMatcher.IsMatch(x.TopicFilter, topic));

    private static MqttQualityOfService GetDeliveryQualityOfService(MqttSessionState session, MqttPublishPacket publish)
    {
        var requested = session.Subscriptions.Where(x => MqttTopicFilterMatcher.IsMatch(x.TopicFilter, publish.Topic)).Select(x => x.QualityOfService).DefaultIfEmpty(MqttQualityOfService.AtMostOnce).Max();
        return requested < publish.QualityOfService ? requested : publish.QualityOfService;
    }

    private static void EnsureConnected(ClientConnectionState state)
    {
        if (state == null)
        {
            throw new MqttProtocolException("CONNECT must be the first packet.");
        }
    }

    private List<MqttPublishPacket> SnapshotRetainedMessages()
        => _retainedMessages.Values.ToList();

    private static BrokerListenerRuntime CreateListenerRuntime(MqttListenerRegistration registration)
    {
        var listener = new TcpTransportListener(new IPEndPoint(IPAddress.Any, registration.Port));
        return new BrokerListenerRuntime(registration, listener, CreateConnectionAdapter(registration));
    }

    private static Func<ITransportConnection, CancellationToken, Task<IMqttConnection>> CreateConnectionAdapter(MqttListenerRegistration registration)
    {
        switch (registration.Scheme)
        {
            case MqttTransportScheme.Tcp:
                return (connection, cancellationToken) => CreatePipeConnectionAsync(connection, cancellationToken);
            case MqttTransportScheme.WebSocket:
                return (connection, cancellationToken) => CreateWebSocketConnectionAsync(connection, registration.Path, cancellationToken);
            case MqttTransportScheme.Tls:
            case MqttTransportScheme.WebSocketSecure:
                return (_, __) => Task.FromException<IMqttConnection>(new NotSupportedException("TLS/WSS listener startup is deferred until certificate plumbing is in place."));
            default:
                return (_, __) => Task.FromException<IMqttConnection>(new NotSupportedException("Unsupported transport scheme: " + registration.Scheme));
        }
    }

    private static Task<IMqttConnection> CreatePipeConnectionAsync(ITransportConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IMqttConnection>(new MqttPipeConnection(connection));
    }

    private static async Task<IMqttConnection> CreateWebSocketConnectionAsync(ITransportConnection connection, string expectedPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = expectedPath;
        return await MqttWebSocketConnection.CreateServerAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private sealed class ClientConnectionState
    {
        public ClientConnectionState(string clientId, IMqttConnection connection, MqttSessionState session)
        {
            ClientId = clientId;
            Connection = connection;
            Session = session;
        }

        public string ClientId { get; }

        public IMqttConnection Connection { get; }

        public MqttSessionState Session { get; }
    }

    private sealed class BrokerListenerRuntime
    {
        public BrokerListenerRuntime(MqttListenerRegistration registration, ITransportListener listener, Func<ITransportConnection, CancellationToken, Task<IMqttConnection>> acceptAsync)
        {
            Registration = registration;
            Listener = listener;
            AcceptAsync = acceptAsync;
        }

        public MqttListenerRegistration Registration { get; }

        public ITransportListener Listener { get; }

        public Func<ITransportConnection, CancellationToken, Task<IMqttConnection>> AcceptAsync { get; }

        public Task AcceptLoopTask { get; set; }
    }
}
public static class MqttTopicFilterMatcher
{
    public static bool IsMatch(string filter, string topic)
    {
        if (string.IsNullOrWhiteSpace(filter) || string.IsNullOrWhiteSpace(topic))
        {
            return false;
        }

        var filterLevels = filter.Split('/');
        var topicLevels = topic.Split('/');
        var topicIndex = 0;

        for (var filterIndex = 0; filterIndex < filterLevels.Length; filterIndex++)
        {
            var current = filterLevels[filterIndex];
            if (current == "#")
            {
                return filterIndex == filterLevels.Length - 1;
            }

            if (topicIndex >= topicLevels.Length)
            {
                return false;
            }

            if (current != "+" && !string.Equals(current, topicLevels[topicIndex], StringComparison.Ordinal))
            {
                return false;
            }

            topicIndex++;
        }

        return topicIndex == topicLevels.Length;
    }
}

public sealed class MqttProtocolException : InvalidOperationException
{
    public MqttProtocolException(string message)
        : base(message)
    {
    }
}
