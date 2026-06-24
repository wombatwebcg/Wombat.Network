# Wombat.Network.Mqtt AOT / Trimming 清单

更新时间：2026-06-24

## 1. 当前验证方式

- 新增 `Wombat.Network.Mqtt.AotSmoke`，位置：`Wombat.Network.Mqtt\Wombat.Network.Mqtt.AotSmoke`
- 验证命令：
  - `dotnet publish Wombat.Network.Mqtt\\Wombat.Network.Mqtt.AotSmoke\\Wombat.Network.Mqtt.AotSmoke.csproj -c Release -r win-x64`
- 运行结果：
  - NativeAOT publish 成功
  - 产物启动成功
  - 运行输出：`MqttClient:MqttBroker:MqttPublishPacket`

## 2. 当前结论

- `Abstractions`、`Protocol`、`Transport`、`Client`、`Broker` 当前主路径没有暴露新的 AOT 阻塞错误
- `Generators` 是编译期组件，不构成运行时 AOT 阻塞项
- 当前最大的 AOT / trimming 风险不在 MQTT 主路径，而在 `LiteDB`

## 3. 当前告警

- `LiteDB.dll` 产生 `IL2104`
  - 结论：LiteDB 自身存在 trimming warning
- `LiteDB.dll` 产生 `IL3053`
  - 结论：LiteDB 自身存在 AOT analysis warning
- `System.Linq.Expressions.dll` 产生 `IL3053`
  - 结论：AOT 发布链路包含表达式树相关分析警告，当前根因仍主要来自 LiteDB 依赖链

## 4. 已确认低风险点

- MQTT 插件注册当前不依赖运行时扫描
- MQTT 主协议包模型和编解码不依赖反射
- Broker / Client 主路径未引入动态代理或字符串反射激活
- TLS / WSS 主接入路径可通过 NativeAOT publish 编译

## 5. 待处理项

- 评估 `LiteDbMqttSessionStore` 是否继续作为默认 AOT 可用持久化方案
- 若要求“无 AOT warning”，需要把 LiteDB 从 AOT 主路径隔离
- 在 Linux 主机或容器内补一轮 `linux-x64` publish 验证，当前 Windows 主机无法跨 OS 直接做 NativeAOT
- 若后续引入真实生成器注册表，再复跑一次 smoke publish

## 5.1 `linux-x64` 验证结果（2026-06-24）

- 已执行命令：
  - `dotnet publish Wombat.Network.Mqtt\\Wombat.Network.Mqtt.AotSmoke\\Wombat.Network.Mqtt.AotSmoke.csproj -c Release -r linux-x64`
- 当前结果：
  - 项目 restore / build 成功
  - NativeAOT 阶段失败
- 失败原因：
  - `Cross-OS native compilation is not supported.`
- 结论：
  - 当前阻塞点是执行环境，不是 MQTT 代码路径新增 AOT 错误
  - 这轮验证需要切到 Linux 主机、WSL Linux 发行版，或 Linux 容器内执行

## 6. 建议收口策略

1. 保持 `Broker` / `Client` / `Protocol` 作为 AOT 主路径
2. 把 `Persistence.LiteDb` 标记为“功能可用，但非零 warning 依赖”
3. 如果第三轮要达成“无关键阻塞项”而不是“零 warning”，当前可以判定主路径已可发布
4. 如果目标升级为“零 trimming / AOT warning”，下一步先替换或隔离 LiteDB

## 7. 第三轮定案

- `LiteDbMqttSessionStore` 保留为可选持久化实现，不进入 NativeAOT 主发布路径承诺
- MQTT 主包的 AOT 结论以 `Abstractions`、`Protocol`、`Transport`、`Client`、`Broker`、`AotSmoke` 为准
- 若发布目标要求零 AOT / trimming warning，直接禁用 LiteDB 集成包，不再尝试在第三轮内消灭其上游告警
- 当前将 LiteDB 定位为：
  - 可用于常规 .NET 运行时部署
  - 可用于本地或服务器进程的持久化验证
  - 不作为 NativeAOT 零告警承诺的一部分
