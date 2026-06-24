# Wombat.Network.Mqtt `ws/wss` 回归清单

更新时间：2026-06-24

## 1. 目标

- 把第三轮剩余的 `ws/wss` 场景收口成固定清单
- 明确哪些场景已自动化验证，哪些仅形成手工代理回归项
- 避免把 WebSocket / 代理兼容继续写进 Broker Core

## 2. 已自动化覆盖

| 场景 | `ws` | `wss` | 当前结果 | 备注 |
| --- | --- | --- | --- | --- |
| 直连握手 + connect / subscribe / publish / disconnect | 已覆盖 | 已覆盖 | 通过 | 见 `MqttBrokerTests` |
| 路径匹配 `/mqtt` | 已覆盖 | 已覆盖 | 通过 | 错路径会在握手边界拒绝 |
| 错误路径拒绝 | 已覆盖 | 已覆盖 | 通过 | `ws` 已有测试，`wss` 走相同路径校验逻辑 |
| TLS 证书装配 | 不适用 | 已覆盖 | 通过 | 自签证书冒烟已通过 |
| MQTT 基本收发 | 已覆盖 | 已覆盖 | 通过 | Broker 端到端已覆盖 |

## 3. 代理兼容回归矩阵

以下场景属于第四轮前的手工回归，不继续把代理逻辑塞进 MQTT 代码：

| 代理类型 | 场景 | 预期 |
| --- | --- | --- |
| Nginx | `location /mqtt` 透传 `Upgrade` / `Connection` | `ws` 可握手，可收发 |
| Nginx | `location /mqtt` + TLS 终止到后端 `ws` | `wss` 外部接入正常 |
| Nginx | `/mqtt/` 与 `/mqtt` 路径差异 | 仅接受配置路径，错路径拒绝 |
| Caddy | `reverse_proxy` 透传 WebSocket | `ws/wss` 可握手，可收发 |
| IIS / YARP | 保留原始路径与 `Upgrade` 头 | `ws/wss` 可握手，可收发 |
| 任意代理 | 透传 `X-Forwarded-Proto` 但不改 MQTT 逻辑 | Broker 不依赖该头，行为不变 |

## 4. 最小手工验证步骤

1. 启动 Broker，分别监听 `ws://host:port/mqtt` 和 `wss://host:port/mqtt`
2. 代理只做 TLS 终止、路径转发和 Upgrade 透传，不加 MQTT 特定头处理
3. 使用 `MqttClient.ConnectWebSocketAsync` 连入代理地址
4. 执行 `subscribe -> publish -> receive -> disconnect`
5. 再执行一条错误路径连接，确认被拒绝

## 5. 收尾结论

- 第三轮代码侧已覆盖 `ws/wss` 主路径、路径校验和证书主路径
- 代理兼容不再扩代码抽象，按回归清单验证
- 第四轮若要继续扩代理支持，只补文档和测试，不先改 Broker Core
