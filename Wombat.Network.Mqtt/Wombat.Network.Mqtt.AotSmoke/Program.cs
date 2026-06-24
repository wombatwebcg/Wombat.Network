using System;
using LiteDB;
using Wombat.Network.Mqtt.Abstractions;
using Wombat.Network.Mqtt.Broker;
using Wombat.Network.Mqtt.Client;
using Wombat.Network.Mqtt.Persistence.LiteDb;
using Wombat.Network.Mqtt.Protocol;

var endpoint = new MqttEndpoint("localhost", 1883, MqttTransportScheme.Tcp);
var client = new MqttClient(new MqttClientOptions
{
    Endpoint = endpoint,
    ClientId = "aot-smoke",
    CleanStart = true,
    KeepAliveSeconds = 15,
});

using var database = new LiteDatabase(new MemoryStream());
using var sessionStore = new LiteDbMqttSessionStore(database);
var broker = new MqttBroker(new MqttBrokerOptions()
    .ListenTcp(1883)
    .UseSessionStore(sessionStore));

var packet = new MqttPublishPacket("smoke/topic", ReadOnlyMemory<byte>.Empty);
sessionStore.Save(new MqttSessionState("aot-smoke-session")
{
    Connected = false,
    WillMessage = packet,
});

Console.WriteLine($"{client.GetType().Name}:{broker.GetType().Name}:{packet.GetType().Name}");
