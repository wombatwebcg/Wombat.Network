# Wombat.Network API

当前公开模型只保留以下入口：

## Transport

- `Wombat.Network.Transports.Abstractions.ITransportConnection`
- `Wombat.Network.Transports.Abstractions.ITransportListener`
- `Wombat.Network.Transports.Tcp.TcpTransportConnection`
- `Wombat.Network.Transports.Tcp.TcpTransportListener`
- `Wombat.Network.Transports.Udp.IUdpTransport`
- `Wombat.Network.Transports.Udp.UdpDatagramTransport`
- `Wombat.Network.Transports.Tls.TlsTransportConnection`
- `Wombat.Network.Transports.Serial.SerialTransportConnection`

## Channel

- `Wombat.Network.Channels.IMessageChannel`
- `Wombat.Network.Channels.StreamMessageChannel`
- `Wombat.Network.Channels.DatagramMessageChannel`
- `Wombat.Network.Channels.WebSocketMessageChannel`
- `Wombat.Network.Channels.ReceivedMessage`

## Protocol

- `Wombat.Network.Protocols.IMessagePipe`
- `Wombat.Network.Protocols.Framing.LengthFieldMessagePipe`
- `Wombat.Network.Protocols.WebSocket.WebSocketHandshakeMiddleware`
- `Wombat.Network.Protocols.WebSocket.WebSocketFrameCodec`
- `Wombat.Network.Protocols.WebSocket.WebSocketReceivedMessage`
- `Wombat.Network.Protocols.WebSocket.WebSocketMessageType`

## Notes

- 旧 `TcpSocket*`、`UdpSocket*`、`WebSocketClient/Server/Session` 已删除。
- 旧 `PipelineSocketConnection` 已删除。
- 示例见 [README.md](/D:/Wombat/Wombat.Network/Wombat.Network/README.md)。
