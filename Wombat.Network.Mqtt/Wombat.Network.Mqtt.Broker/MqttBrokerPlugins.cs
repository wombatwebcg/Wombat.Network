using System;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Mqtt.Protocol;

namespace Wombat.Network.Mqtt.Broker;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class MqttBrokerPluginAttribute : Attribute
{
}

public interface IMqttBrokerPlugin
{
    Task OnClientConnectedAsync(MqttBrokerConnectionContext context, CancellationToken cancellationToken = default);

    Task OnClientDisconnectedAsync(MqttBrokerConnectionContext context, CancellationToken cancellationToken = default);

    Task OnSubscribedAsync(MqttBrokerSubscriptionContext context, CancellationToken cancellationToken = default);

    Task OnPublishingAsync(MqttBrokerPublishContext context, CancellationToken cancellationToken = default);
}

public sealed class MqttBrokerConnectionContext
{
    public MqttBrokerConnectionContext(MqttSessionState session, IMqttSessionStore sessionStore, MqttConnectPacket connectPacket)
    {
        Session = session;
        SessionStore = sessionStore;
        ConnectPacket = connectPacket;
    }

    public MqttSessionState Session { get; }

    public IMqttSessionStore SessionStore { get; }

    public MqttConnectPacket ConnectPacket { get; }
}

public sealed class MqttBrokerSubscriptionContext
{
    public MqttBrokerSubscriptionContext(MqttSessionState session, IMqttSessionStore sessionStore, MqttSubscribePacket subscribePacket)
    {
        Session = session;
        SessionStore = sessionStore;
        SubscribePacket = subscribePacket;
    }

    public MqttSessionState Session { get; }

    public IMqttSessionStore SessionStore { get; }

    public MqttSubscribePacket SubscribePacket { get; }
}

public sealed class MqttBrokerPublishContext
{
    public MqttBrokerPublishContext(MqttSessionState session, IMqttSessionStore sessionStore, MqttPublishPacket publishPacket)
    {
        Session = session;
        SessionStore = sessionStore;
        PublishPacket = publishPacket;
    }

    public MqttSessionState Session { get; }

    public IMqttSessionStore SessionStore { get; }

    public MqttPublishPacket PublishPacket { get; }
}

public abstract class MqttBrokerPlugin : IMqttBrokerPlugin
{
    public virtual Task OnClientConnectedAsync(MqttBrokerConnectionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public virtual Task OnClientDisconnectedAsync(MqttBrokerConnectionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public virtual Task OnSubscribedAsync(MqttBrokerSubscriptionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public virtual Task OnPublishingAsync(MqttBrokerPublishContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
