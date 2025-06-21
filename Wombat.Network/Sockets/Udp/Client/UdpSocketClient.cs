using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network;
using Microsoft.Extensions.Logging;

namespace Wombat.Network.Sockets
{
    public class UdpSocketClient
    {
        #region Fields

        private ILogger _logger;
        private UdpClient _udpClient;
        private readonly IUdpSocketClientEventDispatcher _dispatcher;
        private readonly UdpSocketClientConfiguration _configuration;
        private readonly IPEndPoint _remoteEndPoint;
        private readonly IPEndPoint _localEndPoint;
        private ArraySegment<byte> _receiveBuffer = default(ArraySegment<byte>);

        private int _state;
        private const int _none = 0;
        private const int _connecting = 1;
        private const int _connected = 2;
        private const int _closed = 5;
        
        // 异步处理相关字段
        private CancellationTokenSource _processCts;
        private Task _processTask;

        // 数据缓冲区相关字段
        private readonly ConcurrentQueue<ReceivedDataItem> _dataQueue = new ConcurrentQueue<ReceivedDataItem>();
        private readonly AutoResetEvent _dataAvailableEvent = new AutoResetEvent(false);
        private readonly object _dataLock = new object();
        private long _totalBytesAvailable = 0;
        
        // 心跳相关字段
        private Timer _heartbeatTimer;
        private DateTime _lastReceivedHeartbeatTime;
        private int _missedHeartbeats;
        private readonly object _heartbeatLock = new object();

        #endregion

        #region Internal Types

        /// <summary>
        /// 接收数据项，包含数据和来源终结点信息
        /// </summary>
        private class ReceivedDataItem
        {
            public byte[] Data { get; set; }
            public IPEndPoint RemoteEndPoint { get; set; }
        }

        #endregion

        #region Constructors

        public UdpSocketClient(IPAddress remoteAddress, int remotePort, IUdpSocketClientEventDispatcher dispatcher, UdpSocketClientConfiguration configuration = null)
            : this(new IPEndPoint(remoteAddress, remotePort), dispatcher, configuration)
        {
        }

        public UdpSocketClient(IPAddress remoteAddress, int remotePort, IPAddress localAddress, int localPort, IUdpSocketClientEventDispatcher dispatcher, UdpSocketClientConfiguration configuration = null)
            : this(new IPEndPoint(remoteAddress, remotePort), new IPEndPoint(localAddress, localPort), dispatcher, configuration)
        {
        }

        public UdpSocketClient(IPEndPoint remoteEP, IUdpSocketClientEventDispatcher dispatcher, UdpSocketClientConfiguration configuration = null)
            : this(remoteEP, null, dispatcher, configuration)
        {
        }

        public UdpSocketClient(IPEndPoint remoteEP, IPEndPoint localEP, IUdpSocketClientEventDispatcher dispatcher, UdpSocketClientConfiguration configuration = null)
        {
            if (remoteEP == null)
                throw new ArgumentNullException("remoteEP");
            if (dispatcher == null)
                throw new ArgumentNullException("dispatcher");

            _remoteEndPoint = remoteEP;
            _localEndPoint = localEP;
            _dispatcher = dispatcher;
            _configuration = configuration ?? new UdpSocketClientConfiguration();

            if (_configuration.BufferManager == null)
                throw new InvalidProgramException("The buffer manager in configuration cannot be null.");
            if (_configuration.FrameBuilder == null)
                throw new InvalidProgramException("The frame handler in configuration cannot be null.");
        }

        // 使用委托的构造函数重载
        public UdpSocketClient(IPAddress remoteAddress, int remotePort,
            Func<UdpSocketClient, byte[], int, int, IPEndPoint, Task> onServerDataReceived = null,
            Func<UdpSocketClient, Task> onServerConnected = null,
            Func<UdpSocketClient, Task> onServerDisconnected = null,
            UdpSocketClientConfiguration configuration = null)
            : this(new IPEndPoint(remoteAddress, remotePort),
                  onServerDataReceived, onServerConnected, onServerDisconnected, configuration)
        {
        }

        public UdpSocketClient(IPEndPoint remoteEP,
            Func<UdpSocketClient, byte[], int, int, IPEndPoint, Task> onServerDataReceived = null,
            Func<UdpSocketClient, Task> onServerConnected = null,
            Func<UdpSocketClient, Task> onServerDisconnected = null,
            UdpSocketClientConfiguration configuration = null)
            : this(remoteEP, null,
                  onServerDataReceived, onServerConnected, onServerDisconnected, configuration)
        {
        }

        public UdpSocketClient(IPEndPoint remoteEP, IPEndPoint localEP,
            Func<UdpSocketClient, byte[], int, int, IPEndPoint, Task> onServerDataReceived = null,
            Func<UdpSocketClient, Task> onServerConnected = null,
            Func<UdpSocketClient, Task> onServerDisconnected = null,
            UdpSocketClientConfiguration configuration = null)
            : this(remoteEP, localEP,
                 new DefaultUdpSocketClientEventDispatcher(onServerDataReceived, onServerConnected, onServerDisconnected),
                 configuration)
        {
        }

        #endregion

        #region Properties

        public UdpSocketClientConfiguration UdpSocketClientConfiguration { get { return _configuration; } }

        /// <summary>
        /// 指示是否已连接（UDP模拟连接状态）
        /// </summary>
        public bool Connected
        {
            get
            {
                return _udpClient != null && _state == _connected;
            }
        }

        public IPEndPoint RemoteEndPoint { get { return _remoteEndPoint; } }
        public IPEndPoint LocalEndPoint 
        { 
            get 
            { 
                if (_udpClient != null && _udpClient.Client != null)
                {
                    try
                    {
                        return (IPEndPoint)_udpClient.Client.LocalEndPoint;
                    }
                    catch
                    {
                        return _localEndPoint;
                    }
                }
                return _localEndPoint; 
            } 
        }

        /// <summary>
        /// 指示接收缓冲区是否已满
        /// </summary>
        public bool IsBufferFull
        {
            get
            {
                lock (_dataLock)
                {
                    return _totalBytesAvailable >= _configuration.MaxReceiveBufferSize;
                }
            }
        }

        /// <summary>
        /// 获取当前缓冲区使用率（百分比）
        /// </summary>
        public double BufferUsage
        {
            get
            {
                lock (_dataLock)
                {
                    if (_configuration.MaxReceiveBufferSize <= 0)
                        return 0;
                    
                    return (double)_totalBytesAvailable / _configuration.MaxReceiveBufferSize * 100.0;
                }
            }
        }

        /// <summary>
        /// UDP套接字状态
        /// </summary>
        public UdpSocketConnectionState State
        {
            get
            {
                switch (_state)
                {
                    case _none:
                        return UdpSocketConnectionState.None;
                    case _connecting:
                        return UdpSocketConnectionState.Starting;
                    case _connected:
                        return UdpSocketConnectionState.Active;
                    case _closed:
                        return UdpSocketConnectionState.Closed;
                    default:
                        return UdpSocketConnectionState.Closed;
                }
            }
        }

        public override string ToString()
        {
            return string.Format("RemoteEndPoint[{0}], LocalEndPoint[{1}]",
                this.RemoteEndPoint, this.LocalEndPoint);
        }

        #endregion

        #region Connect

        public async Task Connect(CancellationToken cancellationToken = default)
        {
            int origin = Interlocked.Exchange(ref _state, _connecting);
            if (!(origin == _none || origin == _closed))
            {
                await Close(false);
                throw new InvalidOperationException("This udp socket client is in invalid state when connecting.");
            }

            Clean(); // force to clean

            try
            {
                // 创建UDP客户端
                if (_localEndPoint != null)
                {
                    _udpClient = new UdpClient(_localEndPoint);
                }
                else
                {
                    _udpClient = new UdpClient(_remoteEndPoint.AddressFamily);
                }

                SetSocketOptions();

                // 在连接模式下，将UDP客户端绑定到远程终结点
                if (_configuration.ConnectedMode)
                {
                    _udpClient.Connect(_remoteEndPoint);
                }

                if (_receiveBuffer == default(ArraySegment<byte>))
                    _receiveBuffer = _configuration.BufferManager.BorrowBuffer();

                if (Interlocked.CompareExchange(ref _state, _connected, _connecting) != _connecting)
                {
                    await Close(false);
                    throw new InvalidOperationException("This udp socket client is in invalid state when connected.");
                }

                _logger?.LogDebug("Connected to server [{0}] with dispatcher [{1}] on [{2}].",
                    this.RemoteEndPoint,
                    _dispatcher.GetType().Name,
                    DateTime.UtcNow.ToString(@"yyyy-MM-dd HH:mm:ss.fffffff"));

                bool isErrorOccurredInUserSide = false;
                try
                {
                    await _dispatcher.OnServerConnected(this);
                }
                catch (Exception ex)
                {
                    isErrorOccurredInUserSide = true;
                    await HandleUserSideError(ex);
                }

                if (!isErrorOccurredInUserSide)
                {
                    // 启动处理任务
                    _processCts = new CancellationTokenSource();
                    _processTask = ProcessAsync(_processCts.Token);
                    
                    // 启动心跳机制
                    if (_configuration.EnableHeartbeat)
                    {
                        StartHeartbeat();
                    }
                }
                else
                {
                    await Close(true);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await Close(false);
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex.Message, ex);
                await Close(true);
                throw;
            }
        }

        private async Task ProcessAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (State == UdpSocketConnectionState.Active && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        UdpReceiveResult result;
                        
                        // 使用带超时的接收
                        using (var timeoutCts = new CancellationTokenSource(_configuration.ReceiveTimeout))
                        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken))
                        {
                            var receiveTask = _udpClient.ReceiveAsync();
                            
                            // 添加超时处理
                            if (await Task.WhenAny(receiveTask, Task.Delay(_configuration.ReceiveTimeout, linkedCts.Token)) != receiveTask)
                            {
                                if (!cancellationToken.IsCancellationRequested)
                                {
                                    // 接收超时，继续等待
                                    continue;
                                }
                                else
                                {
                                    // 外部请求取消
                                    break;
                                }
                            }
                            
                            result = await receiveTask;
                        }

                        // 在连接模式下，检查数据来源
                        if (_configuration.ConnectedMode && !result.RemoteEndPoint.Equals(_remoteEndPoint))
                        {
                            // 忽略来自非目标终结点的数据
                            continue;
                        }

                        // 处理接收到的数据
                        await ProcessReceivedData(result.Buffer, 0, result.Buffer.Length, result.RemoteEndPoint, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // 正常取消，退出循环
                        break;
                    }
                    catch (Exception ex)
                    {
                        await HandleReceiveOperationException(ex);
                        break;
                    }
                }
            }
            finally
            {
                await Close(true);
            }
        }

        private async Task ProcessReceivedData(byte[] data, int offset, int count, IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            try
            {
                // 尝试解码帧
                byte[] payload;
                int payloadOffset;
                int payloadCount;
                int frameLength;

                if (_configuration.FrameBuilder.Decoder.TryDecodeFrame(
                    data, offset, count,
                    out frameLength, out payload, out payloadOffset, out payloadCount))
                {
                    // 检查是否为心跳包
                    if (HeartbeatManager.IsHeartbeatPacket(payload, payloadOffset, payloadCount))
                    {
                        ProcessHeartbeatPacket(payload, payloadOffset);
                    }
                    else
                    {
                        // 将接收到的数据复制到队列中
                        if (payload != null && payloadCount > 0)
                        {
                            byte[] dataCopy = new byte[payloadCount];
                            Buffer.BlockCopy(payload, payloadOffset, dataCopy, 0, payloadCount);
                            
                            lock (_dataLock)
                            {
                                if (_totalBytesAvailable + dataCopy.Length <= _configuration.MaxReceiveBufferSize)
                                {
                                    _dataQueue.Enqueue(new ReceivedDataItem 
                                    { 
                                        Data = dataCopy, 
                                        RemoteEndPoint = remoteEndPoint 
                                    });
                                    _totalBytesAvailable += dataCopy.Length;
                                    _dataAvailableEvent.Set();
                                }
                            }
                        }
                        
                        // 使用操作超时处理事件分发
                        using (var dispatchCts = new CancellationTokenSource(_configuration.OperationTimeout))
                        using (var dispatchLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(dispatchCts.Token, cancellationToken))
                        {
                            var dispatchTask = _dispatcher.OnServerDataReceived(this, payload, payloadOffset, payloadCount, remoteEndPoint);
                            
                            if (await Task.WhenAny(dispatchTask, Task.Delay(_configuration.OperationTimeout, dispatchLinkedCts.Token)) != dispatchTask)
                            {
                                if (!cancellationToken.IsCancellationRequested)
                                {
                                    _logger?.LogWarning("Dispatch operation timed out after {0}ms", 
                                        _configuration.OperationTimeout.TotalMilliseconds);
                                }
                                else
                                {
                                    return;
                                }
                            }
                            else
                            {
                                await dispatchTask;
                            }
                        }
                    }
                }
                else
                {
                    // 无法解码的数据，记录警告
                    _logger?.LogWarning("Unable to decode received UDP datagram from {0}", remoteEndPoint);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                await HandleUserSideError(ex);
            }
        }

        private void SetSocketOptions()
        {
            if (_udpClient?.Client != null)
            {
                _udpClient.Client.ReceiveBufferSize = _configuration.ReceiveBufferSize;
                _udpClient.Client.SendBufferSize = _configuration.SendBufferSize;
                _udpClient.Client.ReceiveTimeout = (int)_configuration.ReceiveTimeout.TotalMilliseconds;
                _udpClient.Client.SendTimeout = (int)_configuration.SendTimeout.TotalMilliseconds;
                
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _configuration.ReuseAddress);
                
                if (_configuration.DontFragment)
                {
                    _udpClient.DontFragment = true;
                }
                
                if (_configuration.Broadcast)
                {
                    _udpClient.EnableBroadcast = true;
                }
            }
        }

        #endregion

        #region Logger
        public void UseLogger(ILogger logger)
        {
            _logger = logger;
        }

        #endregion

        #region Close

        public async Task Close()
        {
            await Close(true);
        }

        private async Task Close(bool shallNotifyUserSide)
        {
            if (Interlocked.Exchange(ref _state, _closed) == _closed)
            {
                return;
            }

            // 停止心跳检测
            StopHeartbeat();
            
            if (shallNotifyUserSide)
            {
                _logger?.LogDebug("Disconnected from server [{0}] with dispatcher [{1}] on [{2}].",
                    this.RemoteEndPoint,
                    _dispatcher.GetType().Name,
                    DateTime.UtcNow.ToString(@"yyyy-MM-dd HH:mm:ss.fffffff"));
                try
                {
                    await _dispatcher.OnServerDisconnected(this);
                }
                catch (Exception ex)
                {
                    await HandleUserSideError(ex);
                }
            }

            Clean();
        }

        private void Clean()
        {
            try
            {
                try
                {
                    _processCts?.Cancel();
                    _processCts?.Dispose();
                    _processCts = null;
                }
                catch { }
                
                try
                {
                    if (_udpClient != null)
                    {
                        _udpClient.Close();
                        _udpClient.Dispose();
                    }
                }
                catch { }
            }
            catch { }
            finally
            {
                _udpClient = null;
                
                // 清空接收缓冲区
                ClearReceiveBuffer();
            }

            if (_receiveBuffer != default(ArraySegment<byte>))
                _configuration.BufferManager.ReturnBuffer(_receiveBuffer);
            _receiveBuffer = default(ArraySegment<byte>);
        }

        #endregion

        #region Exception Handler

        private async Task HandleReceiveOperationException(Exception ex)
        {
            await CloseIfShould(ex);
            throw new UdpSocketException(ex.Message, ex);
        }

        private async Task HandleSendOperationException(Exception ex)
        {
            await CloseIfShould(ex);
            throw new UdpSocketException(ex.Message, ex);
        }

        private async Task<bool> CloseIfShould(Exception ex)
        {
            if (ex is ObjectDisposedException
                || ex is InvalidOperationException
                || ex is SocketException
                || ex is NullReferenceException
                || ex is ArgumentException)
            {
                _logger?.LogError(ex.Message, ex);
                await Close(false);
                return true;
            }

            return false;
        }

        private async Task HandleUserSideError(Exception ex)
        {
            _logger?.LogError(string.Format("Client [{0}] error occurred in user side [{1}].", this, ex.Message), ex);
            await Task.CompletedTask;
        }

        #endregion

        #region Send

        public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            await SendAsync(data, 0, data.Length, cancellationToken);
        }

        public async Task SendAsync(byte[] data, int offset, int count, CancellationToken cancellationToken = default)
        {
            BufferValidator.ValidateBuffer(data, offset, count, "data");

            if (State != UdpSocketConnectionState.Active)
            {
                throw new InvalidOperationException("This client is not active.");
            }

            CancellationTokenSource timeoutCts = null;
            CancellationTokenSource linkedCts = null;
            
            try
            {
                timeoutCts = new CancellationTokenSource(_configuration.SendTimeout);
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
                
                byte[] frameBuffer;
                int frameBufferOffset;
                int frameBufferLength;
                _configuration.FrameBuilder.Encoder.EncodeFrame(data, offset, count, out frameBuffer, out frameBufferOffset, out frameBufferLength);

                // 准备发送的数据
                byte[] sendData = new byte[frameBufferLength];
                Buffer.BlockCopy(frameBuffer, frameBufferOffset, sendData, 0, frameBufferLength);

                // 使用带超时的发送
                Task sendTask;
                if (_configuration.ConnectedMode)
                {
                    sendTask = _udpClient.SendAsync(sendData, sendData.Length);
                }
                else
                {
                    sendTask = _udpClient.SendAsync(sendData, sendData.Length, _remoteEndPoint);
                }
                
                if (await Task.WhenAny(sendTask, Task.Delay(_configuration.SendTimeout, linkedCts.Token)) != sendTask)
                {
                    if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException($"Send operation timed out after {_configuration.SendTimeout.TotalMilliseconds}ms");
                    }
                }
                
                await sendTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await HandleSendOperationException(ex);
            }
            finally
            {
                timeoutCts?.Dispose();
                linkedCts?.Dispose();
            }
        }

        #endregion

        #region Data Reading

        public int BytesAvailable
        {
            get
            {
                lock (_dataLock)
                {
                    return (int)Math.Min(_totalBytesAvailable, int.MaxValue);
                }
            }
        }

        public int ClearReceiveBuffer()
        {
            lock (_dataLock)
            {
                int released = (int)_totalBytesAvailable;
                while (_dataQueue.TryDequeue(out _)) { }
                _totalBytesAvailable = 0;
                return released;
            }
        }

        #endregion

        #region Heartbeat Management

        private void StartHeartbeat()
        {
            if (!_configuration.EnableHeartbeat || _state != _connected)
                return;

            lock (_heartbeatLock)
            {
                StopHeartbeat();
                
                _lastReceivedHeartbeatTime = DateTime.UtcNow;
                _missedHeartbeats = 0;
                
                _heartbeatTimer = new Timer(
                    HeartbeatTimerCallback, 
                    null, 
                    _configuration.HeartbeatInterval, 
                    _configuration.HeartbeatInterval);
                
                HeartbeatManager.LogHeartbeat(_logger, "启动UDP客户端心跳，间隔: {0}秒", _configuration.HeartbeatInterval.TotalSeconds);
            }
        }

        private void StopHeartbeat()
        {
            lock (_heartbeatLock)
            {
                if (_heartbeatTimer != null)
                {
                    _heartbeatTimer.Dispose();
                    _heartbeatTimer = null;
                    HeartbeatManager.LogHeartbeat(_logger, "停止UDP客户端心跳");
                }
            }
        }

        private async void HeartbeatTimerCallback(object state)
        {
            if (_state != _connected)
            {
                StopHeartbeat();
                return;
            }
            
            try
            {
                await SendHeartbeatPacket();
                CheckHeartbeatTimeout();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "UDP心跳处理中发生错误");
            }
        }

        private async Task SendHeartbeatPacket()
        {
            try
            {
                if (_state != _connected)
                    return;
                    
                byte[] heartbeatPacket = HeartbeatManager.CreateHeartbeatPacket();
                await SendAsync(heartbeatPacket);
                
                HeartbeatManager.LogHeartbeat(_logger, "UDP客户端发送心跳包到服务器: {0}", this.RemoteEndPoint);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "发送UDP心跳包时发生错误");
            }
        }

        private void ProcessHeartbeatPacket(byte[] data, int offset)
        {
            try
            {
                lock (_heartbeatLock)
                {
                    _lastReceivedHeartbeatTime = DateTime.UtcNow;
                    _missedHeartbeats = 0;
                }
                
                long timestamp = HeartbeatManager.ExtractTimestamp(data, offset);
                if (timestamp > 0)
                {
                    long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    long delay = currentTime - timestamp;
                    
                    HeartbeatManager.LogHeartbeat(_logger, "UDP客户端收到服务器心跳包: {0}, 网络延迟: {1}ms", 
                        this.RemoteEndPoint, delay);
                }
                else
                {
                    HeartbeatManager.LogHeartbeat(_logger, "UDP客户端收到服务器心跳包: {0}", this.RemoteEndPoint);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "处理UDP心跳包时发生错误");
            }
        }

        private void CheckHeartbeatTimeout()
        {
            if (!_configuration.EnableHeartbeat || _state != _connected)
                return;
                
            lock (_heartbeatLock)
            {
                TimeSpan elapsed = DateTime.UtcNow - _lastReceivedHeartbeatTime;
                
                if (elapsed > _configuration.HeartbeatTimeout)
                {
                    _missedHeartbeats++;
                    HeartbeatManager.LogHeartbeat(_logger, "UDP客户端心跳超时，已连续 {0}/{1} 次未收到服务器心跳", 
                        _missedHeartbeats, _configuration.MaxMissedHeartbeats);
                    
                    if (_missedHeartbeats >= _configuration.MaxMissedHeartbeats)
                    {
                        HeartbeatManager.LogHeartbeat(_logger, "UDP客户端心跳连续 {0} 次超时，关闭连接: {1}", 
                            _missedHeartbeats, this.RemoteEndPoint);
                            
                        Task.Run(async () => await Close(true));
                    }
                }
            }
        }

        #endregion
    }
} 