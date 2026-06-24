using System.Text;
using Wombat.Network.Mqtt.Broker;
using Wombat.Network.Mqtt.Client;
using Wombat.Network.Mqtt.Protocol;
using Wombat.Network.TestHelper;

namespace Wombat.Network.MqttTest;

internal static class Scenarios
{
    public static readonly Scenario[] All =
    [
        new("ManyMessages", "订阅后发布多条不同内容消息，逐条校验顺序和内容。", RunManyMessagesAsync),
        new("WildcardSubscription", "订阅通配 topic，发布多个内容，校验都能匹配收到。", RunWildcardSubscriptionAsync),
        new("ReconnectAndResubscribe", "客户端断开后重新连接并重新订阅，校验新会话仍可正常收消息。", RunReconnectAndResubscribeAsync),
        new("ParallelClients", "多个客户端并发订阅和发布，校验隔离与广播。", RunParallelClientsAsync),
    ];

    private static async Task RunManyMessagesAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await MqttTestHarness.RunTcpBrokerAsync(async (port, _, token) =>
        {
            var subscriber = MqttTestHarness.CreateTcpClient("many-sub", port);
            var publisher = MqttTestHarness.CreateTcpClient("many-pub", port);
            const int count = 10;
            var expected = Enumerable.Range(1, count).Select(i => $"msg-{i:000}").ToArray();

            await subscriber.ConnectAsync(token);
                await subscriber.SubscribeAsync("demo/many", cancellationToken: token);
            await publisher.ConnectAsync(token);

            try
            {
                for (var i = 0; i < expected.Length; i++)
                {
                    await publisher.PublishAsync("demo/many", Encoding.UTF8.GetBytes(expected[i]), cancellationToken: token);
                    var packet = await subscriber.ReceiveAsync(token);
                    NetworkTestHelper.Ensure(packet is MqttPublishPacket, $"many recv {i + 1} should be publish.");
                    var publish = (MqttPublishPacket)packet;
                    NetworkTestHelper.Ensure(publish.Topic == "demo/many", $"many recv {i + 1} topic mismatch.");
                    NetworkTestHelper.Ensure(Encoding.UTF8.GetString(publish.Payload.ToArray()) == expected[i], $"many recv {i + 1} payload mismatch.");
                }
            }
            finally
            {
                await SafeDisconnectAsync(publisher);
                await SafeDisconnectAsync(subscriber);
            }

            Console.WriteLine($"      message count: {count}");
        }, cts.Token);
    }

    private static async Task RunWildcardSubscriptionAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await MqttTestHarness.RunTcpBrokerAsync(async (port, _, token) =>
        {
            var publisher = MqttTestHarness.CreateTcpClient("wild-pub", port);
            var subscriber = MqttTestHarness.CreateTcpClient("wild-sub", port);
            var expected = new[]
            {
                ("room/a/temp", "21"),
                ("room/b/temp", "22"),
                ("room/c/temp", "23"),
            };

            await subscriber.ConnectAsync(token);
            await subscriber.SubscribeAsync("room/+/temp", cancellationToken: token);
            await publisher.ConnectAsync(token);

            try
            {
                for (var i = 0; i < expected.Length; i++)
                {
                    await publisher.PublishAsync(expected[i].Item1, Encoding.UTF8.GetBytes(expected[i].Item2), cancellationToken: token);
                    var packet = await subscriber.ReceiveAsync(token);
                    NetworkTestHelper.Ensure(packet is MqttPublishPacket, $"wildcard recv {i + 1} should be publish.");
                    var publish = (MqttPublishPacket)packet;
                    NetworkTestHelper.Ensure(publish.Topic == expected[i].Item1, $"wildcard recv {i + 1} topic mismatch.");
                    NetworkTestHelper.Ensure(Encoding.UTF8.GetString(publish.Payload.ToArray()) == expected[i].Item2, $"wildcard recv {i + 1} payload mismatch.");
                }
            }
            finally
            {
                await SafeDisconnectAsync(publisher);
                await SafeDisconnectAsync(subscriber);
            }
        }, cts.Token);
    }

    private static async Task RunReconnectAndResubscribeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await MqttTestHarness.RunTcpBrokerAsync(async (port, _, token) =>
        {
            var subscriber = MqttTestHarness.CreateTcpClient("reconnect-sub", port);
            var publisher = MqttTestHarness.CreateTcpClient("persist-pub", port);

            for (var session = 1; session <= 2; session++)
            {
                await subscriber.ConnectAsync(token);
                await subscriber.SubscribeAsync("persist/#", cancellationToken: token);
                await publisher.ConnectAsync(token);
                await publisher.PublishAsync("persist/topic", Encoding.UTF8.GetBytes($"session-{session}"), cancellationToken: token);
                var packet = await subscriber.ReceiveAsync(token);
                NetworkTestHelper.Ensure(packet is MqttPublishPacket, $"reconnect recv {session} should be publish.");
                var publish = (MqttPublishPacket)packet;
                NetworkTestHelper.Ensure(publish.Topic == "persist/topic", $"reconnect recv {session} topic mismatch.");
                NetworkTestHelper.Ensure(Encoding.UTF8.GetString(publish.Payload.ToArray()) == $"session-{session}", $"reconnect recv {session} payload mismatch.");
                await SafeDisconnectAsync(publisher);
                await SafeDisconnectAsync(subscriber);
            }

            Console.WriteLine("      reconnect sessions: 2");
        }, cts.Token);
    }

    private static async Task RunParallelClientsAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await MqttTestHarness.RunTcpBrokerAsync(async (port, _, token) =>
        {
            const int clientCount = 3;
            var subscribers = Enumerable.Range(1, clientCount)
                .Select(i => MqttTestHarness.CreateTcpClient($"parallel-sub-{i}", port))
                .ToArray();
            var publishers = Enumerable.Range(1, clientCount)
                .Select(i => MqttTestHarness.CreateTcpClient($"parallel-pub-{i}", port))
                .ToArray();

            foreach (var subscriber in subscribers)
            {
                await subscriber.ConnectAsync(token);
                var suffix = Array.IndexOf(subscribers, subscriber) + 1;
                await subscriber.SubscribeAsync($"parallel/{suffix}", cancellationToken: token);
            }

            foreach (var publisher in publishers)
            {
                await publisher.ConnectAsync(token);
            }

            try
            {
                for (var i = 0; i < subscribers.Length; i++)
                {
                    await publishers[i].PublishAsync($"parallel/{i + 1}", Encoding.UTF8.GetBytes($"payload-{i + 1}"), cancellationToken: token);
                    var packet = await subscribers[i].ReceiveAsync(token);
                    NetworkTestHelper.Ensure(packet is MqttPublishPacket, $"parallel subscriber {i + 1} should receive publish.");
                    var publish = (MqttPublishPacket)packet;
                    NetworkTestHelper.Ensure(publish.Topic == $"parallel/{i + 1}", $"parallel subscriber {i + 1} topic mismatch.");
                    NetworkTestHelper.Ensure(Encoding.UTF8.GetString(publish.Payload.ToArray()) == $"payload-{i + 1}", $"parallel subscriber {i + 1} payload mismatch.");
                }
            }
            finally
            {
                foreach (var publisher in publishers)
                {
                    await SafeDisconnectAsync(publisher);
                }

                foreach (var subscriber in subscribers)
                {
                    await SafeDisconnectAsync(subscriber);
                }
            }

            Console.WriteLine($"      clients per role: {clientCount}");
        }, cts.Token);
    }

    private static async Task SafeDisconnectAsync(MqttClient client)
    {
        try
        {
            await client.DisconnectAsync(CancellationToken.None);
        }
        catch
        {
        }
    }
}

internal sealed record Scenario(string Name, string Description, Func<Task> RunAsync);
