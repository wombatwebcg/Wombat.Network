# Wombat.Network

一个高性能、功能丰富的.NET网络通信库，支持TCP Socket、WebSocket以及基于System.IO.Pipelines的高性能I/O操作。

## 目录
- [简介](#简介)
- [特性详解](#特性详解)
- [安装](#安装)
- [快速入门](#快速入门)
- [配置选项](#配置选项)
- [进阶主题](#进阶主题)
- [性能优化建议](#性能优化建议)
- [贡献指南](#贡献指南)
- [许可信息](#许可信息)

## 简介

Wombat.Network是一个用于.NET平台的网络通信库，提供了简单易用但功能强大的API，用于构建高性能的网络应用程序。无论是构建实时通信应用、游戏服务器还是物联网设备通信，Wombat.Network都能满足您的需求。

### 主要功能

- **TCP Socket客户端/服务器**：提供可靠的TCP通信功能
- **WebSocket客户端/服务器**：支持标准WebSocket协议
- **高性能I/O处理**：基于System.IO.Pipelines的高效数据处理
- **多种帧格式**：支持多种数据帧格式，包括长度前缀、行分隔等
- **SSL/TLS支持**：内置安全通信支持
- **异步API**：全面支持异步编程模型
- **可配置性**：提供丰富的配置选项以满足不同场景需求
- **可扩展性**：易于扩展的架构设计

## 特性详解

### 高性能

- **基于System.IO.Pipelines**：利用最新的高性能I/O API
- **异步非阻塞I/O**：全面使用异步操作提高吞吐量
- **智能缓冲区管理**：高效的内存使用和缓冲区重用
- **高并发支持**：设计用于处理大量并发连接
- **超时控制**：精细的超时和取消操作支持

### 可靠性

- **健壮的错误处理**：全面的异常处理机制
- **自动重连**：可配置的连接恢复策略
- **连接状态管理**：详细的连接状态跟踪
- **资源自动释放**：实现IDisposable确保资源正确释放
- **日志支持**：集成Microsoft.Extensions.Logging支持

### 易用性

- **流畅的API设计**：简洁明了的方法命名和参数设计
- **全面的配置选项**：易于自定义的配置对象
- **直观的事件模型**：基于回调的事件处理
- **支持多种数据格式**：文本、二进制、JSON等

## 安装

### NuGet安装

```bash
dotnet add package Wombat.Network
```

或在Visual Studio的NuGet包管理器中搜索"Wombat.Network"。

### 手动安装

1. 从GitHub仓库克隆源代码：

```bash
git clone https://github.com/wombatwebcg/Wombat.Network.git
```

2. 构建解决方案：

```bash
cd Wombat.Network
dotnet build
```

3. 在您的项目中引用Wombat.Network.dll。

### 支持的平台

- .NET Standard 2.0+
- .NET Core 2.0+
- .NET 5/6/7/8

## 快速入门

以下是一些基本使用示例，帮助您快速上手：

### TCP Socket客户端

创建并使用TCP Socket客户端。[查看详细示例](#tcp-socket客户端)

### TCP Socket服务器

创建并使用TCP Socket服务器。[查看详细示例](#tcp-socket服务器)

### WebSocket客户端

创建并使用WebSocket客户端。[查看详细示例](#websocket客户端)

### WebSocket服务器

创建并使用WebSocket服务器。[查看详细示例](#websocket服务器)

### 高性能 Pipeline 连接

使用System.IO.Pipelines进行高性能Socket通信。[查看详细示例](#高性能-pipeline-连接)

## 配置选项

Wombat.Network提供了丰富的配置选项，使您能够根据具体需求定制网络组件的行为。

### TCP Socket客户端配置

`TcpSocketClientConfiguration`类提供以下主要配置选项：

| 配置项 | 说明 | 默认值 |
|-------|------|-------|
| `BufferManager` | 缓冲区管理器 | 新的SegmentBufferManager实例 |
| `FrameBuilder` | 帧构建器，决定如何解析数据包 | LengthPrefixedFrameBuilder |
| `ReceiveBufferSize` | 接收缓冲区大小（字节） | 8192 |
| `SendBufferSize` | 发送缓冲区大小（字节） | 8192 |
| `ReceiveTimeout` | 接收超时时间 | 30秒 |
| `SendTimeout` | 发送超时时间 | 30秒 |
| `ConnectTimeout` | 连接超时时间 | 30秒 |
| `NoDelay` | 是否禁用Nagle算法 | true |
| `SslEnabled` | 是否启用SSL/TLS | false |
| `OperationTimeout` | 一般操作超时时间 | 30秒 |
| `EnablePipelineIo` | 是否启用高性能Pipeline | true |
| `MaxConcurrentOperations` | 每个连接的最大并发操作数 | 10 |

### WebSocket客户端配置

`WebSocketClientConfiguration`类提供以下主要配置选项：

| 配置项 | 说明 | 默认值 |
|-------|------|-------|
| `BufferManager` | 缓冲区管理器 | 新的SegmentBufferManager实例 |
| `ReceiveBufferSize` | 接收缓冲区大小（字节） | 8192 |
| `SendBufferSize` | 发送缓冲区大小（字节） | 8192 |
| `ReceiveTimeout` | 接收超时时间 | 30秒 |
| `SendTimeout` | 发送超时时间 | 30秒 |
| `ConnectTimeout` | 连接超时时间 | 30秒 |
| `CloseTimeout` | 关闭超时时间 | 10秒 |
| `KeepAliveInterval` | 保活间隔时间 | 60秒 |
| `KeepAliveTimeout` | 保活超时时间 | 10秒 |
| `EnabledExtensions` | 启用的WebSocket扩展 | 默认包含PerMessageCompressionExtension |

### 帧构建器

Wombat.Network支持多种帧格式，您可以根据需要选择合适的帧构建器：

1. **LengthPrefixedFrameBuilder**：长度前缀帧，消息前加入长度字段
2. **LengthFieldBasedFrameBuilder**：基于长度字段的帧，可以灵活设置长度字段位置和大小
3. **LineBasedFrameBuilder**：基于行分隔符的帧，适合文本协议
4. **RawBufferFrameBuilder**：原始缓冲区帧，无特殊格式

示例：
```csharp
// 使用长度前缀帧构建器
config.FrameBuilder = new LengthPrefixedFrameBuilder();

// 使用基于行的帧构建器
config.FrameBuilder = new LineBasedFrameBuilder();

// 使用自定义长度字段帧构建器
config.FrameBuilder = new LengthFieldBasedFrameBuilder(
    lengthFieldOffset: 0,     // 长度字段起始位置
    lengthFieldLength: 4,     // 长度字段占用字节数
    lengthAdjustment: 0,      // 长度调整值
    initialBytesToStrip: 4    // 解码时跳过的初始字节数
);
```

## 进阶主题

### SSL/TLS配置

在TCP Socket和WebSocket客户端中启用SSL/TLS加密：

```csharp
// TCP Socket客户端SSL配置
var config = new TcpSocketClientConfiguration
{
    SslEnabled = true,
    SslTargetHost = "example.com",
    SslClientCertificates = new X509CertificateCollection
    {
        new X509Certificate2("client.pfx", "password")
    },
    SslEncryptionPolicy = EncryptionPolicy.RequireEncryption,
    SslEnabledProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
    SslCheckCertificateRevocation = true,
    SslPolicyErrorsBypassed = false
};

// WebSocket服务器SSL配置
var config = new WebSocketServerConfiguration
{
    SslEnabled = true,
    SslServerCertificate = new X509Certificate2("server.pfx", "password"),
    SslEnabledProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
    SslCheckCertificateRevocation = true,
    SslPolicyErrorsBypassed = false
};
```

### WebSocket扩展和子协议

WebSocket支持标准扩展和子协议的注册和使用：

```csharp
// 客户端启用压缩扩展
var config = new WebSocketClientConfiguration();
config.EnabledExtensions.Add(
    PerMessageCompressionExtension.RegisteredToken, 
    new PerMessageCompressionExtensionNegotiator()
);
config.OfferedExtensions.Add(
    new WebSocketExtensionOfferDescription(PerMessageCompressionExtension.RegisteredToken)
);

// 服务器端启用自定义子协议
var config = new WebSocketServerConfiguration();
config.EnabledSubProtocols.Add(
    "myprotocol", 
    new MyProtocolNegotiator()
);
```

### 自定义帧构建器

您可以通过实现`IFrameBuilder`接口创建自定义帧构建器：

```csharp
public class MyCustomFrameBuilder : IFrameBuilder
{
    public IFrameDecoder Decoder { get; }
    public IFrameEncoder Encoder { get; }
    
    public MyCustomFrameBuilder()
    {
        Decoder = new MyCustomFrameDecoder();
        Encoder = new MyCustomFrameEncoder();
    }
}

public class MyCustomFrameDecoder : IFrameDecoder
{
    public bool TryDecodeFrame(byte[] buffer, int offset, int count, out int frameLength, out byte[] payload, out int payloadOffset, out int payloadCount)
    {
        // 实现您的解码逻辑
    }
}

public class MyCustomFrameEncoder : IFrameEncoder
{
    public void EncodeFrame(byte[] payload, int offset, int count, out byte[] frameBuffer, out int frameBufferOffset, out int frameBufferLength)
    {
        // 实现您的编码逻辑
    }
}
```

## 性能优化建议

以下是一些提高Wombat.Network性能的建议：

### 1. 使用System.IO.Pipelines

启用Pipeline I/O以获得更高的吞吐量和更低的内存占用：

```csharp
var config = new TcpSocketClientConfiguration
{
    EnablePipelineIo = true
};
```

或直接使用`PipelineSocketConnection`类处理高性能Socket通信。

### 2. 框架兼容性注意事项

在不同的.NET框架版本中，Socket API的使用方式存在差异：

- **.NET Standard 2.0**: 使用`SocketAsyncEventArgs`进行异步操作，不支持`Socket.ReceiveAsync(Memory<byte>, SocketFlags)`和`Socket.SendAsync(ReadOnlyMemory<byte>, SocketFlags)`。
- **.NET Core 3.0+/.NET 5+**: 支持基于`Memory<T>`的异步Socket API，可以直接使用`await socket.ReceiveAsync(memory, SocketFlags.None)`。

如果您的项目面向.NET Standard 2.0，`PipelineSocketConnection`已经内部处理了这些差异，确保跨平台兼容性。对于自定义Socket操作，请使用兼容的API。

```csharp
// .NET Standard 2.0 兼容写法
var args = new SocketAsyncEventArgs();
args.SetBuffer(buffer, 0, buffer.Length);
args.Completed += OnOperationCompleted;
bool pending = socket.ReceiveAsync(args);
if (!pending) OnOperationCompleted(socket, args);

// .NET Core 3.0+/.NET 5+ 写法 (不兼容 .NET Standard 2.0)
int bytesReceived = await socket.ReceiveAsync(memory, SocketFlags.None);
```

### 3. 选择合适的帧格式

为您的应用场景选择最合适的帧格式：
- 对于二进制协议，使用`LengthFieldBasedFrameBuilder`
- 对于文本协议，使用`LineBasedFrameBuilder`
- 对于大量小消息，使用`RawBufferFrameBuilder`

### 4. 配置适当的缓冲区大小

根据您的消息大小设置合适的缓冲区参数：

```csharp
var config = new TcpSocketClientConfiguration
{
    ReceiveBufferSize = 16384,  // 更大的接收缓冲区
    SendBufferSize = 16384,     // 更大的发送缓冲区
    BufferManager = new SegmentBufferManager(
        maxCapacity: 1000,      // 最大缓冲区数量
        bufferSize: 16384,      // 每个缓冲区大小
        maxSegmentSize: 16,     // 每个缓冲段的最大大小
        isCircular: true        // 循环使用缓冲区
    )
};
```

### 5. 优化连接参数

调整连接和超时参数以获得更好的性能：

```csharp
var config = new TcpSocketClientConfiguration
{
    NoDelay = true,               // 禁用Nagle算法
    MaxConcurrentOperations = 20, // 增加并发操作数
    OperationTimeout = TimeSpan.FromSeconds(5), // 减少操作超时
    KeepAlive = true,             // 启用TCP保活
    KeepAliveInterval = TimeSpan.FromSeconds(30) // 设置保活间隔
};
```

### 6. 减少内存分配

- 重用缓冲区而不是每次分配新内存
- 使用`ArrayPool<byte>`或缓冲区池管理内存
- 避免不必要的数据复制操作

## 贡献指南

我们欢迎社区的贡献！如果您想参与Wombat.Network的开发，请遵循以下步骤：

1. Fork仓库到您的GitHub账号
2. 创建新的特性分支 (`git checkout -b feature/amazing-feature`)
3. 提交您的更改 (`git commit -m 'Add some amazing feature'`)
4. 推送到分支 (`git push origin feature/amazing-feature`)
5. 创建一个Pull Request

### 代码规范

- 请遵循现有的代码风格和命名约定
- 为所有公共API添加XML注释
- 确保所有单元测试通过
- 添加新功能时应包含相应的单元测试

## 许可信息

Wombat.Network使用MIT许可证发布。详细信息请参见[LICENSE](LICENSE)文件。

---

© 2023 wombatwebcg. 保留所有权利。