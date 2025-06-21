using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network;
using Microsoft.Extensions.Logging;

namespace Wombat.Network.Sockets
{
    public class TcpSocketClient
    {
        #region Fields

        private  ILogger _logger;
        private TcpClient _tcpClient;
        private readonly ITcpSocketClientEventDispatcher _dispatcher;
        private readonly TcpSocketClientConfiguration _configuration;
        private readonly IPEndPoint _remoteEndPoint;
        private readonly IPEndPoint _localEndPoint;
        private Stream _stream;
        private ArraySegment<byte> _receiveBuffer = default(ArraySegment<byte>);
        private int _receiveBufferOffset = 0;

        private int _state;
        private const int _none = 0;
        private const int _connecting = 1;
        private const int _connected = 2;
        private const int _closed = 5;
        
        // 新增字段，用于异步处理
        private CancellationTokenSource _processCts;
        private Task _processTask;

        // 新增字段，用于存储接收到的数据
        private readonly ConcurrentQueue<byte[]> _dataQueue = new ConcurrentQueue<byte[]>();
        private readonly AutoResetEvent _dataAvailableEvent = new AutoResetEvent(false);
        private readonly object _dataLock = new object();
        private long _totalBytesAvailable = 0;

        #endregion

        #region Constructors

        public TcpSocketClient(IPAddress remoteAddress, int remotePort, IPAddress localAddress, int localPort, ITcpSocketClientEventDispatcher dispatcher, TcpSocketClientConfiguration configuration = null)
            : this(new IPEndPoint(remoteAddress, remotePort), new IPEndPoint(localAddress, localPort), dispatcher, configuration)
        {
        }

        public TcpSocketClient(IPAddress remoteAddress, int remotePort, IPEndPoint localEP, ITcpSocketClientEventDispatcher dispatcher, TcpSocketClientConfiguration configuration = null)
            : this(new IPEndPoint(remoteAddress, remotePort), localEP, dispatcher, configuration)
        {
        }

        public TcpSocketClient(IPAddress remoteAddress, int remotePort, ITcpSocketClientEventDispatcher dispatcher, TcpSocketClientConfiguration configuration = null)
            : this(new IPEndPoint(remoteAddress, remotePort), dispatcher, configuration)
        {
        }

        public TcpSocketClient(IPEndPoint remoteEP, ITcpSocketClientEventDispatcher dispatcher, TcpSocketClientConfiguration configuration = null)
            : this(remoteEP, null, dispatcher, configuration)
        {
        }

        public TcpSocketClient(IPEndPoint remoteEP, IPEndPoint localEP, ITcpSocketClientEventDispatcher dispatcher, TcpSocketClientConfiguration configuration = null)
        {
            if (remoteEP == null)
                throw new ArgumentNullException("remoteEP");
            if (dispatcher == null)
                throw new ArgumentNullException("dispatcher");

            _remoteEndPoint = remoteEP;
            _localEndPoint = localEP;
            _dispatcher = dispatcher;
            _configuration = configuration ?? new TcpSocketClientConfiguration();

            if (_configuration.BufferManager == null)
                throw new InvalidProgramException("The buffer manager in configuration cannot be null.");
            if (_configuration.FrameBuilder == null)
                throw new InvalidProgramException("The frame handler in configuration cannot be null.");
        }

        public TcpSocketClient(IPAddress remoteAddress, int remotePort, IPAddress localAddress, int localPort,
            Func<TcpSocketClient, byte[], int, int, Task> onServerDataReceived = null,
            Func<TcpSocketClient, Task> onServerConnected = null,
            Func<TcpSocketClient, Task> onServerDisconnected = null,
            TcpSocketClientConfiguration configuration = null)
            : this(new IPEndPoint(remoteAddress, remotePort), new IPEndPoint(localAddress, localPort),
                  onServerDataReceived, onServerConnected, onServerDisconnected, configuration)
        {
        }

        public TcpSocketClient(IPAddress remoteAddress, int remotePort, IPEndPoint localEP,
            Func<TcpSocketClient, byte[], int, int, Task> onServerDataReceived = null,
            Func<TcpSocketClient, Task> onServerConnected = null,
            Func<TcpSocketClient, Task> onServerDisconnected = null,
            TcpSocketClientConfiguration configuration = null)
            : this(new IPEndPoint(remoteAddress, remotePort), localEP,
                  onServerDataReceived, onServerConnected, onServerDisconnected, configuration)
        {
        }

        public TcpSocketClient(IPAddress remoteAddress, int remotePort,
            Func<TcpSocketClient, byte[], int, int, Task> onServerDataReceived = null,
            Func<TcpSocketClient, Task> onServerConnected = null,
            Func<TcpSocketClient, Task> onServerDisconnected = null,
            TcpSocketClientConfiguration configuration = null)
            : this(new IPEndPoint(remoteAddress, remotePort),
                  onServerDataReceived, onServerConnected, onServerDisconnected, configuration)
        {
        }

        public TcpSocketClient(IPEndPoint remoteEP,
            Func<TcpSocketClient, byte[], int, int, Task> onServerDataReceived = null,
            Func<TcpSocketClient, Task> onServerConnected = null,
            Func<TcpSocketClient, Task> onServerDisconnected = null,
            TcpSocketClientConfiguration configuration = null)
            : this(remoteEP, null,
                  onServerDataReceived, onServerConnected, onServerDisconnected, configuration)
        {
        }

        public TcpSocketClient(IPEndPoint remoteEP, IPEndPoint localEP,
            Func<TcpSocketClient, byte[], int, int, Task> onServerDataReceived = null,
            Func<TcpSocketClient, Task> onServerConnected = null,
            Func<TcpSocketClient, Task> onServerDisconnected = null,
            TcpSocketClientConfiguration configuration = null)
            : this(remoteEP, localEP,
                 new DefaultTcpSocketClientEventDispatcher(onServerDataReceived, onServerConnected, onServerDisconnected),
                 configuration)
        {
        }

        #endregion

        #region Properties


        public TcpSocketClientConfiguration TcpSocketClientConfiguration { get { return _configuration; } }

        public bool Connected
        {
            get
            {
                return _tcpClient != null && _tcpClient.Connected;

            }
        }
        public IPEndPoint RemoteEndPoint { get { return Connected ? (IPEndPoint)_tcpClient.Client.RemoteEndPoint : _remoteEndPoint; } }
        public IPEndPoint LocalEndPoint { get { return Connected ? (IPEndPoint)_tcpClient.Client.LocalEndPoint : _localEndPoint; } }

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

        public TcpSocketConnectionState State
        {
            get
            {
                switch (_state)
                {
                    case _none:
                        return TcpSocketConnectionState.None;
                    case _connecting:
                        return TcpSocketConnectionState.Connecting;
                    case _connected:
                        return TcpSocketConnectionState.Connected;
                    case _closed:
                        return TcpSocketConnectionState.Closed;
                    default:
                        return TcpSocketConnectionState.Closed;
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
                await Close(false); // connecting with wrong state
                throw new InvalidOperationException("This tcp socket client is in invalid state when connecting.");
            }

            Clean(); // force to clean

            CancellationTokenSource timeoutCts = null;
            CancellationTokenSource linkedCts = null;
            
            try
            {
                timeoutCts = new CancellationTokenSource(_configuration.ConnectTimeout);
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

                _tcpClient = _localEndPoint != null ?
                    new TcpClient(_localEndPoint) :
                    new TcpClient(_remoteEndPoint.Address.AddressFamily);
                SetSocketOptions();

                try
                {
                    // 使用真正的异步操作替换阻塞等待
                    var connectTask = _tcpClient.ConnectAsync(_remoteEndPoint.Address, _remoteEndPoint.Port);
                    
                    // 添加超时
                    if (await Task.WhenAny(connectTask, Task.Delay(_configuration.ConnectTimeout, cancellationToken)) != connectTask)
                    {
                        await Close(false); // connect timeout
                        throw new TimeoutException(string.Format(
                            "Connect to [{0}] timeout [{1}].", _remoteEndPoint, _configuration.ConnectTimeout));
                    }
                    
                    // 确保连接任务已完成
                    await connectTask;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await Close(false); // operation canceled
                    throw; // 重新抛出取消异常
                }

                // 使用异步方式协商流
                try
                {
                    var negotiateTask = NegotiateStreamAsync(_tcpClient.GetStream(), cancellationToken);
                    
                    // 添加超时
                    if (await Task.WhenAny(negotiateTask, Task.Delay(_configuration.ConnectTimeout, cancellationToken)) != negotiateTask)
                    {
                        await Close(false); // ssl negotiation timeout
                        throw new TimeoutException(string.Format(
                            "Negotiate SSL/TSL with remote [{0}] timeout [{1}].", _remoteEndPoint, _configuration.ConnectTimeout));
                    }
                    
                    // 获取协商结果
                    _stream = await negotiateTask;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await Close(false); // operation canceled
                    throw; // 重新抛出取消异常
                }

                if (_receiveBuffer == default(ArraySegment<byte>))
                    _receiveBuffer = _configuration.BufferManager.BorrowBuffer();
                _receiveBufferOffset = 0;

                if (Interlocked.CompareExchange(ref _state, _connected, _connecting) != _connecting)
                {
                    await Close(false); // connected with wrong state
                    throw new InvalidOperationException("This tcp socket client is in invalid state when connected.");
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
                catch (Exception ex) // catch all exceptions from out-side
                {
                    isErrorOccurredInUserSide = true;
                    await HandleUserSideError(ex);
                }

                if (!isErrorOccurredInUserSide)
                {
                    // 使用取消令牌启动处理任务
                    _processCts = new CancellationTokenSource();
                    _processTask = ProcessAsync(_processCts.Token);
                }
                else
                {
                    await Close(true); // user side handle tcp connection error occurred
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await Close(false); // operation canceled
                throw; // 重新抛出取消异常
            }
            catch (Exception ex) // catch exceptions then log then re-throw
            {
                _logger?.LogError(ex.Message, ex);
                await Close(true); // handle tcp connection error occurred
                throw;
            }
            finally
            {
                timeoutCts?.Dispose();
                linkedCts?.Dispose();
            }
        }
        
        private async Task ProcessAsync(CancellationToken cancellationToken)
        {
            try
            {
                int frameLength;
                byte[] payload;
                int payloadOffset;
                int payloadCount;
                int consumedLength = 0;

                while (State == TcpSocketConnectionState.Connected && !cancellationToken.IsCancellationRequested)
                {
                    // 使用带超时的读取
                    CancellationTokenSource timeoutCts = null;
                    CancellationTokenSource linkedCts = null;
                    
                    try
                    {
                        timeoutCts = new CancellationTokenSource(_configuration.ReceiveTimeout);
                        linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
                        
                        var receiveTask = _stream.ReadAsync(
                            _receiveBuffer.Array,
                            _receiveBuffer.Offset + _receiveBufferOffset,
                            _receiveBuffer.Count - _receiveBufferOffset);
                            
                        // 添加超时
                        if (await Task.WhenAny(receiveTask, Task.Delay(_configuration.ReceiveTimeout, linkedCts.Token)) != receiveTask)
                        {
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                // 读取超时，可以选择继续或关闭，这里选择继续
                                _logger?.LogWarning("Read operation timed out after {0}ms, continuing", 
                                    _configuration.ReceiveTimeout.TotalMilliseconds);
                                continue;
                            }
                            else
                            {
                                // 外部请求取消
                                break;
                            }
                        }
                        
                        // 获取接收结果
                        int receiveCount = await receiveTask;
                        
                        if (receiveCount == 0)
                            break;

                        SegmentBufferDeflector.ReplaceBuffer(_configuration.BufferManager, ref _receiveBuffer, ref _receiveBufferOffset, receiveCount);
                        consumedLength = 0;
                        
                        // 处理收到的数据
                        while (true)
                        {
                            frameLength = 0;
                            payload = null;
                            payloadOffset = 0;
                            payloadCount = 0;

                            if (_configuration.FrameBuilder.Decoder.TryDecodeFrame(
                                _receiveBuffer.Array,
                                _receiveBuffer.Offset + consumedLength,
                                _receiveBufferOffset - consumedLength,
                                out frameLength, out payload, out payloadOffset, out payloadCount))
                            {
                                try
                                {
                                    // 将接收到的数据复制到队列中
                                    if (payload != null && payloadCount > 0)
                                    {
                                        byte[] dataCopy = new byte[payloadCount];
                                        Buffer.BlockCopy(payload, payloadOffset, dataCopy, 0, payloadCount);
                                        
                                        lock (_dataLock)
                                        {
                                            // 检查缓冲区是否已满
                                            if (_totalBytesAvailable + dataCopy.Length <= _configuration.MaxReceiveBufferSize)
                                            {
                                                _dataQueue.Enqueue(dataCopy);
                                                _totalBytesAvailable += dataCopy.Length;
                                                _dataAvailableEvent.Set(); // 发出信号，表示有数据可用
                                            }

                                        }
                                    }
                                    
                                    // 使用操作超时
                                    using (var dispatchCts = new CancellationTokenSource(_configuration.OperationTimeout))
                                    using (var dispatchLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(dispatchCts.Token, cancellationToken))
                                    {
                                        var dispatchTask = _dispatcher.OnServerDataReceived(this, payload, payloadOffset, payloadCount);
                                        
                                        // 添加超时
                                        if (await Task.WhenAny(dispatchTask, Task.Delay(_configuration.OperationTimeout, dispatchLinkedCts.Token)) != dispatchTask)
                                        {
                                            if (!cancellationToken.IsCancellationRequested)
                                            {
                                                _logger?.LogWarning("Dispatch operation timed out after {0}ms", 
                                                    _configuration.OperationTimeout.TotalMilliseconds);
                                            }
                                            else
                                            {
                                                // 外部请求取消
                                                return;
                                            }
                                        }
                                        else
                                        {
                                            // 确保调度任务已完成
                                            await dispatchTask;
                                        }
                                    }
                                }
                                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                                {
                                    // 外部请求取消，直接退出
                                    return;
                                }
                                catch (Exception ex)
                                {
                                    await HandleUserSideError(ex);
                                }
                                finally
                                {
                                    consumedLength += frameLength;
                                }
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (_receiveBuffer != null && _receiveBuffer.Array != null)
                        {
                            SegmentBufferDeflector.ShiftBuffer(_configuration.BufferManager, consumedLength, ref _receiveBuffer, ref _receiveBufferOffset);
                        }
                    }
                    finally
                    {
                        timeoutCts?.Dispose();
                        linkedCts?.Dispose();
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Graceful exit
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 正常取消，不需要额外处理
            }
            catch (Exception ex)
            {
                await HandleReceiveOperationException(ex);
            }
            finally
            {
                await Close(true); // 关闭连接
            }
        }

        private void SetSocketOptions()
        {
            _tcpClient.ReceiveBufferSize = _configuration.ReceiveBufferSize;
            _tcpClient.SendBufferSize = _configuration.SendBufferSize;
            _tcpClient.ReceiveTimeout = (int)_configuration.ReceiveTimeout.TotalMilliseconds;
            _tcpClient.SendTimeout = (int)_configuration.SendTimeout.TotalMilliseconds;
            _tcpClient.NoDelay = _configuration.NoDelay;
            _tcpClient.LingerState = _configuration.LingerState;

            if (_configuration.KeepAlive)
            {
                _tcpClient.Client.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.KeepAlive,
                    (int)_configuration.KeepAliveInterval.TotalMilliseconds);
            }

            _tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _configuration.ReuseAddress);
        }

        // 添加新的异步协商方法
        private async Task<Stream> NegotiateStreamAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (!_configuration.SslEnabled)
                return stream;

            var validateRemoteCertificate = new RemoteCertificateValidationCallback(
                (object sender,
                X509Certificate certificate,
                X509Chain chain,
                SslPolicyErrors sslPolicyErrors)
                =>
                {
                    if (sslPolicyErrors == SslPolicyErrors.None)
                        return true;

                    if (_configuration.SslPolicyErrorsBypassed)
                        return true;
                    else
                        _logger?.LogError("Error occurred when validating remote certificate: [{0}], [{1}].",
                            this.RemoteEndPoint, sslPolicyErrors);

                    return false;
                });

            var sslStream = new SslStream(
                stream,
                false,
                validateRemoteCertificate,
                null,
                _configuration.SslEncryptionPolicy);

            if (_configuration.SslClientCertificates == null || _configuration.SslClientCertificates.Count == 0)
            {
                await sslStream.AuthenticateAsClientAsync( // No client certificates are used in the authentication. The certificate revocation list is not checked during authentication.
                    _configuration.SslTargetHost); // The name of the server that will share this SslStream. The value specified for targetHost must match the name on the server's certificate.
            }
            else
            {
                await sslStream.AuthenticateAsClientAsync(
                    _configuration.SslTargetHost, // The name of the server that will share this SslStream. The value specified for targetHost must match the name on the server's certificate.
                    _configuration.SslClientCertificates, // The X509CertificateCollection that contains client certificates.
                    _configuration.SslEnabledProtocols, // The SslProtocols value that represents the protocol used for authentication.
                    _configuration.SslCheckCertificateRevocation); // A Boolean value that specifies whether the certificate revocation list is checked during authentication.
            }

            // When authentication succeeds, you must check the IsEncrypted and IsSigned properties 
            // to determine what security services are used by the SslStream. 
            // Check the IsMutuallyAuthenticated property to determine whether mutual authentication occurred.
            _logger?.LogDebug(
                "Ssl Stream: SslProtocol[{0}], IsServer[{1}], IsAuthenticated[{2}], IsEncrypted[{3}], IsSigned[{4}], IsMutuallyAuthenticated[{5}], "
                + "HashAlgorithm[{6}], HashStrength[{7}], KeyExchangeAlgorithm[{8}], KeyExchangeStrength[{9}], CipherAlgorithm[{10}], CipherStrength[{11}].",
                sslStream.SslProtocol,
                sslStream.IsServer,
                sslStream.IsAuthenticated,
                sslStream.IsEncrypted,
                sslStream.IsSigned,
                sslStream.IsMutuallyAuthenticated,
                sslStream.HashAlgorithm,
                sslStream.HashStrength,
                sslStream.KeyExchangeAlgorithm,
                sslStream.KeyExchangeStrength,
                sslStream.CipherAlgorithm,
                sslStream.CipherStrength);

            return sslStream;
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
            await Close(true); // close by external
        }

        private async Task Close(bool shallNotifyUserSide)
        {
            if (Interlocked.Exchange(ref _state, _closed) == _closed)
            {
                return;
            }

            Shutdown();

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
                catch (Exception ex) // catch all exceptions from out-side
                {
                    await HandleUserSideError(ex);
                }
            }

            Clean();
        }

        public void Shutdown()
        {
            // The correct way to shut down the connection (especially if you are in a full-duplex conversation) 
            // is to call socket.Shutdown(SocketShutdown.Send) and give the remote party some time to close 
            // their send channel. This ensures that you receive any pending data instead of slamming the 
            // connection shut. ObjectDisposedException should never be part of the normal application flow.
            if (_tcpClient != null && _tcpClient.Connected)
            {
                _tcpClient.Client.Shutdown(SocketShutdown.Send);
            }
        }

        private void Clean()
        {
            try
            {
                try
                {
                    if (_stream != null)
                    {
                        _stream.Dispose();
                    }
                }
                catch { }
                try
                {
                    if (_tcpClient != null)
                    {
                        _tcpClient.Close();
                    }
                }
                catch { }
            }
            catch { }
            finally
            {
                _stream = null;
                _tcpClient = null;
                
                // 清空接收缓冲区
                ClearReceiveBuffer();
                
                // 确保取消任何正在进行的处理
                try
                {
                    _processCts?.Cancel();
                    _processCts?.Dispose();
                    _processCts = null;
                }
                catch { }
            }

            if (_receiveBuffer != default(ArraySegment<byte>))
                _configuration.BufferManager.ReturnBuffer(_receiveBuffer);
            _receiveBuffer = default(ArraySegment<byte>);
            _receiveBufferOffset = 0;
        }

        #endregion

        #region Exception Handler

        private async Task HandleSendOperationException(Exception ex)
        {
            if (IsSocketTimeOut(ex))
            {
                await CloseIfShould(ex);
                throw new TcpSocketException(ex.Message, new TimeoutException(ex.Message, ex));
            }

            await CloseIfShould(ex);
            throw new TcpSocketException(ex.Message, ex);
        }

        private async Task HandleReceiveOperationException(Exception ex)
        {
            if (IsSocketTimeOut(ex))
            {
                await CloseIfShould(ex);
                throw new TcpSocketException(ex.Message, new TimeoutException(ex.Message, ex));
            }

            await CloseIfShould(ex);
            throw new TcpSocketException(ex.Message, ex);
        }

        private bool IsSocketTimeOut(Exception ex)
        {
            return ex is IOException
                && ex.InnerException != null
                && ex.InnerException is SocketException
                && (ex.InnerException as SocketException).SocketErrorCode == SocketError.TimedOut;
        }

        private async Task<bool> CloseIfShould(Exception ex)
        {
            if (ex is ObjectDisposedException
                || ex is InvalidOperationException
                || ex is SocketException
                || ex is IOException
                || ex is NullReferenceException // buffer array operation
                || ex is ArgumentException      // buffer array operation
                )
            {
                _logger?.LogError(ex.Message, ex);

                await Close(false); // intend to close the session

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

            if (State != TcpSocketConnectionState.Connected)
            {
                throw new InvalidOperationException("This client has not connected to server.");
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

                // 使用带超时的写入
                var sendTask = _stream.WriteAsync(frameBuffer, frameBufferOffset, frameBufferLength);
                
                // 添加超时
                if (await Task.WhenAny(sendTask, Task.Delay(_configuration.SendTimeout, linkedCts.Token)) != sendTask)
                {
                    if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException($"Send operation timed out after {_configuration.SendTimeout.TotalMilliseconds}ms");
                    }
                    // 如果是外部取消，就继续让异常抛出
                }
                
                // 确保发送任务已完成
                await sendTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 外部请求取消，直接抛出
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

        /// <summary>
        /// 获取可读取的字节数
        /// </summary>
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

        /// <summary>
        /// 读取接收到的数据，如果没有数据可用，则阻塞直到有数据到达或超时
        /// </summary>
        /// <param name="buffer">存储读取数据的缓冲区</param>
        /// <param name="offset">缓冲区中的偏移量</param>
        /// <param name="count">要读取的最大字节数</param>
        /// <param name="timeout">超时时间，如果为null则使用配置的接收超时</param>
        /// <returns>实际读取的字节数</returns>
        public int Read(byte[] buffer, int offset, int count, TimeSpan? timeout = null)
        {
            BufferValidator.ValidateBuffer(buffer, offset, count, "buffer");
            
            if (State != TcpSocketConnectionState.Connected)
            {
                throw new InvalidOperationException("This client has not connected to server.");
            }
            
            // 使用默认超时或指定的超时
            TimeSpan actualTimeout = timeout ?? _configuration.ReceiveTimeout;
            
            // 如果没有数据可用，等待数据到达
            if (BytesAvailable == 0)
            {
                if (!_dataAvailableEvent.WaitOne(actualTimeout))
                {
                    return 0; // 超时，没有数据可用
                }
            }
            
            int totalRead = 0;
            
            lock (_dataLock)
            {
                while (totalRead < count && _dataQueue.Count > 0)
                {
                    if (_dataQueue.TryPeek(out byte[] data))
                    {
                        int bytesToRead = Math.Min(count - totalRead, data.Length);
                        Buffer.BlockCopy(data, 0, buffer, offset + totalRead, bytesToRead);
                        totalRead += bytesToRead;
                        
                        // 如果完全读取了这个数据块
                        if (bytesToRead == data.Length)
                        {
                            _dataQueue.TryDequeue(out _); // 移除已读数据块
                            _totalBytesAvailable -= data.Length;
                        }
                        else
                        {
                            // 如果只读取了部分数据，创建新的数据块保存剩余数据
                            byte[] remainingData = new byte[data.Length - bytesToRead];
                            Buffer.BlockCopy(data, bytesToRead, remainingData, 0, remainingData.Length);
                            
                            _dataQueue.TryDequeue(out _); // 移除原始数据块
                            _dataQueue.Enqueue(remainingData); // 将剩余数据重新入队
                            _totalBytesAvailable -= bytesToRead;
                            
                            break; // 已读取所需的字节数
                        }
                    }
                }
            }
            
            return totalRead;
        }

        /// <summary>
        /// 异步读取接收到的数据
        /// </summary>
        /// <param name="buffer">存储读取数据的缓冲区</param>
        /// <param name="offset">缓冲区中的偏移量</param>
        /// <param name="count">要读取的最大字节数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>实际读取的字节数</returns>
        public async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            BufferValidator.ValidateBuffer(buffer, offset, count, "buffer");
            
            if (State != TcpSocketConnectionState.Connected)
            {
                throw new InvalidOperationException("This client has not connected to server.");
            }
            
            // 如果已有数据可用，立即读取
            if (BytesAvailable > 0)
            {
                return Read(buffer, offset, count);
            }
            
            // 创建任务完成源
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            CancellationTokenRegistration registration = default;
            
            try
            {
                // 注册取消令牌
                if (cancellationToken.CanBeCanceled)
                {
                    registration = cancellationToken.Register(() => tcs.TrySetCanceled());
                }
                
                // 等待数据可用或取消
                ThreadPool.RegisterWaitForSingleObject(
                    _dataAvailableEvent, 
                    (state, timedOut) => 
                    {
                        var source = state as TaskCompletionSource<bool>;
                        if (source != null)
                        {
                            source.TrySetResult(!timedOut);
                        }
                    }, 
                    tcs, 
                    _configuration.ReceiveTimeout, 
                    true);
                
                // 等待数据可用、取消或超时
                bool dataAvailable;
                try
                {
                    dataAvailable = await tcs.Task;
                }
                catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // 重新抛出取消异常
                }
                
                if (!dataAvailable)
                {
                    return 0; // 超时，没有数据可用
                }
                
                // 有数据可用，读取数据
                return Read(buffer, offset, count);
            }
            finally
            {
                registration.Dispose();
            }
        }

        /// <summary>
        /// 清空接收缓冲区
        /// </summary>
        /// <returns>释放的字节数</returns>
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

        /// <summary>
        /// 释放指定百分比的缓冲区空间，保留最新的数据
        /// </summary>
        /// <param name="percentage">要释放的缓冲区百分比（0.0-100.0）</param>
        /// <returns>实际释放的字节数</returns>
        public int ReleaseBuffer(double percentage)
        {
            if (percentage <= 0)
                return 0;
                
            if (percentage >= 100)
                return ClearReceiveBuffer();
                
            lock (_dataLock)
            {
                if (_totalBytesAvailable == 0)
                    return 0;
                    
                long bytesToRelease = (long)(_totalBytesAvailable * percentage / 100.0);
                return ReleaseBufferBytes((int)bytesToRelease);
            }
        }
        
        /// <summary>
        /// 释放指定字节数的缓冲区空间，保留最新的数据
        /// </summary>
        /// <param name="bytes">要释放的字节数</param>
        /// <returns>实际释放的字节数</returns>
        public int ReleaseBufferBytes(int bytes)
        {
            if (bytes <= 0)
                return 0;
                
            lock (_dataLock)
            {
                if (_totalBytesAvailable == 0)
                    return 0;
                    
                int releasedBytes = 0;
                while (releasedBytes < bytes && _dataQueue.Count > 0)
                {
                    if (_dataQueue.TryPeek(out byte[] oldestData))
                    {
                        if (releasedBytes + oldestData.Length <= bytes)
                        {
                            // 可以完全释放这个数据块
                            _dataQueue.TryDequeue(out _);
                            releasedBytes += oldestData.Length;
                            _totalBytesAvailable -= oldestData.Length;
                        }
                        else
                        {
                            // 只需要释放部分数据
                            int bytesToKeep = oldestData.Length - (bytes - releasedBytes);
                            byte[] remainingData = new byte[bytesToKeep];
                            Buffer.BlockCopy(oldestData, oldestData.Length - bytesToKeep, remainingData, 0, bytesToKeep);
                            
                            _dataQueue.TryDequeue(out _); // 移除原始数据块
                            _dataQueue.Enqueue(remainingData); // 将剩余数据重新入队
                            _totalBytesAvailable -= (bytes - releasedBytes);
                            releasedBytes = bytes;
                            break;
                        }
                    }
                }
                
                return releasedBytes;
            }
        }
        
        /// <summary>
        /// 读取缓冲区中的所有数据并清空缓冲区
        /// </summary>
        /// <returns>包含缓冲区中所有数据的字节数组</returns>
        public byte[] ReadAllBytes()
        {
            lock (_dataLock)
            {
                if (_totalBytesAvailable == 0)
                    return new byte[0];
                
                // 创建一个足够大的缓冲区来存储所有数据
                byte[] result = new byte[(int)_totalBytesAvailable];
                int offset = 0;
                
                // 复制所有数据块到结果数组
                while (_dataQueue.Count > 0)
                {
                    if (_dataQueue.TryDequeue(out byte[] data))
                    {
                        Buffer.BlockCopy(data, 0, result, offset, data.Length);
                        offset += data.Length;
                    }
                }
                
                // 重置缓冲区
                _totalBytesAvailable = 0;
                
                return result;
            }
        }
        
        /// <summary>
        /// 读取缓冲区中的所有数据但不清空缓冲区
        /// </summary>
        /// <returns>包含缓冲区中所有数据的字节数组</returns>
        public byte[] PeekAllBytes()
        {
            lock (_dataLock)
            {
                if (_totalBytesAvailable == 0)
                    return new byte[0];
                
                // 创建一个足够大的缓冲区来存储所有数据
                byte[] result = new byte[(int)_totalBytesAvailable];
                int offset = 0;
                
                // 遍历队列并复制数据但不移除
                foreach (byte[] data in _dataQueue)
                {
                    Buffer.BlockCopy(data, 0, result, offset, data.Length);
                    offset += data.Length;
                }
                
                return result;
            }
        }
        
        /// <summary>
        /// 异步读取缓冲区中的所有数据并清空缓冲区
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>包含缓冲区中所有数据的字节数组</returns>
        public async Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
        {
            // 如果没有数据可读且未取消，则等待数据
            if (BytesAvailable == 0)
            {
                TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
                CancellationTokenRegistration registration = default;
                
                try
                {
                    // 注册取消令牌
                    if (cancellationToken.CanBeCanceled)
                    {
                        registration = cancellationToken.Register(() => tcs.TrySetCanceled());
                    }
                    
                    // 等待数据可用或取消
                    ThreadPool.RegisterWaitForSingleObject(
                        _dataAvailableEvent, 
                        (state, timedOut) => 
                        {
                            var source = state as TaskCompletionSource<bool>;
                            if (source != null)
                            {
                                source.TrySetResult(!timedOut);
                            }
                        }, 
                        tcs, 
                        _configuration.ReceiveTimeout, 
                        true);
                    
                    // 等待数据可用、取消或超时
                    try
                    {
                        bool dataAvailable = await tcs.Task;
                        if (!dataAvailable)
                        {
                            return new byte[0]; // 超时，没有数据可用
                        }
                    }
                    catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw; // 重新抛出取消异常
                    }
                }
                finally
                {
                    registration.Dispose();
                }
            }
            
            // 同步读取所有数据
            return ReadAllBytes();
        }
        
        #endregion
    }
}
