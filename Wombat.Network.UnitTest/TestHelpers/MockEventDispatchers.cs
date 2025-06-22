using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Wombat.Network.Sockets;
using Wombat.Network.WebSockets;

namespace Wombat.Network.UnitTest.TestHelpers
{
    /// <summary>
    /// Mock TCP客户端事件分发器
    /// </summary>
    public class MockTcpClientEventDispatcher : ITcpSocketClientEventDispatcher
    {
        public List<(byte[] data, int offset, int count)> ReceivedData { get; } = new();
        public int ConnectedCount { get; private set; }
        public int DisconnectedCount { get; private set; }
        public Exception? LastException { get; private set; }

        public async Task OnServerConnected(TcpSocketClient client)
        {
            ConnectedCount++;
            await Task.CompletedTask;
        }

        public async Task OnServerDataReceived(TcpSocketClient client, byte[] data, int offset, int count)
        {
            var buffer = new byte[count];
            Buffer.BlockCopy(data, offset, buffer, 0, count);
            ReceivedData.Add((buffer, 0, count));
            await Task.CompletedTask;
        }

        public async Task OnServerDisconnected(TcpSocketClient client)
        {
            DisconnectedCount++;
            await Task.CompletedTask;
        }

        public void Reset()
        {
            ReceivedData.Clear();
            ConnectedCount = 0;
            DisconnectedCount = 0;
            LastException = null;
        }

        public void SimulateException(Exception exception)
        {
            LastException = exception;
        }
    }

    /// <summary>
    /// Mock TCP服务器事件分发器
    /// </summary>
    public class MockTcpServerEventDispatcher : ITcpSocketServerEventDispatcher
    {
        public List<(string sessionKey, byte[] data, int offset, int count)> ReceivedData { get; } = new();
        public List<string> StartedSessions { get; } = new();
        public List<string> ClosedSessions { get; } = new();

        public async Task OnSessionDataReceived(TcpSocketSession session, byte[] data, int offset, int count)
        {
            var buffer = new byte[count];
            Buffer.BlockCopy(data, offset, buffer, 0, count);
            ReceivedData.Add((session.SessionKey, buffer, 0, count));
            await Task.CompletedTask;
        }

        public async Task OnSessionStarted(TcpSocketSession session)
        {
            StartedSessions.Add(session.SessionKey);
            await Task.CompletedTask;
        }

        public async Task OnSessionClosed(TcpSocketSession session)
        {
            ClosedSessions.Add(session.SessionKey);
            await Task.CompletedTask;
        }

        public void Reset()
        {
            ReceivedData.Clear();
            StartedSessions.Clear();
            ClosedSessions.Clear();
        }
    }

    /// <summary>
    /// Mock UDP客户端事件分发器
    /// </summary>
    public class MockUdpClientEventDispatcher : IUdpSocketClientEventDispatcher
    {
        public List<(byte[] data, int offset, int count, IPEndPoint endpoint)> ReceivedData { get; } = new();
        public int ConnectedCount { get; private set; }
        public int DisconnectedCount { get; private set; }

        public async Task OnServerConnected(UdpSocketClient client)
        {
            ConnectedCount++;
            await Task.CompletedTask;
        }

        public async Task OnServerDataReceived(UdpSocketClient client, byte[] data, int offset, int count, IPEndPoint remoteEndPoint)
        {
            var buffer = new byte[count];
            Buffer.BlockCopy(data, offset, buffer, 0, count);
            ReceivedData.Add((buffer, 0, count, remoteEndPoint));
            await Task.CompletedTask;
        }

        public async Task OnServerDisconnected(UdpSocketClient client)
        {
            DisconnectedCount++;
            await Task.CompletedTask;
        }

        public void Reset()
        {
            ReceivedData.Clear();
            ConnectedCount = 0;
            DisconnectedCount = 0;
        }
    }

    /// <summary>
    /// Mock UDP服务器事件分发器
    /// </summary>
    public class MockUdpServerEventDispatcher : IUdpSocketServerEventDispatcher
    {
        public List<(string sessionKey, byte[] data, int offset, int count)> ReceivedData { get; } = new();
        public List<string> StartedSessions { get; } = new();
        public List<string> ClosedSessions { get; } = new();

        public async Task OnSessionDataReceived(UdpSocketSession session, byte[] data, int offset, int count)
        {
            var buffer = new byte[count];
            Buffer.BlockCopy(data, offset, buffer, 0, count);
            ReceivedData.Add((session.SessionKey, buffer, 0, count));
            await Task.CompletedTask;
        }

        public async Task OnSessionStarted(UdpSocketSession session)
        {
            StartedSessions.Add(session.SessionKey);
            await Task.CompletedTask;
        }

        public async Task OnSessionClosed(UdpSocketSession session)
        {
            ClosedSessions.Add(session.SessionKey);
            await Task.CompletedTask;
        }

        public void Reset()
        {
            ReceivedData.Clear();
            StartedSessions.Clear();
            ClosedSessions.Clear();
        }
    }

    /// <summary>
    /// Mock WebSocket客户端事件分发器
    /// </summary>
    public class MockWebSocketClientEventDispatcher : IWebSocketClientMessageDispatcher
    {
        public List<string> ReceivedTextMessages { get; } = new();
        public List<byte[]> ReceivedBinaryMessages { get; } = new();
        public int ConnectedCount { get; private set; }
        public int DisconnectedCount { get; private set; }

        public async Task OnServerTextReceived(WebSocketClient client, string text)
        {
            ReceivedTextMessages.Add(text);
            await Task.CompletedTask;
        }

        public async Task OnServerBinaryReceived(WebSocketClient client, byte[] data, int offset, int count)
        {
            var buffer = new byte[count];
            Buffer.BlockCopy(data, offset, buffer, 0, count);
            ReceivedBinaryMessages.Add(buffer);
            await Task.CompletedTask;
        }

        public async Task OnServerConnected(WebSocketClient client)
        {
            ConnectedCount++;
            await Task.CompletedTask;
        }

        public async Task OnServerDisconnected(WebSocketClient client)
        {
            DisconnectedCount++;
            await Task.CompletedTask;
        }

        public async Task OnServerFragmentationStreamOpened(WebSocketClient client, byte[] data, int offset, int count)
        {
            await Task.CompletedTask;
        }

        public async Task OnServerFragmentationStreamContinued(WebSocketClient client, byte[] data, int offset, int count)
        {
            await Task.CompletedTask;
        }

        public async Task OnServerFragmentationStreamClosed(WebSocketClient client, byte[] data, int offset, int count)
        {
            await Task.CompletedTask;
        }

        public void Reset()
        {
            ReceivedTextMessages.Clear();
            ReceivedBinaryMessages.Clear();
            ConnectedCount = 0;
            DisconnectedCount = 0;
        }
    }
} 