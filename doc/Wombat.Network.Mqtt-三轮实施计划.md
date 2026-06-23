# Wombat.Network.Mqtt 三轮实施计划

## 1. 目标

在 `D:\Wombat\Wombat.Network\Wombat.Network\Wombat.Network.Mqtt` 下创建新的 MQTT 库，范围包含：

- MQTT Client
- MQTT Broker Server
- MQTT 5.0 作为默认协议
- MQTT 3.1.1 兼容层
- TCP/TLS 与 WebSocket Secure/Non-Secure 传输入口
- 全面异步化
- NativeAOT 兼容
- `netstandard2.0` 目标框架

实现原则：

- 优先复用 `Wombat.Network` 的通用连接与管道能力，不把 MQTT 特定状态机回灌到基础库
- 底层优先使用 `SocketAsyncEventArgs`、`System.IO.Pipelines`、`ArrayPool<byte>`、`Memory<byte>`、`ReadOnlyMemory<byte>`
- 减少 `byte[]` 暴露与临时复制
- 避免 `MemoryStream`、`Stream.CopyTo`
- 主热路径以零拷贝和最少 GC 为目标
- 反射依赖不用运行时扫描，改为显式注册或源码生成
- Broker 采用 `Core + Plugin`，避免所有能力耦合在一个类库中
- 日志统一使用 `ILogger`
- 安全默认值优先 TLS，支持客户端证书认证
- Broker 和 Client 都需要兼容 `mqtt`、`mqtts`、`ws`、`wss` 接入方式

非本期目标：

- 集群
- Web 管理后台
- 规则引擎
- 分布式一致性
- 多存储后端一起首发

## 2. 总体设计原则

### 2.1 协议策略

- MQTT 5.0 为唯一主协议模型
- MQTT 3.1.1 仅作为兼容入口
- 内部统一使用一套包模型、会话模型、路由模型
- 3.1.1 差异点在接入边界适配，不在内部全局分叉

### 2.2 架构分层

建议使用以下项目拆分：

- `Wombat.Network.Mqtt.Abstractions`
- `Wombat.Network.Mqtt.Protocol`
- `Wombat.Network.Mqtt.Transport`
- `Wombat.Network.Mqtt.Client`
- `Wombat.Network.Mqtt.Broker`
- `Wombat.Network.Mqtt.Persistence.LiteDb`
- `Wombat.Network.Mqtt.Generators`
- `Wombat.Network.Mqtt.Tests`
- `Wombat.Network.Mqtt.Benchmarks`

其中：

- `Abstractions` 放公共契约，不放实现
- `Protocol` 只负责 MQTT 编解码和协议语义
- `Transport` 只负责 TCP/TLS/WebSocket/WSS 连接、管道、缓冲和背压
- `Client` 只负责客户端连接生命周期与高层 API
- `Broker` 只负责 Broker Core 和插件管线
- `Persistence.LiteDb` 单独隔离 LiteDB 依赖
- `Generators` 只做源码生成，不做业务承载

### 2.3 API 原则

- 默认暴露异步 API
- 优先使用 `ValueTask`
- 可持续消息流优先使用 `IAsyncEnumerable<T>`
- 生命周期统一支持 `IAsyncDisposable`
- 面向高频调用路径时优先使用 `ReadOnlyMemory<byte>`
- API 风格避免事件堆叠和隐藏线程模型
- 传输地址模型需要统一表达 `tcp`、`tls`、`ws`、`wss`

### 2.3.1 传输策略

- MQTT 协议层不区分 TCP 与 WebSocket 业务语义
- WebSocket 只作为 MQTT 字节流载体，不单独派生第二套协议模型
- `ws` 与 `wss` 通过独立 listener / connector 接入 `Transport`
- WebSocket 握手和路径匹配属于传输接入层，不侵入 Broker Core
- 若复用现有 `Wombat.Network` WebSocket channel，优先复用，不重复造一套

### 2.4 Broker 原则

Broker Core 只保留：

- 连接接入
- 协议握手
- Session 生命周期
- Topic Filter 匹配
- QoS1/QoS2 状态机
- Retain / Will / Inflight 调度
- 插件调用点

以下能力不直接耦合进 Core：

- 认证
- 授权
- 审计
- 指标
- 持久化实现
- 桥接
- 规则扩展

### 2.5 AOT 原则

- 不依赖运行时反射扫描
- 不依赖动态代理
- 不依赖字符串反射激活
- 可生成的映射表由源码生成器产出
- 插件注册采用显式注册优先，源码生成作为增强

## 3. 三轮实施计划

## 第一轮：协议内核与最小传输闭环

### 3.1 目标

建立 MQTT 的最小可运行内核，先打通协议与连接，不提前做 Broker 全家桶。

### 3.2 范围

- 建立项目结构与引用关系
- 建立 MQTT 5.0 基础包模型
- 实现基础编码器与解码器
- 打通基于 `Pipelines` 的读写链路
- 建立最小连接抽象
- 复用或适配现有 `Wombat.Network` 传输能力
- 建立 TCP/TLS 与 `ws/wss` 的统一 MQTT 连接入口抽象
- 建立最小 Client connect/ping/disconnect 流程

### 3.3 交付物

- 新解决方案或主解决方案中的 MQTT 项目组
- `Abstractions` 初始契约
- `Protocol` 中的基础包定义与编解码器
- `Transport` 中的最小 MQTT 连接封装
- 最小 WebSocket MQTT 接入封装
- 最小客户端 API 原型
- 基础单元测试与 roundtrip 自检
- 基础 benchmark 雏形

### 3.4 第一轮建议先支持的报文

- CONNECT
- CONNACK
- PUBLISH
- PUBACK
- SUBSCRIBE
- SUBACK
- PINGREQ
- PINGRESP
- DISCONNECT

先不在第一轮做全量覆盖，避免协议面铺太大。

### 3.5 验收标准

- 能完成 MQTT 5.0 基础连接握手
- 能完成最小发布与订阅闭环
- TCP 与 `ws` 两种接入方式都能完成基础握手
- 编解码 roundtrip 正确
- 热路径中不出现 `MemoryStream`
- 对外高频 API 不以 `byte[]` 作为主输入输出
- 有最小性能基线可供后续对比

### 3.6 风险与控制

- `netstandard2.0` 下部分现代实现细节可能需要兼容写法
- 如果直接在第一轮引入完整 Broker，会拖慢内核收敛
- 若协议模型先设计过重，后续会拖累 AOT 与 GC 目标

## 第二轮：Broker Core、Client API 与 Session 基础能力

### 4.1 目标

在第一轮内核稳定后，补齐真正可用的 Client 和最小 Broker Core。

### 4.2 范围

- 完成现代异步 Client API
- 完成 Broker Core
- 完成 Session 生命周期管理
- 完成 Topic Filter 匹配
- 完成 QoS1 基础闭环
- 建立 Retain / Will 的基础处理
- 建立日志与错误模型
- 暴露 Broker 的 TCP/TLS/WebSocket/WSS 监听配置

### 4.3 交付物

- `Client` 中可用的连接、发布、订阅 API
- `Broker` 中最小可运行 Broker Core
- 会话管理器
- Topic 路由与过滤器
- 基础 QoS1 状态流转
- `ILogger` 全链路接入
- 最小端到端集成测试
- WebSocket 接入集成测试

### 4.4 推荐 API 方向

Client 侧保持简洁：

- `ConnectAsync`
- `ConnectWebSocketAsync`
- `PublishAsync`
- `SubscribeAsync`
- `DisconnectAsync`

Broker 侧保持 builder 但不过度抽象：

- `ListenTcp`
- `ListenTls`
- `ListenWebSocket`
- `ListenWebSocketSecure`
- `UsePlugin`
- `UseSessionStore`

### 4.5 验收标准

- 多客户端可接入同一 Broker
- TCP/TLS 与 `ws/wss` 接入行为一致
- 订阅匹配正确
- Session 生命周期可追踪
- QoS1 报文往返正确
- Will 与 Retain 基础行为符合预期
- 日志能覆盖关键连接、认证、订阅、发布和异常路径

### 4.6 风险与控制

- Topic Filter 匹配和 QoS 状态机是易错点，需要先求正确再压性能
- 如果第二轮就塞入过多插件功能，Broker Core 边界会变糊
- 如果 Client API 做成事件中心，后续可维护性会迅速变差

### 4.7 当前落地状态（2026-06-23）

本轮已落地：

- `Client` 已补齐 `ConnectAsync`、`ConnectWebSocketAsync`、`PublishAsync`、`SubscribeAsync`、`DisconnectAsync`
- `Client` 已补充基础日志与 `MqttClientException`
- `Protocol` 已补充 Will Message 的 `CONNECT` 编解码
- `Broker` 已落地最小 `MqttBroker` Core
- 已落地内存 `SessionStore`
- 已落地 Topic Filter 匹配
- 已落地 QoS1 基础路由与 `PUBACK`
- 已落地 Retain 基础处理
- 已落地 Will 基础处理
- 已落地 `ListenTcp`、`ListenTls`、`ListenWebSocket`、`ListenWebSocketSecure`、`UsePlugin`、`UseSessionStore` 的配置面
- `MqttBroker` 已可按 `MqttBrokerOptions` 启动/停止真实 TCP listener
- `MqttBroker` 已可按 `MqttBrokerOptions` 启动/停止真实 WebSocket listener
- 已补充 MQTT 相关单元测试和最小 Broker 端到端行为测试
- 已补充 Broker 侧 TCP / WebSocket 端到端测试

本轮未落地：

- TLS listener 接入
- WSS listener 接入
- TLS / WSS 冒烟测试
- Broker 侧证书装配与 listener 证书配置模型

建议作为第二轮剩余收口项直接追加：

1. 在第三轮前半段补 broker 监听证书配置，把 `ListenTls` / `ListenWebSocketSecure` 接到真实 TLS/WSS listener
2. 补 TLS / WSS 至少一条冒烟测试
3. 若后续需要多网卡监听，再把当前 `IPAddress.Any` 固定策略升级成显式绑定地址配置

### 4.8 第二轮收尾结论（2026-06-23）

第二轮按“先收主路径、再延后证书基建”的方式完成收尾：

- MQTT 5 主路径上的 Client API、Broker Core、Session、Topic Filter、QoS1、Retain、Will 已完成并有测试覆盖
- Broker 已从“只能手动跑 `RunConnectionAsync`”收口到“可按 `MqttBrokerOptions` 启动/停止真实 TCP / WebSocket listener”
- TCP 与 WebSocket 两条 Broker 主接入路径已经补齐端到端验证，可作为第三轮继续扩展的稳定基线
- `ListenTls` / `ListenWebSocketSecure` 暂保留配置入口，但真实 listener 与证书装配延后到第三轮前半段处理

收尾判断：

- 第二轮核心目标已达成
- 第二轮未阻塞第三轮启动
- 第三轮应优先补 TLS/WSS 与证书配置，再继续插件化、持久化和兼容层

## 第三轮：插件化、持久化、兼容层与 AOT 收口

### 5.1 目标

把库从“能跑”推进到“可扩展、可落地、可发布”。

### 5.2 范围

- 建立 Broker 插件模型
- 接入 LiteDB Session 持久化
- 增加 MQTT 3.1.1 兼容层
- 加入源码生成器替换反射式注册
- 清理 NativeAOT 和 trimming 风险
- 补齐 QoS2、Session 恢复、Retain 恢复等高级能力
- 补齐 `wss` 场景下的证书、路径、代理兼容校验

### 5.3 交付物

- 插件契约与执行管线
- LiteDB 持久化实现
- MQTT 3.1.1 adapter
- 源码生成器首版
- AOT 兼容性清单
- 完整集成测试与回归测试
- `ws/wss` 回归测试清单

### 5.4 持久化边界

LiteDB 实现建议至少覆盖：

- Session 元数据
- 订阅关系
- Inflight 消息状态
- Retained 消息
- Will 元数据

但 LiteDB 只作为一个实现，不上升为核心模型依赖。

### 5.5 验收标准

- Broker 重启后可恢复持久 Session
- MQTT 3.1.1 客户端可接入并完成基本收发
- 主流 MQTT over WebSocket 场景可完成基本收发
- 插件无需运行时反射扫描即可注册
- trimming / AOT 检查无关键阻塞项
- QoS2 基础路径正确

### 5.6 风险与控制

- LiteDB 持久化模型若和内存模型强耦合，后续替换成本会很高
- 3.1.1 兼容若侵入主协议模型，会污染 MQTT 5 主路径
- 如果 WebSocket 握手细节侵入协议层，会让传输边界失控
- 源码生成器若做太多，会增加维护成本和调试难度

## 4. 跨轮约束

以下约束三轮都要持续执行：

- 不在热路径使用 `MemoryStream`
- 不在插件发现中使用运行时反射扫描
- 不把所有 Broker 能力揉进一个核心类
- 不把 WebSocket 握手、路径规则和 MQTT 协议状态机写在同一层
- 不为了“灵活性”提前引入多余抽象
- 每轮都保留最小可运行测试
- 每轮都补一个最小 benchmark，持续观察分配与吞吐

## 5. 当前建议的实施顺序

建议按以下顺序推进：

1. 先建文档和项目骨架
2. 先做协议内核，不先做全功能 Broker
3. 先做 MQTT 5 主路径，再补 3.1.1 兼容
4. 先把 TCP/TLS 和 `ws/wss` 都纳入统一传输抽象，不做两套 Client/Broker
5. 先做显式插件注册，再用源码生成器消掉样板代码
6. 先把 LiteDB 作为单独项目接入，不把持久化耦合到 Core

## 6. 需要审查确认的点

在进入实现前，建议先确认以下决策：

1. 是否接受按多项目结构拆分，而不是单项目承载所有 MQTT 代码
2. 是否接受 MQTT 3.1.1 只做兼容入口，不作为内部主模型
3. 是否接受第一轮只覆盖最小报文集，不做全协议一次性交付
4. 是否接受 `ws/wss` 作为第一轮就纳入的标准传输入口，而不是后补功能
5. 是否接受 LiteDB 作为首个持久化实现，但不把持久化接口绑死到 LiteDB
6. 是否接受先显式注册插件，再在第三轮加入源码生成器增强

如果以上五点都确认，下一步就可以直接进入项目骨架搭建。
