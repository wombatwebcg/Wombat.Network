using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Sockets;
using Wombat.Network.WebSockets;

namespace Wombat.Network.Benchmark.Utilities
{
    /// <summary>
    /// TCP客户端基准测试事件分发器
    /// </summary>
    public class BenchmarkTcpClientEventDispatcher : ITcpSocketClientEventDispatcher
    {
        private int _receivedMessages;
        private long _receivedBytes;

        public int ReceivedMessages => _receivedMessages;
        public long ReceivedBytes => _receivedBytes;

        public void ResetCounters()
        {
            _receivedMessages = 0;
            _receivedBytes = 0;
        }

        public void Reset()
        {
            ResetCounters();
        }

        public async Task OnServerConnected(TcpSocketClient client)
        {
            await Task.CompletedTask;
        }

        public async Task OnServerDataReceived(TcpSocketClient client, byte[] data, int offset, int count)
        {
            Interlocked.Increment(ref _receivedMessages);
            Interlocked.Add(ref _receivedBytes, count);
            await Task.CompletedTask;
        }

        public async Task OnServerDisconnected(TcpSocketClient client)
        {
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// TCP服务器基准测试事件分发器
    /// </summary>
    public class BenchmarkTcpServerEventDispatcher : ITcpSocketServerEventDispatcher
    {
        private int _receivedMessages;
        private long _receivedBytes;

        public int ReceivedMessages => _receivedMessages;
        public long ReceivedBytes => _receivedBytes;

        public void ResetCounters()
        {
            _receivedMessages = 0;
            _receivedBytes = 0;
        }

        public void Reset()
        {
            ResetCounters();
        }

        public async Task OnSessionStarted(TcpSocketSession session)
        {
            await Task.CompletedTask;
        }

        public async Task OnSessionDataReceived(TcpSocketSession session, byte[] data, int offset, int count)
        {
            Interlocked.Increment(ref _receivedMessages);
            Interlocked.Add(ref _receivedBytes, count);
            
            // 回显数据（用于RTT测试）
            await session.SendAsync(data, offset, count);
        }

        public async Task OnSessionClosed(TcpSocketSession session)
        {
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// UDP客户端基准测试事件分发器
    /// </summary>
    public class BenchmarkUdpClientEventDispatcher : IUdpSocketClientEventDispatcher
    {
        private int _receivedMessages;
        private long _receivedBytes;

        public int ReceivedMessages => _receivedMessages;
        public long ReceivedBytes => _receivedBytes;

        public void ResetCounters()
        {
            _receivedMessages = 0;
            _receivedBytes = 0;
        }

        public async Task OnServerConnected(UdpSocketClient client)
        {
            await Task.CompletedTask;
        }

        public async Task OnServerDataReceived(UdpSocketClient client, byte[] data, int offset, int count, IPEndPoint remoteEndPoint)
        {
            Interlocked.Increment(ref _receivedMessages);
            Interlocked.Add(ref _receivedBytes, count);
            await Task.CompletedTask;
        }

        public async Task OnServerDisconnected(UdpSocketClient client)
        {
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// UDP服务器基准测试事件分发器
    /// </summary>
    public class BenchmarkUdpServerEventDispatcher : IUdpSocketServerEventDispatcher
    {
        private int _receivedMessages;
        private long _receivedBytes;

        public int ReceivedMessages => _receivedMessages;
        public long ReceivedBytes => _receivedBytes;

        public void ResetCounters()
        {
            _receivedMessages = 0;
            _receivedBytes = 0;
        }

        public async Task OnSessionStarted(UdpSocketSession session)
        {
            await Task.CompletedTask;
        }

        public async Task OnSessionDataReceived(UdpSocketSession session, byte[] data, int offset, int count)
        {
            Interlocked.Increment(ref _receivedMessages);
            Interlocked.Add(ref _receivedBytes, count);
            
            // 回显数据（用于RTT测试）
            await session.SendAsync(data, offset, count);
        }

        public async Task OnSessionClosed(UdpSocketSession session)
        {
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// WebSocket客户端基准测试事件分发器
    /// </summary>
    public class BenchmarkWebSocketClientDispatcher : IWebSocketClientMessageDispatcher
    {
        private int _receivedMessages;
        private long _receivedBytes;

        public int ReceivedMessages => _receivedMessages;
        public long ReceivedBytes => _receivedBytes;

        public void ResetCounters()
        {
            _receivedMessages = 0;
            _receivedBytes = 0;
        }

        public async Task OnServerConnected(WebSocketClient client)
        {
            await Task.CompletedTask;
        }

        public async Task OnServerTextReceived(WebSocketClient client, string text)
        {
            Interlocked.Increment(ref _receivedMessages);
            Interlocked.Add(ref _receivedBytes, System.Text.Encoding.UTF8.GetByteCount(text));
            await Task.CompletedTask;
        }

        public async Task OnServerBinaryReceived(WebSocketClient client, byte[] data, int offset, int count)
        {
            Interlocked.Increment(ref _receivedMessages);
            Interlocked.Add(ref _receivedBytes, count);
            await Task.CompletedTask;
        }

        public async Task OnServerDisconnected(WebSocketClient client)
        {
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
            Interlocked.Increment(ref _receivedMessages);
            Interlocked.Add(ref _receivedBytes, count);
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// WebSocket服务器基准测试模块目录
    /// </summary>
    public class BenchmarkWebSocketServerModuleCatalog : AsyncWebSocketServerModuleCatalog
    {
        private int _receivedMessages;
        private long _receivedBytes;

        public int ReceivedMessages => _receivedMessages;
        public long ReceivedBytes => _receivedBytes;

        public void ResetCounters()
        {
            _receivedMessages = 0;
            _receivedBytes = 0;
        }

        public BenchmarkWebSocketServerModuleCatalog()
        {
            RegisterModule(new BenchmarkWebSocketServerModule(this));
        }

        internal void IncrementMessageCount()
        {
            Interlocked.Increment(ref _receivedMessages);
        }

        internal void AddBytes(long bytes)
        {
            Interlocked.Add(ref _receivedBytes, bytes);
        }
    }

    /// <summary>
    /// WebSocket服务器基准测试模块
    /// </summary>
    public class BenchmarkWebSocketServerModule : AsyncWebSocketServerModule
    {
        private readonly BenchmarkWebSocketServerModuleCatalog _catalog;

        public BenchmarkWebSocketServerModule(BenchmarkWebSocketServerModuleCatalog catalog)
        {
            _catalog = catalog;
        }

        public override async Task OnSessionStarted(WebSocketSession session)
        {
            await Task.CompletedTask;
        }

        public override async Task OnSessionTextReceived(WebSocketSession session, string text)
        {
            _catalog.IncrementMessageCount();
            _catalog.AddBytes(System.Text.Encoding.UTF8.GetByteCount(text));
            
            // 回显文本消息（用于RTT测试）
            await session.SendTextAsync(text);
        }

        public override async Task OnSessionBinaryReceived(WebSocketSession session, byte[] data, int offset, int count)
        {
            _catalog.IncrementMessageCount();
            _catalog.AddBytes(count);
            
            // 回显二进制消息（用于RTT测试）
            await session.SendBinaryAsync(data, offset, count);
        }

        public override async Task OnSessionClosed(WebSocketSession session)
        {
            await Task.CompletedTask;
        }

        public override async Task OnSessionFragmentationStreamOpened(WebSocketSession session, byte[] data, int offset, int count)
        {
            await Task.CompletedTask;
        }

        public override async Task OnSessionFragmentationStreamContinued(WebSocketSession session, byte[] data, int offset, int count)
        {
            await Task.CompletedTask;
        }

        public override async Task OnSessionFragmentationStreamClosed(WebSocketSession session, byte[] data, int offset, int count)
        {
            _catalog.IncrementMessageCount();
            _catalog.AddBytes(count);
            await Task.CompletedTask;
        }
    }
} 