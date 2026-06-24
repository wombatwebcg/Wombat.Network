# Wombat.Network.Mqtt

`Wombat.Network.Mqtt` 是一套基于 .NET Standard 2.0 的 MQTT 组件，包含：

- `Wombat.Network.Mqtt.Client`：MQTT 客户端
- `Wombat.Network.Mqtt.Broker`：内嵌 MQTT Broker
- `Wombat.Network.Mqtt.Transport`：TCP / TLS / WebSocket / WSS 传输适配
- `Wombat.Network.Mqtt.Protocol`：MQTT 报文模型与编解码
- `Wombat.Network.Mqtt.Persistence.LiteDb`：基于 LiteDB 的会话与保留消息持久化
- `Wombat.Network.Mqtt.Abstractions`：公共抽象

这个目录是一个解决方案，不是单一项目。实际使用时，按需引用其中的子项目或 NuGet 包。

## 适用场景

适合以下场景：

- 在服务端进程内启动一个轻量 MQTT Broker
- 在应用中直接连接 MQTT Broker 进行发布和订阅
- 需要支持 TCP、TLS、WebSocket、WebSocket Secure
- 需要会话状态与 Retained Message 持久化
- 需要在 Broker 侧插入连接、订阅、发布钩子

## 当前实现能力

从代码实现来看，目前已覆盖的核心能力包括：

- 协议版本：MQTT 3.1.1、MQTT 5.0
- 报文类型：
  - `CONNECT`
  - `CONNACK`
  - `PUBLISH`
  - `PUBACK`
  - `PUBREC`
  - `PUBREL`
  - `PUBCOMP`
  - `SUBSCRIBE`
  - `SUBACK`
  - `PINGREQ`
  - `PINGRESP`
  - `DISCONNECT`
- QoS：`0`、`1`、`2`
- 保留消息：支持
- 遗嘱消息：支持
- 用户名/密码认证：支持
- 会话存储：内存 / LiteDB
- 传输层：
  - TCP
  - TLS
  - WebSocket
  - WebSocket Secure

## 安装与项目引用

最常见的引用方式：

```xml
<ItemGroup>
  <ProjectReference Include="..\Wombat.Network.Mqtt.Client\Wombat.Network.Mqtt.Client.csproj" />
  <ProjectReference Include="..\Wombat.Network.Mqtt.Broker\Wombat.Network.Mqtt.Broker.csproj" />
</ItemGroup>
```

如果你只需要客户端：

```xml
<ItemGroup>
  <ProjectReference Include="..\Wombat.Network.Mqtt.Client\Wombat.Network.Mqtt.Client.csproj" />
</ItemGroup>
```

如果你需要 LiteDB 持久化：

```xml
<ItemGroup>
  <ProjectReference Include="..\Wombat.Network.Mqtt.Persistence.LiteDb\Wombat.Network.Mqtt.Persistence.LiteDb.csproj" />
</ItemGroup>
```

## 包之间的关系

- `Client` 依赖 `Abstractions`、`Protocol`、`Transport`
- `Broker` 依赖 `Abstractions`、`Protocol`、`Transport`
- `Persistence.LiteDb` 依赖 `Broker`、`Protocol`
- `Transport` 提供默认的 `MqttConnectionFactory`

通常：

- 想连别人的 MQTT 服务，用 `Client`
- 想自己在进程内开 Broker，用 `Broker`
- 想持久化会话和 retained message，再加 `Persistence.LiteDb`

## 快速开始

### 1. 启动一个最简单的 Broker

```csharp
using System.Threading.Tasks;
using Wombat.Network.Mqtt.Broker;

var broker = new MqttBroker(new MqttBrokerOptions()
    .ListenTcp(1883));

await broker.StartAsync();

Console.WriteLine("Broker started on tcp://0.0.0.0:1883");
Console.ReadLine();

await broker.StopAsync();
```

### 2. 用客户端连接 Broker

```csharp
using Wombat.Network.Mqtt.Abstractions;
using Wombat.Network.Mqtt.Client;

var client = new MqttClient(new MqttClientOptions
{
    Endpoint = new MqttEndpoint("127.0.0.1", 1883, MqttTransportScheme.Tcp),
    ClientId = "demo-client",
    CleanStart = true,
    KeepAliveSeconds = 30,
});

var connAck = await client.ConnectAsync();
Console.WriteLine($"Connected, reason code: {connAck.ReasonCode}");
```

### 3. 订阅主题

```csharp
using Wombat.Network.Mqtt.Protocol;

await client.SubscribeAsync("demo/topic", MqttQualityOfService.AtLeastOnce);
```

### 4. 发布消息

```csharp
using System.Text;
using Wombat.Network.Mqtt.Protocol;

await client.PublishAsync(
    "demo/topic",
    Encoding.UTF8.GetBytes("hello mqtt"),
    MqttQualityOfService.AtLeastOnce);
```

### 5. 接收消息

`ReceiveAsync()` 会返回一个 `MqttPacket`，需要自行判断类型：

```csharp
using System;
using System.Text;
using Wombat.Network.Mqtt.Protocol;

var packet = await client.ReceiveAsync();

if (packet is MqttPublishPacket publish)
{
    var text = Encoding.UTF8.GetString(publish.Payload.ToArray());
    Console.WriteLine($"{publish.Topic}: {text}");
}
```

### 6. 断开连接

```csharp
await client.DisconnectAsync();
```

## Broker 用法

### 监听不同协议

```csharp
var options = new MqttBrokerOptions()
    .ListenTcp(1883)
    .ListenWebSocket(8083, "/mqtt");
```

支持的方法：

- `ListenTcp(int port)`
- `ListenTls(int port)`
- `ListenWebSocket(int port, string path = "/mqtt")`
- `ListenWebSocketSecure(int port, string path = "/mqtt")`

### 用户名密码认证

```csharp
var broker = new MqttBroker(new MqttBrokerOptions()
    .ListenTcp(1883)
    .UseCredentials("admin", "123456"));
```

客户端连接时设置：

```csharp
var client = new MqttClient(new MqttClientOptions
{
    Endpoint = new MqttEndpoint("127.0.0.1", 1883),
    ClientId = "auth-client",
    Username = "admin",
    Password = "123456",
});
```

Broker 当前认证逻辑是固定用户名/密码字符串全等比较。

### TLS / WSS

如果 Broker 使用 `ListenTls()` 或 `ListenWebSocketSecure()`，必须配置服务端证书：

```csharp
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

var certificate = new X509Certificate2("server.pfx", "password");

var broker = new MqttBroker(new MqttBrokerOptions()
    .ListenTls(8883)
    .ListenWebSocketSecure(8443, "/mqtt")
    .UseServerCertificate(
        certificate,
        clientCertificateRequired: false,
        enabledSslProtocols: SslProtocols.Tls12,
        checkCertificateRevocation: false));
```

客户端连接 TLS：

```csharp
using System.Net.Security;
using Wombat.Network.Mqtt.Abstractions;

var endpoint = new MqttEndpoint(
    host: "localhost",
    port: 8883,
    scheme: MqttTransportScheme.Tls,
    serverCertificateValidationCallback: static (_, _, _, _) => true);
```

客户端连接 WSS：

```csharp
var endpoint = new MqttEndpoint(
    host: "localhost",
    port: 8443,
    scheme: MqttTransportScheme.WebSocketSecure,
    path: "/mqtt",
    serverCertificateValidationCallback: static (_, _, _, _) => true);
```

`serverCertificateValidationCallback` 用于客户端证书校验。示例里直接返回 `true` 只适合开发环境。

### WebSocket

Broker：

```csharp
var broker = new MqttBroker(new MqttBrokerOptions()
    .ListenWebSocket(8083, "/mqtt"));
```

客户端：

```csharp
var client = new MqttClient(new MqttClientOptions
{
    Endpoint = new MqttEndpoint("localhost", 8083, MqttTransportScheme.WebSocket, "/mqtt"),
    ClientId = "ws-client",
});

await client.ConnectAsync();
```

注意：

- Broker 会校验 WebSocket 请求路径
- 客户端和服务端的 `path` 必须一致
- 默认路径是 `/mqtt`

## Client 用法

### MqttClientOptions

`MqttClientOptions` 主要参数：

- `Endpoint`：目标地址，必填
- `ClientId`：客户端 ID，默认 `wombat-client`
- `KeepAliveSeconds`：默认 `30`
- `CleanStart`：默认 `true`
- `WillMessage`：遗嘱消息
- `ProtocolVersion`：默认 `MqttProtocolVersion.V500`
- `Username` / `Password`：认证信息

### 连接示例

```csharp
using Wombat.Network.Mqtt.Abstractions;
using Wombat.Network.Mqtt.Client;
using Wombat.Network.Mqtt.Protocol;

var client = new MqttClient(new MqttClientOptions
{
    Endpoint = new MqttEndpoint("localhost", 1883, MqttTransportScheme.Tcp),
    ClientId = "sample-client",
    KeepAliveSeconds = 15,
    CleanStart = true,
    ProtocolVersion = MqttProtocolVersion.V500,
});

await client.ConnectAsync();
```

### 遗嘱消息

```csharp
using System.Text;
using Wombat.Network.Mqtt.Protocol;

var willMessage = new MqttPublishPacket(
    topic: "client/status",
    payload: Encoding.UTF8.GetBytes("offline"),
    qualityOfService: MqttQualityOfService.AtLeastOnce,
    retain: true);

var client = new MqttClient(new MqttClientOptions
{
    Endpoint = new MqttEndpoint("localhost", 1883),
    ClientId = "device-01",
    WillMessage = willMessage,
});
```

如果客户端异常断开，Broker 会发布该遗嘱消息。

### 批量订阅

```csharp
using Wombat.Network.Mqtt.Protocol;

await client.SubscribeAsync(new[]
{
    new MqttSubscription("device/+/status", MqttQualityOfService.AtMostOnce),
    new MqttSubscription("device/+/telemetry", MqttQualityOfService.AtLeastOnce),
});
```

### 心跳

```csharp
await client.PingAsync();
```

## 持久化

默认情况下，Broker 使用 `InMemoryMqttSessionStore`，进程结束后数据丢失。

如果需要持久化会话状态、QoS 2 inflight 状态、订阅信息和保留消息，可以使用 `LiteDbMqttSessionStore`。

### 使用 LiteDB 持久化

```csharp
using Wombat.Network.Mqtt.Broker;
using Wombat.Network.Mqtt.Persistence.LiteDb;

using var sessionStore = new LiteDbMqttSessionStore("Filename=mqtt.db;Connection=shared");

var broker = new MqttBroker(new MqttBrokerOptions()
    .ListenTcp(1883)
    .UseSessionStore(sessionStore));

await broker.StartAsync();
```

也可以传入现成的 `LiteDatabase`：

```csharp
using LiteDB;
using Wombat.Network.Mqtt.Persistence.LiteDb;

using var database = new LiteDatabase("Filename=mqtt.db;Connection=shared");
using var sessionStore = new LiteDbMqttSessionStore(database);
```

### 自定义会话存储

实现 `IMqttSessionStore` 即可：

```csharp
public interface IMqttSessionStore
{
    MqttSessionState Get(string clientId);
    IReadOnlyCollection<MqttSessionState> GetAll();
    void Save(MqttSessionState session);
    void Remove(string clientId);
    IReadOnlyCollection<MqttPublishPacket> GetRetainedMessages();
    void SaveRetainedMessage(MqttPublishPacket message);
    void RemoveRetainedMessage(string topic);
}
```

然后注册到 Broker：

```csharp
var broker = new MqttBroker(new MqttBrokerOptions()
    .ListenTcp(1883)
    .UseSessionStore(mySessionStore));
```

## 插件扩展

Broker 支持通过 `IMqttBrokerPlugin` 插入钩子。

扩展点包括：

- 客户端连接后：`OnClientConnectedAsync`
- 客户端断开后：`OnClientDisconnectedAsync`
- 客户端订阅后：`OnSubscribedAsync`
- Broker 路由发布前：`OnPublishingAsync`

### 插件示例

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Mqtt.Broker;

[MqttBrokerPlugin]
public sealed class LoggingPlugin : MqttBrokerPlugin
{
    public override Task OnClientConnectedAsync(MqttBrokerConnectionContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Connected: {context.Session.ClientId}");
        return Task.CompletedTask;
    }

    public override Task OnPublishingAsync(MqttBrokerPublishContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Publishing: {context.PublishPacket.Topic}");
        return Task.CompletedTask;
    }
}
```

注册方式：

```csharp
var broker = new MqttBroker(new MqttBrokerOptions()
    .ListenTcp(1883)
    .UsePlugin(new LoggingPlugin()));
```

## 传输层说明

客户端连接地址使用 `MqttEndpoint`：

```csharp
var endpoint = new MqttEndpoint(
    host: "localhost",
    port: 1883,
    scheme: MqttTransportScheme.Tcp,
    path: "/mqtt");
```

参数说明：

- `host`：主机名或 IP
- `port`：端口
- `scheme`：传输方式
- `path`：WebSocket 路径，TCP/TLS 场景下可忽略
- `serverCertificateValidationCallback`：TLS/WSS 客户端证书校验回调

`MqttTransportScheme` 枚举值：

- `Tcp`
- `Tls`
- `WebSocket`
- `WebSocketSecure`

## 一段完整示例

下面的示例在同一进程内启动 Broker，然后创建一个 Client 连接、订阅并发布消息：

```csharp
using System;
using System.Text;
using System.Threading.Tasks;
using Wombat.Network.Mqtt.Abstractions;
using Wombat.Network.Mqtt.Broker;
using Wombat.Network.Mqtt.Client;
using Wombat.Network.Mqtt.Protocol;

var broker = new MqttBroker(new MqttBrokerOptions()
    .ListenTcp(1883));

await broker.StartAsync();

var client = new MqttClient(new MqttClientOptions
{
    Endpoint = new MqttEndpoint("localhost", 1883, MqttTransportScheme.Tcp),
    ClientId = "sample-client",
});

await client.ConnectAsync();
await client.SubscribeAsync("demo/topic", MqttQualityOfService.AtLeastOnce);
await client.PublishAsync("demo/topic", Encoding.UTF8.GetBytes("hello"), MqttQualityOfService.AtLeastOnce);

var packet = await client.ReceiveAsync();
if (packet is MqttPublishPacket publish)
{
    Console.WriteLine($"{publish.Topic}: {Encoding.UTF8.GetString(publish.Payload.ToArray())}");
}

await client.DisconnectAsync();
await broker.StopAsync();
```

## 注意事项

- `MqttClient.ReceiveAsync()` 返回原始 `MqttPacket`，调用方需要自己判断具体类型
- `ListenTls()` 和 `ListenWebSocketSecure()` 必须先配置 `UseServerCertificate()`
- WebSocket 请求路径必须与 Broker 配置一致
- 默认会话存储是内存实现，重启不会保留数据
- Broker 当前用户名密码认证是固定值比较，不是可插拔认证链
- 如果使用自签名证书，客户端需要通过 `serverCertificateValidationCallback` 自行处理校验

## 目录说明

- `Wombat.Network.Mqtt.Abstractions`：连接抽象、端点、传输协议枚举
- `Wombat.Network.Mqtt.Protocol`：MQTT 报文与编解码
- `Wombat.Network.Mqtt.Transport`：TCP/TLS/WebSocket/WSS 适配
- `Wombat.Network.Mqtt.Client`：客户端 API
- `Wombat.Network.Mqtt.Broker`：Broker 核心实现
- `Wombat.Network.Mqtt.Persistence.LiteDb`：LiteDB 持久化实现
- `Wombat.Network.Mqtt.Generators`：生成器项目
- `Wombat.Network.Mqtt.AotSmoke`：AOT smoke 示例

## 参考

仓库中可直接查看的示例：

- `Wombat.Network.Mqtt.AotSmoke/Program.cs`

