using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using Wombat.Network.Mqtt.Broker;
using Wombat.Network.Mqtt.Protocol;

namespace Wombat.Network.Mqtt.Persistence.LiteDb;

public sealed class LiteDbMqttSessionStore : IMqttSessionStore, IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<MqttSessionDocument> _sessions;
    private readonly ILiteCollection<MqttRetainedMessageDocument> _retainedMessages;

    public LiteDbMqttSessionStore(string connectionString)
        : this(new LiteDatabase(connectionString))
    {
    }

    public LiteDbMqttSessionStore(LiteDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _sessions = _database.GetCollection<MqttSessionDocument>("mqtt_sessions");
        _sessions.EnsureIndex(x => x.ClientId, true);
        _retainedMessages = _database.GetCollection<MqttRetainedMessageDocument>("mqtt_retained_messages");
        _retainedMessages.EnsureIndex(x => x.Topic, true);
    }

    public MqttSessionState Get(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var document = _sessions.FindById(clientId);
        return document == null ? null : ToState(document);
    }

    public IReadOnlyCollection<MqttSessionState> GetAll()
        => _sessions.FindAll().Select(ToState).ToList();

    public void Save(MqttSessionState session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.ClientId))
        {
            return;
        }

        _sessions.Upsert(ToDocument(session));
    }

    public void Remove(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        _sessions.Delete(clientId);
    }

    public IReadOnlyCollection<MqttPublishPacket> GetRetainedMessages()
        => _retainedMessages.FindAll().Select(ToRetainedMessage).ToList();

    public void SaveRetainedMessage(MqttPublishPacket message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.Topic))
        {
            return;
        }

        _retainedMessages.Upsert(new MqttRetainedMessageDocument
        {
            Topic = message.Topic,
            Payload = message.Payload.ToArray(),
            QualityOfService = (byte)message.QualityOfService,
            PacketIdentifier = message.PacketIdentifier,
            Retain = message.Retain,
            Duplicate = message.Duplicate,
        });
    }

    public void RemoveRetainedMessage(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        _retainedMessages.Delete(topic);
    }

    public void Dispose()
        => _database.Dispose();

    private static MqttSessionDocument ToDocument(MqttSessionState session)
    {
        return new MqttSessionDocument
        {
            ClientId = session.ClientId,
            Connected = session.Connected,
            Subscriptions = session.Subscriptions.Select(x => new MqttSubscriptionDocument
            {
                TopicFilter = x.TopicFilter,
                QualityOfService = (byte)x.QualityOfService,
            }).ToList(),
            InflightMessages = session.InflightMessages.Select(x => new MqttInflightDocument
            {
                PacketIdentifier = x.PacketIdentifier,
                Topic = x.Topic,
                Payload = x.Payload ?? Array.Empty<byte>(),
                QualityOfService = (byte)x.QualityOfService,
                Retain = x.Retain,
            }).ToList(),
            WillMessage = session.WillMessage == null ? null : new MqttPublishDocument
            {
                Topic = session.WillMessage.Topic,
                Payload = session.WillMessage.Payload.ToArray(),
                QualityOfService = (byte)session.WillMessage.QualityOfService,
                PacketIdentifier = session.WillMessage.PacketIdentifier,
                Retain = session.WillMessage.Retain,
                Duplicate = session.WillMessage.Duplicate,
            }
        };
    }

    private static MqttSessionState ToState(MqttSessionDocument document)
    {
        var session = new MqttSessionState(document.ClientId)
        {
            Connected = document.Connected,
            WillMessage = document.WillMessage == null ? null : new MqttPublishPacket(
                document.WillMessage.Topic,
                document.WillMessage.Payload ?? Array.Empty<byte>(),
                (MqttQualityOfService)document.WillMessage.QualityOfService,
                document.WillMessage.PacketIdentifier,
                document.WillMessage.Retain,
                document.WillMessage.Duplicate)
        };

        if (document.Subscriptions != null)
        {
            foreach (var subscription in document.Subscriptions)
            {
                session.Subscriptions.Add(new MqttSubscription(subscription.TopicFilter, (MqttQualityOfService)subscription.QualityOfService));
            }
        }

        if (document.InflightMessages != null)
        {
            foreach (var inflight in document.InflightMessages)
            {
                session.InflightMessages.Add(new MqttInflightState
                {
                    PacketIdentifier = inflight.PacketIdentifier,
                    Topic = inflight.Topic,
                    Payload = inflight.Payload ?? Array.Empty<byte>(),
                    QualityOfService = (MqttQualityOfService)inflight.QualityOfService,
                    Retain = inflight.Retain,
                });
            }
        }

        return session;
    }

    private static MqttPublishPacket ToRetainedMessage(MqttRetainedMessageDocument document)
        => new MqttPublishPacket(
            document.Topic,
            document.Payload ?? Array.Empty<byte>(),
            (MqttQualityOfService)document.QualityOfService,
            document.PacketIdentifier,
            document.Retain,
            document.Duplicate);

    private sealed class MqttSessionDocument
    {
        [BsonId]
        public string ClientId { get; set; }

        public bool Connected { get; set; }

        public List<MqttSubscriptionDocument> Subscriptions { get; set; }

        public List<MqttInflightDocument> InflightMessages { get; set; }

        public MqttPublishDocument WillMessage { get; set; }
    }

    private sealed class MqttSubscriptionDocument
    {
        public string TopicFilter { get; set; }

        public byte QualityOfService { get; set; }
    }

    private sealed class MqttInflightDocument
    {
        public ushort PacketIdentifier { get; set; }

        public string Topic { get; set; }

        public byte[] Payload { get; set; }

        public byte QualityOfService { get; set; }

        public bool Retain { get; set; }
    }

    private sealed class MqttPublishDocument
    {
        public string Topic { get; set; }

        public byte[] Payload { get; set; }

        public byte QualityOfService { get; set; }

        public ushort PacketIdentifier { get; set; }

        public bool Retain { get; set; }

        public bool Duplicate { get; set; }
    }

    private sealed class MqttRetainedMessageDocument
    {
        [BsonId]
        public string Topic { get; set; }

        public byte[] Payload { get; set; }

        public byte QualityOfService { get; set; }

        public ushort PacketIdentifier { get; set; }

        public bool Retain { get; set; }

        public bool Duplicate { get; set; }
    }
}
