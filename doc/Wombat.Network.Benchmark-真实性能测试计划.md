# Wombat.Network.Benchmark 真实性能测试计划

## 目标

在 `Wombat.Network.Benchmark` 中补上针对当前 `Wombat.Network` 新模型的真实性能测试，优先覆盖真实热路径，而不是已经删除的旧架构。

本计划只做两件事：

1. 测当前仍存在的核心编解码与收发路径
2. 产出可重复执行、可比较版本差异的 BenchmarkDotNet 基准

## 现状

当前仓库情况：

- `Wombat.Network.Benchmark` 只剩 `Program.cs` 和 `.csproj`
- 旧 benchmark 文件已经删除，不能直接恢复照搬
- 主库的新热路径集中在以下类型
  - `LengthFieldMessagePipe`
  - `WebSocketFrameCodec`
  - `TcpTransportConnection` + `StreamMessageChannel`
  - `UdpDatagramTransport` + `DatagramMessageChannel`
  - `WebSocketMessageChannel`

这意味着 benchmark 应该围绕“当前真实代码路径”重建，而不是围绕旧目录名补壳。

## 范围

本次 benchmark 分成两层。

### 第一层：纯内存微基准

目标是先测算法和分配成本，结果稳定，改动最小。

包含：

- `LengthFieldMessagePipe`
  - `WriteAsync`
  - `TryRead`
  - 单帧读取
  - 多帧连续读取
  - 不同长度头：`1/2/4/8` 字节
- `WebSocketFrameCodec`
  - `EncodeBinary`
  - `EncodeText`
  - `TryDecode`
  - `masked` / `unmasked`

### 第二层：loopback 端到端基准

目标是测真实 socket 路径，不用 mock，不用 fake。

包含：

- `TcpTransportConnection` + `StreamMessageChannel`
  - 单次 request/response 延迟
  - 固定消息数吞吐量
- `UdpDatagramTransport` + `DatagramMessageChannel`
  - 裸 datagram 收发
  - 带 `LengthFieldMessagePipe` 的 datagram 收发
- `WebSocketMessageChannel`
  - 握手完成后的 text/binary message 收发

## 明确不测

第一版不测这些内容：

- 已删除的旧 `SocketServer`、旧 framing、旧 websocket 扩展路径
- 日志、DI、控制台输出
- 把完整 WebSocket 握手混入主消息收发 benchmark
- 跨机器网络、弱网、丢包、TLS

原因很简单：这些不是当前最短路径上的主热点，先不写。

## Benchmark 项目结构

建议保持少文件，够用就行：

- `Wombat.Network.Benchmark/Program.cs`
- `Wombat.Network.Benchmark/BenchmarkConfig.cs`
- `Wombat.Network.Benchmark/BenchmarkData.cs`
- `Wombat.Network.Benchmark/Benchmarks/LengthFieldMessagePipeBenchmarks.cs`
- `Wombat.Network.Benchmark/Benchmarks/WebSocketFrameCodecBenchmarks.cs`
- `Wombat.Network.Benchmark/Benchmarks/TcpChannelBenchmarks.cs`
- `Wombat.Network.Benchmark/Benchmarks/UdpChannelBenchmarks.cs`
- `Wombat.Network.Benchmark/Benchmarks/WebSocketChannelBenchmarks.cs`

## 参数设计

统一参数，避免每个 benchmark 自己发明一套：

- Payload 大小：`64`、`512`、`4096`、`32768`
- 消息数量：`1`、`1000`
- WebSocket 掩码：`true/false`
- LengthField：`OneByte`、`TwoBytes`、`FourBytes`、`EigthBytes`

原则：

- 小包看固定开销
- 中包看常规路径
- 大包看拷贝和分配压力

## 结果要求

每个 benchmark 至少输出：

- 平均耗时
- 吞吐量或每操作耗时
- 内存分配 `Allocated`
- `Gen0` 情况

结果要能用于回答这几个问题：

1. 当前哪条路径最耗时
2. 当前哪条路径分配最多
3. 包大小变化后，热点是否发生变化
4. 后续优化是否真的有效

## 实施顺序

### 阶段 1

先补最小基础设施：

- `BenchmarkConfig`
- 公共测试数据生成
- 参数统一
- `MemoryDiagnoser`

### 阶段 2

补纯内存 benchmark：

- `LengthFieldMessagePipeBenchmarks`
- `WebSocketFrameCodecBenchmarks`

这是第一批必须先落地的内容，因为：

- 最便宜
- 最稳定
- 最容易先暴露明显分配热点

### 阶段 3

补真实 loopback benchmark：

- `TcpChannelBenchmarks`
- `UdpChannelBenchmarks`

要求 setup 里真的建立连接并完成一次收发，避免测到空路径。

### 阶段 4

补 `WebSocketMessageChannel` 端到端 benchmark：

- text message
- binary message
- client/server 方向分开看

## 验收标准

完成后至少满足：

1. `dotnet run -c Release --project Wombat.Network.Benchmark` 可以直接执行
2. 至少有 4 组有效 benchmark
3. 每组 benchmark 都能看到耗时和分配结果
4. benchmark 测的都是当前代码真实路径，不是 mock 路径
5. 能据此识别第一批优化点

## 预期会先暴露的热点

按当前代码看，优先怀疑这些地方：

- `WebSocketFrameCodec` 中多次 `ToArray()`
- `EncodePing/EncodePong` 中字节和字符串互转
- `UdpDatagramTransport.SendAsync` 中 `payload.ToArray()`
- 各类 channel 收发中的额外复制

这部分先通过 benchmark 证实，再决定要不要优化。不要先写优化，再补测试。

## 推荐执行方式

建议按两批推进：

第一批：

- `LengthFieldMessagePipe`
- `WebSocketFrameCodec`

第二批：

- `Tcp`
- `Udp`
- `WebSocketChannel`

这样第一天就能拿到可用数据，不必等全部场景做完。

## 结论

本计划的重点不是“把 benchmark 项目补完整”，而是用最少文件把当前真实热路径测出来。

先做内存路径，再做 loopback 真实收发；先拿数据，再决定优化。
