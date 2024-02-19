using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network;

namespace Wombat.Network.Sockets
{
    public sealed class TcpSocketSession
    {
        #region Fields

        private static ILogger _logger;
        private TcpClient _tcpClient;
        private readonly TcpSocketServerConfiguration _configuration;
        private readonly ISegmentBufferManager _bufferManager;
        private readonly ITcpSocketServerEventDispatcher _dispatcher;
        private readonly IFrameBuilder _frameBuilder;
        private readonly TcpSocketServer _server;
        private readonly string _sessionKey;
        private Stream _stream;
        private ArraySegment<byte> _receiveBuffer = default(ArraySegment<byte>);
        private int _receiveBufferOffset = 0;
        private IPEndPoint _remoteEndPoint;
        private IPEndPoint _localEndPoint;

        private int _state;
        private const int _none = 0;
        private const int _connecting = 1;
        private const int _connected = 2;
        private const int _disposed = 5;

        #endregion

        #region Constructors

        public TcpSocketSession(
            TcpClient tcpClient,
            TcpSocketServerConfiguration configuration,
            ISegmentBufferManager bufferManager,
            ITcpSocketServerEventDispatcher dispatcher,
            IFrameBuilder frameBuilder,
            ILogger logger,
            TcpSocketServer server)
        {
            if (tcpClient == null)
                throw new ArgumentNullException("tcpClient");
            if (configuration == null)
                throw new ArgumentNullException("configuration");
            if (bufferManager == null)
                throw new ArgumentNullException("bufferManager");
            if (dispatcher == null)
                throw new ArgumentNullException("dispatcher");
            if (server == null)
                throw new ArgumentNullException("server");
            if (logger == null)
                throw new ArgumentNullException("server");

            _logger = logger;
            _tcpClient = tcpClient;
            _configuration = configuration;
            _bufferManager = bufferManager;
            _dispatcher = dispatcher;
            _server = server;
            _frameBuilder = frameBuilder;
            _sessionKey = Guid.NewGuid().ToString();
            Security = Security ?? new ServerSecurityOptions();
            this.StartTime = DateTime.UtcNow;

            SetSocketOptions();

            _remoteEndPoint = this.RemoteEndPoint;
            _localEndPoint = this.LocalEndPoint;
        }

        #endregion

        #region Properties

        public string SessionKey { get { return _sessionKey; } }
        public DateTime StartTime { get; private set; }
        public TimeSpan ConnectTimeout { get { return _configuration.ConnectTimeout; } }

        private bool Connected { get { return _tcpClient != null && _tcpClient.Client.Connected; } }
        public IPEndPoint RemoteEndPoint { get { return Connected ? (IPEndPoint)_tcpClient.Client.RemoteEndPoint : _remoteEndPoint; } }
        public IPEndPoint LocalEndPoint { get { return Connected ? (IPEndPoint)_tcpClient.Client.LocalEndPoint : _localEndPoint; } }

        public System.Net.Sockets.Socket Socket { get { return Connected ? _tcpClient.Client : null; } }

        public Stream Stream { get { return _stream; } }

        public TcpSocketServer Server { get { return _server; } }

        public ServerSecurityOptions Security { get; set; }


        public ConnectionState State
        {
            get
            {
                switch (_state)
                {
                    case _none:
                        return ConnectionState.None;
                    case _connecting:
                        return ConnectionState.Connecting;
                    case _connected:
                        return ConnectionState.Connected;
                    case _disposed:
                        return ConnectionState.Closed;
                    default:
                        return ConnectionState.Closed;
                }
            }
        }

        public override string ToString()
        {
            return string.Format("SessionKey[{0}], RemoteEndPoint[{1}], LocalEndPoint[{2}]",
                this.SessionKey, this.RemoteEndPoint, this.LocalEndPoint);
        }

        #endregion

        #region Process

        internal async Task Start()
        {
            int origin = Interlocked.CompareExchange(ref _state, _connecting, _none);
            if (origin == _disposed)
            {
                throw new ObjectDisposedException("This tcp socket session has been disposed when connecting.");
            }
            else if (origin != _none)
            {
                throw new InvalidOperationException("This tcp socket session is in invalid state when connecting.");
            }

            try
            {
                var negotiator = NegotiateStream(_tcpClient.GetStream());
                if (!negotiator.Wait(ConnectTimeout))
                {
                    await Close(false); // ssl negotiation timeout
                    throw new TimeoutException(string.Format(
                        "Negotiate SSL/TSL with remote [{0}] timeout [{1}].", this.RemoteEndPoint, ConnectTimeout));
                }
                _stream = negotiator.Result;

                if (_receiveBuffer == default(ArraySegment<byte>))
                    _receiveBuffer = _bufferManager.BorrowBuffer();
                _receiveBufferOffset = 0;

                if (Interlocked.CompareExchange(ref _state, _connected, _connecting) != _connecting)
                {
                    await Close(false); // connected with wrong state
                    throw new ObjectDisposedException("This tcp socket session has been disposed after connected.");
                }

                _logger?.LogDebug($"Session started for [{this.RemoteEndPoint}] on [{this.StartTime.ToString(@"yyyy-MM-dd HH:mm:ss.fffffff")}] " +
                    $"in dispatcher [{_dispatcher.GetType().Name}] with session count [{this.Server.SessionCount}]."
                    );
                bool isErrorOccurredInUserSide = false;
                try
                {
                    await _dispatcher.OnSessionStarted(this);
                }
                catch (Exception ex) // catch all exceptions from out-side
                {
                    isErrorOccurredInUserSide = true;
                    await HandleUserSideError(ex);
                }

                if (!isErrorOccurredInUserSide)
                {
                    await Process();
                }
                else
                {
                    await Close(true); // user side handle tcp connection error occurred
                }
            }
            catch (Exception ex) // catch exceptions then log then re-throw
            {
                _logger?.LogError($"Session [{this}] exception occurred, [{ ex.Message}].");
                await Close(true); // handle tcp connection error occurred
                throw;
            }
        }

        private async Task Process()
        {
            try
            {
                int frameLength;
                byte[] payload;
                int payloadOffset;
                int payloadCount;
                int consumedLength = 0;

                while (State == ConnectionState.Connected)
                {
                    int receiveCount = await _stream.ReadAsync(
                        _receiveBuffer.Array,
                        _receiveBuffer.Offset + _receiveBufferOffset,
                        _receiveBuffer.Count - _receiveBufferOffset);
                    if (receiveCount == 0)
                        break;

                    SegmentBufferDeflector.ReplaceBuffer(_bufferManager, ref _receiveBuffer, ref _receiveBufferOffset, receiveCount);
                    consumedLength = 0;

                    while (true)
                    {
                        frameLength = 0;
                        payload = null;
                        payloadOffset = 0;
                        payloadCount = 0;

                        if (_frameBuilder.Decoder.TryDecodeFrame(
                            _receiveBuffer.Array,
                            _receiveBuffer.Offset + consumedLength,
                            _receiveBufferOffset - consumedLength,
                            out frameLength, out payload, out payloadOffset, out payloadCount))
                        {
                            try
                            {
                                await _dispatcher.OnSessionDataReceived(this, payload, payloadOffset, payloadCount);
                            }
                            catch (Exception ex) // catch all exceptions from out-side
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
                        SegmentBufferDeflector.ShiftBuffer(_bufferManager, consumedLength, ref _receiveBuffer, ref _receiveBufferOffset);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // looking forward to a graceful quit from the ReadAsync but the inside EndRead will raise the ObjectDisposedException,
                // so a gracefully close for the socket should be a Shutdown, but we cannot avoid the Close triggers this happen.
            }
            catch (Exception ex)
            {
                await HandleReceiveOperationException(ex);
            }
            finally
            {
                await Close(true); // read async buffer returned, remote notifies closed
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

        private async Task<Stream> NegotiateStream(Stream stream)
        {
            if (!Security.SslEnabled)
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

                    if (Security.SslPolicyErrorsBypassed)
                        return true;
                    else
                        _logger?.LogError($"Session [{this}] error occurred when validating remote certificate: [{this.RemoteEndPoint}], [{sslPolicyErrors}].");

                    return false;
                });

            var sslStream = new SslStream(
                stream,
                false,
                validateRemoteCertificate,
                null,
                Security.SslEncryptionPolicy);

            if (!Security.SslClientCertificateRequired)
            {
                await sslStream.AuthenticateAsServerAsync(
                    Security.SslServerCertificate); // The X509Certificate used to authenticate the server.
            }
            else
            {
                await sslStream.AuthenticateAsServerAsync(
                    Security.SslServerCertificate, // The X509Certificate used to authenticate the server.
                    Security.SslClientCertificateRequired, // A Boolean value that specifies whether the client must supply a certificate for authentication.
                    Security.SslEnabledProtocols, // The SslProtocols value that represents the protocol used for authentication.
                    Security.SslCheckCertificateRevocation); // A Boolean value that specifies whether the certificate revocation list is checked during authentication.
            }

            // When authentication succeeds, you must check the IsEncrypted and IsSigned properties 
            // to determine what security services are used by the SslStream. 
            // Check the IsMutuallyAuthenticated property to determine whether mutual authentication occurred.
                        _logger?.LogDebug($"Ssl Stream: SslProtocol[{sslStream.SslProtocol}], " +
                $"IsServer[{sslStream.IsServer}]," +
                $" IsAuthenticated[{sslStream.IsAuthenticated}], " +
                $"IsEncrypted[{sslStream.IsEncrypted}], IsSigned[{sslStream.IsSigned}], " +
                $"IsMutuallyAuthenticated[{sslStream.IsMutuallyAuthenticated}]," +
                $"HashAlgorithm[{sslStream.HashAlgorithm}]," +
                $" HashStrength[{sslStream.HashStrength}]," +
                $" KeyExchangeAlgorithm[{sslStream.KeyExchangeAlgorithm}], " +
                $"KeyExchangeStrength[{sslStream.KeyExchangeStrength}]," +
                $" CipherAlgorithm[{sslStream.CipherAlgorithm}], " +
                $"CipherStrength[{sslStream.CipherStrength}]."
                );

            return sslStream;
        }

        #endregion

        #region Close

        public async Task Close()
        {
            await Close(true); // close by external
        }

        private async Task Close(bool shallNotifyUserSide)
        {
            if (Interlocked.Exchange(ref _state, _disposed) == _disposed)
            {
                return;
            }

            Shutdown();

            if (shallNotifyUserSide)
            {
               _logger?.LogDebug($"Session closed for [{this.RemoteEndPoint}] " +
                    $"on [{DateTime.UtcNow.ToString(@"yyyy-MM-dd HH:mm:ss.fffffff")}] " +
                    $"in dispatcher [{_dispatcher.GetType().Name}] with session count [{this.Server.SessionCount - 1}].");
                try
                {
                    await _dispatcher.OnSessionClosed(this);
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

                await Close(true); // catch specified exception then intend to close the session

                return true;
            }

            return false;
        }

        private async Task HandleUserSideError(Exception ex)
        {
            _logger?.LogError(string.Format("Session [{0}] error occurred in user side [{1}].", this, ex.Message), ex);
            await Task.CompletedTask;
        }

        #endregion

        #region Send


        public void Send(byte[] data)
        {
            Send(data, 0, data.Length);
        }

        public  void Send(byte[] data, int offset, int count)
        {
            BufferValidator.ValidateBuffer(data, offset, count, "data");

            if (State != ConnectionState.Connected)
            {
                throw new InvalidOperationException("This client has not connected to server.");
            }

            try
            {
                _stream.Write(data, offset, count);
            }
            catch (Exception ex)
            {
                HandleSendOperationException(ex).Wait(_configuration.SendTimeout);
            }

        }

        public async Task SendAsync(byte[] data)
        {
            await SendAsync(data, 0, data.Length);
        }

        public async Task SendAsync(byte[] data, int offset, int count)
        {
            BufferValidator.ValidateBuffer(data, offset, count, "data");

            if (State != ConnectionState.Connected)
            {
                throw new InvalidOperationException("This session has not connected.");
            }

            try
            {
                byte[] frameBuffer;
                int frameBufferOffset;
                int frameBufferLength;
                _frameBuilder.Encoder.EncodeFrame(data, offset, count, out frameBuffer, out frameBufferOffset, out frameBufferLength);

                await _stream.WriteAsync(frameBuffer, frameBufferOffset, frameBufferLength);
            }
            catch (Exception ex)
            {
                await HandleSendOperationException(ex);
            }
        }



        #endregion
    }
}
