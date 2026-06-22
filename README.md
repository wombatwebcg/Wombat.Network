# Wombat.Network

`Wombat.Network` 现已切换到统一通信模型，只保留新接口：

- 流式传输：`ITransportConnection` / `ITransportListener`
- 消息通道：`IMessageChannel`
- framing：`IMessagePipe`
- 已提供实现：TCP、UDP、TLS、Serial、WebSocket channel

目标框架保持 `netstandard2.0`，代码内部基于 `System.IO.Pipelines`、`ReadOnlySequence<byte>`、`Memory<T>` 组织。

## 安装

```bash
dotnet add package Wombat.Network
```

## 核心接口

```csharp
public interface ITransportConnection
{
    string Id { get; }
    EndPoint LocalEndPoint { get; }
    EndPoint RemoteEndPoint { get; }
    IDuplexPipe Transport { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}

public interface ITransportListener
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}

public interface IMessageChannel
{
    string Id { get; }
    ValueTask SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default);
    ValueTask<ReceivedMessage?> ReceiveAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}
```

## TCP 示例

```csharp
using System.Buffers;
using System.Net;
using Wombat.Network.Channels;
using Wombat.Network.Protocols.Framing;
using Wombat.Network.Transports.Tcp;

var endPoint = new IPEndPoint(IPAddress.Loopback, 9000);
var listener = new TcpTransportListener(endPoint);
var pipe = new LengthFieldMessagePipe(LengthField.FourBytes);

await listener.StartAsync();

_ = Task.Run(async () =>
{
    var accepted = await listener.AcceptAsync();
    await accepted.StartAsync();

    var serverChannel = new StreamMessageChannel(accepted, pipe);
    var inbound = await serverChannel.ReceiveAsync();
    if (inbound.HasValue)
    {
        await serverChannel.SendAsync(ToArray(inbound.Value.Payload));
    }
});

var client = await TcpTransportConnection.ConnectAsync(endPoint);
await client.StartAsync();

var clientChannel = new StreamMessageChannel(client, pipe);
await clientChannel.SendAsync(new byte[] { 1, 2, 3, 4 });
var echoed = await clientChannel.ReceiveAsync();
```

## UDP 示例

```csharp
using System.Net;
using Wombat.Network.Channels;
using Wombat.Network.Transports.Udp;

var serverEndPoint = new IPEndPoint(IPAddress.Loopback, 9001);
using var serverTransport = new UdpDatagramTransport(serverEndPoint);
using var clientTransport = new UdpDatagramTransport(defaultRemoteEndPoint: serverEndPoint);

await serverTransport.StartAsync();
await clientTransport.StartAsync();

var serverChannel = new DatagramMessageChannel(serverTransport);
var clientChannel = new DatagramMessageChannel(clientTransport, serverEndPoint);

await clientChannel.SendAsync(new byte[] { 1, 2, 3 });
var received = await serverChannel.ReceiveAsync();
```

## WebSocket 示例

```csharp
using System.Net;
using Wombat.Network.Channels;
using Wombat.Network.Protocols.WebSocket;
using Wombat.Network.Transports.Tcp;

var port = 9002;
var listener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, port));
await listener.StartAsync();

_ = Task.Run(async () =>
{
    var accepted = (TcpTransportConnection)await listener.AcceptAsync();
    await accepted.StartAsync();
    await WebSocketHandshakeMiddleware.AcceptServerAsync(accepted);

    var serverChannel = new WebSocketMessageChannel(accepted, isClient: false);
    var inbound = await serverChannel.ReceiveAsync();
    if (inbound.HasValue)
    {
        await serverChannel.SendTextAsync("pong");
    }
});

var client = await TcpTransportConnection.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
await client.StartAsync();
await WebSocketHandshakeMiddleware.AcceptClientAsync(client, $"127.0.0.1:{port}", "/chat");

var channel = new WebSocketMessageChannel(client, isClient: true);
await channel.SendTextAsync("ping");
var echoed = await channel.ReceiveAsync();
```

## 目录

```text
Wombat.Network/
  Transports/
    Abstractions/
    Tcp/
    Udp/
    Tls/
    Serial/
  Channels/
  Protocols/
    Framing/
    WebSocket/
  Pipelines/
```

## 验证

- 单测：`Wombat.Network.UnitTest/NewModel`
- 基准：`Wombat.Network.Benchmark/Benchmarks/New*`

## 破坏性升级

旧 `TcpSocket*`、`UdpSocket*`、旧 `WebSocketClient/Server/Session`、`PipelineSocketConnection`、旧 DI/build 包装层已删除。

如果你还在用旧 API，需要按新模型重写到：

- TCP/Serial/TLS：`Transport + StreamMessageChannel + IMessagePipe`
- UDP：`UdpDatagramTransport + DatagramMessageChannel`
- WebSocket：`TcpTransportConnection + WebSocketHandshakeMiddleware + WebSocketMessageChannel`
