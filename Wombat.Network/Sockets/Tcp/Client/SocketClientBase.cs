using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Wombat.Network.Sockets
{
    public  class SocketClientBase : ISocketClient,IDisposable
    {
        #region Fields

        protected internal ILogger _logger;
        private Socket _socket;

        protected internal TcpSocketClientConfiguration _configuration;
        protected internal ClientSecurityOptions _securityOptions;
        protected internal IPEndPoint _remoteEndPoint;
        protected internal IPEndPoint _localEndPoint;
        protected internal Stream _stream;
        protected internal ArraySegment<byte> _receiveBuffer = default(ArraySegment<byte>);
        protected internal int _receiveBufferOffset = 0;

        protected internal int _state;
        protected internal const int _none = 0;
        protected internal const int _connecting = 1;
        protected internal const int _connected = 2;
        protected internal const int _closing = 3;
        protected internal const int _closed = 5;
        #endregion

        public SocketClientBase()
        {
            _configuration = _configuration ?? new TcpSocketClientConfiguration();
            _securityOptions = _securityOptions ?? new ClientSecurityOptions();
        }

        public SocketClientBase(IPEndPoint localEndPoint, TcpSocketClientConfiguration configuration,ClientSecurityOptions securityOptions)
        {
            _localEndPoint = localEndPoint;
            _configuration = configuration??new TcpSocketClientConfiguration();
            _securityOptions = securityOptions??new ClientSecurityOptions();
            if (_configuration.BufferManager == null)
                throw new InvalidProgramException("The buffer manager in configuration cannot be null.");
            if (_securityOptions == null)
                throw new InvalidProgramException("The securitOptions handler in configuration cannot be null.");

        }

        #region Properties

        public TcpSocketClientConfiguration SocketConfiguration => _configuration;

        public bool Connected => _socket == null ? false: _socket.Connected;

        public IPEndPoint RemoteEndPoint => _remoteEndPoint;

        public  ConnectionState State
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
                    case _closed:
                        return ConnectionState.Closed;
                    default:
                        return ConnectionState.Closed;
                }
            }
        }

        public IPEndPoint LocalEndPoint { get; set; }

        public ClientSecurityOptions SecurityOptions => _securityOptions;

        public TimeSpan ConnectTimeout => _configuration.ConnectTimeout;

        public TimeSpan KeepAliveInterval => _configuration.KeepAliveInterval;


        #endregion

        public virtual void UsgLogger(ILogger log)
        {
            _logger = log;
        }

        bool _isUdp;
        private bool disposedValue;

        //public virtual void AsUdp()
        //{
        //    _isUdp = true;
        //}

        #region Connect

        public virtual async Task ConnectAsync(IPEndPoint remoteEndPoint)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException("remoteEP");
            _remoteEndPoint = remoteEndPoint;
            int origin = Interlocked.Exchange(ref _state, _connecting);
            if (!(origin == _none || origin == _closed))
            {
                await CloseAsync(false); // connecting with wrong state
                throw new InvalidOperationException("This tcp socket client is in invalid state when connecting.");
            }

            Clean(); // force to clean

            try
            {

                if(_isUdp)
                {
                    _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                }
                else
                {
                    _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                }
                if (_localEndPoint != null) _socket.Bind(_localEndPoint);
                SetSocketOptions();

                var awaiter = _socket.ConnectAsync(_remoteEndPoint.Address, _remoteEndPoint.Port);
                if (!awaiter.Wait(SocketConfiguration.ConnectTimeout))
                {
                    await CloseAsync(false); // connect timeout
                    throw new TimeoutException(string.Format(
                        "Connect to [{0}] timeout [{1}].", _remoteEndPoint, SocketConfiguration.ConnectTimeout));
                }

                var negotiator = NegotiateStream(new NetworkStream(_socket, true));
                if (!negotiator.Wait(SocketConfiguration.ConnectTimeout))
                {
                    await CloseAsync(false); // ssl negotiation timeout
                    throw new TimeoutException(string.Format(
                        "Negotiate SSL/TSL with remote [{0}] timeout [{1}].", _remoteEndPoint, SocketConfiguration.ConnectTimeout));
                }
                _stream = negotiator.Result;

                if (_receiveBuffer == default(ArraySegment<byte>))
                    _receiveBuffer = SocketConfiguration.BufferManager.BorrowBuffer();
                _receiveBufferOffset = 0;

                if (Interlocked.CompareExchange(ref _state, _connected, _connecting) != _connecting)
                {
                    await CloseAsync(false); // connected with wrong state
                    throw new InvalidOperationException("This tcp socket client is in invalid state when connected.");
                }

            }
            catch (Exception ex) // catch exceptions then log then re-throw
            {
              
                _logger?.LogError(ex.Message, ex);
                await CloseAsync(true); // handle tcp connection error occurred
                throw;
            }
        }
        public void Connect(IPEndPoint remoteEndPoint) => ConnectAsync(remoteEndPoint).Wait();


        private void SetSocketOptions()
        {
            _socket.ReceiveBufferSize = SocketConfiguration.ReceiveBufferSize;
            _socket.SendBufferSize = SocketConfiguration.SendBufferSize;
            _socket.ReceiveTimeout = (int)SocketConfiguration.ReceiveTimeout.TotalMilliseconds;
            _socket.SendTimeout = (int)SocketConfiguration.SendTimeout.TotalMilliseconds;
            _socket.NoDelay = SocketConfiguration.NoDelay;
            _socket.LingerState = SocketConfiguration.LingerState;

            if (SocketConfiguration.KeepAlive)
            {
                _socket.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.KeepAlive,
                    (int)SocketConfiguration.KeepAliveInterval.TotalMilliseconds);
            }

            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, SocketConfiguration.ReuseAddress);
        }

        private async Task<Stream> NegotiateStream(Stream stream)
        {
            if (!SecurityOptions.SslEnabled)
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

                    if (SecurityOptions.SslPolicyErrorsBypassed)
                        return true;
                    else
                        _logger?.LogError($"Error occurred when validating remote certificate: [{ this.RemoteEndPoint}], [{sslPolicyErrors}]");
                    return false;
                });

            var sslStream = new SslStream(
                stream,
                false,
                validateRemoteCertificate,
                null,
                SecurityOptions.SslEncryptionPolicy);

            if (SecurityOptions.SslClientCertificates == null || SecurityOptions.SslClientCertificates.Count == 0)
            {
                await sslStream.AuthenticateAsClientAsync( // No client certificates are used in the authentication. The certificate revocation list is not checked during authentication.
                    SecurityOptions.SslTargetHost); // The name of the server that will share this SslStream. The value specified for targetHost must match the name on the server's certificate.
            }
            else
            {
                await sslStream.AuthenticateAsClientAsync(
                    SecurityOptions.SslTargetHost, // The name of the server that will share this SslStream. The value specified for targetHost must match the name on the server's certificate.
                    SecurityOptions.SslClientCertificates, // The X509CertificateCollection that contains client certificates.
                    SecurityOptions.SslEnabledProtocols, // The SslProtocols value that represents the protocol used for authentication.
                    SecurityOptions.SslCheckCertificateRevocation); // A Boolean value that specifies whether the certificate revocation list is checked during authentication.
            }

            // When authentication succeeds, you must check the IsEncrypted and IsSigned properties 
            // to determine what security services are used by the SslStream. 
            // Check the IsMutuallyAuthenticated property to determine whether mutual authentication occurred.
            _logger?.LogDebug(
                $"Ssl Stream: SslProtocol[{sslStream.SslProtocol}]," +
                $" IsServer[{sslStream.IsServer}], " +
                $"IsAuthenticated[{sslStream.IsAuthenticated}], " +
                $"IsEncrypted[{sslStream.IsEncrypted}], " +
                $"IsSigned[{ sslStream.IsSigned}], " +
                $"IsMutuallyAuthenticated[{sslStream.IsMutuallyAuthenticated}]," +
                $"HashAlgorithm[{sslStream.HashAlgorithm}]," +
                $" HashStrength[{sslStream.HashStrength}], " +
                $"KeyExchangeAlgorithm[{sslStream.KeyExchangeAlgorithm}]," +
                $" KeyExchangeStrength[{sslStream.KeyExchangeStrength}]," +
                $" CipherAlgorithm[{sslStream.CipherAlgorithm}], " +
                $"CipherStrength[{sslStream.CipherStrength}].");

            return sslStream;
        }

        #endregion

        #region Close

        public virtual async Task CloseAsync()
        {
            await CloseAsync(true); // close by external
        }

        private async Task CloseAsync(bool shallNotifyUserSide)
        {

            if (Interlocked.Exchange(ref _state, _closed) == _closed)
            {
                return;
            }
            await Task.Run(() => { Shutdown(); });
            if (shallNotifyUserSide)
            {
                _logger?.LogDebug($"Disconnected from server [{this.RemoteEndPoint}] " +
                    $"on [{DateTime.UtcNow.ToString(@"yyyy-MM-dd HH:mm:ss.fffffff")}].");

            }

            Clean();
        }

        private void Close(bool shallNotifyUserSide)
        {

            if (Interlocked.Exchange(ref _state, _closed) == _closed)
            {
                return;
            }
            Shutdown();
            if (shallNotifyUserSide)
            {
                _logger?.LogDebug($"Disconnected from server [{this.RemoteEndPoint}] " +
                    $"on [{DateTime.UtcNow.ToString(@"yyyy-MM-dd HH:mm:ss.fffffff")}].");

            }

            Clean();
        }

        public virtual void Close()
        {
            Close(true);
        }



        public void Shutdown()
        {
            // The correct way to shut down the connection (especially if you are in a full-duplex conversation) 
            // is to call socket.Shutdown(SocketShutdown.Send) and give the remote party some time to close 
            // their send channel. This ensures that you receive any pending data instead of slamming the 
            // connection shut. ObjectDisposedException should never be part of the normal application flow.
            if (_socket != null && _socket.Connected)
            {
                _socket.Shutdown(SocketShutdown.Send);
            }
        }

        internal void Clean()
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
                    if (_socket != null)
                    {
                        _socket.Close();
                    }
                }
                catch { }
            }
            catch { }
            finally
            {
                _stream = null;
                _socket = null;
            }

            if (_receiveBuffer != default(ArraySegment<byte>))
                _configuration?.BufferManager.ReturnBuffer(_receiveBuffer);
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

                await CloseAsync(false); // intend to close the session

                return true;
            }

            return false;
        }


        #endregion

        #region Send

        public void Send(byte[] data)
        {
            Send(data, 0, data.Length);
        }

        public virtual void Send(byte[] data, int offset, int count)
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

        public virtual async Task SendAsync(byte[] data, int offset, int count)
        {
            BufferValidator.ValidateBuffer(data, offset, count, "data");

            if (State != ConnectionState.Connected)
            {
                throw new InvalidOperationException("This client has not connected to server.");
            }

            try
            {
                await _stream.WriteAsync(data, offset, count);
            }
            catch (Exception ex)
            {
                await HandleSendOperationException(ex);
            }
        }

        #endregion

        #region Receive

        public int Receive(byte[] data)
        {
            return Receive(data, 0, data.Length);
        }

        public virtual int Receive(byte[] data, int offset, int count)
        {
            BufferValidator.ValidateBuffer(data, offset, count, "data");

            if (State != ConnectionState.Connected)
            {
                throw new InvalidOperationException("This client has not connected to server.");
            }

            try
            {
                return _stream.Read(data, offset, count);
            }
            catch (Exception ex)
            {
                HandleSendOperationException(ex).Wait(_configuration.ReceiveTimeout);
                return 0;
            }
        }


        public async ValueTask<int> ReceiveAsync(byte[] data)
        {
            return await ReceiveAsync(data, 0, data.Length);
        }


        public virtual async ValueTask<int> ReceiveAsync(byte[] data, int offset, int count)
        {
            BufferValidator.ValidateBuffer(data, offset, count, "data");

            if (State != ConnectionState.Connected)
            {
                throw new InvalidOperationException("This client has not connected to server.");
            }

            try
            {
                return await _stream.ReadAsync(data, offset, count);
            }
            catch (Exception ex)
            {
                await HandleSendOperationException(ex);
                return await Task.FromResult(0);
            }
        }




        #endregion

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    try
                    {
                        CloseAsync(false).Wait();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex.Message, ex);
                    }
                    // TODO: 释放托管状态(托管对象)
                }

                // TODO: 释放未托管的资源(未托管的对象)并重写终结器
                // TODO: 将大型字段设置为 null
                disposedValue = true;
            }
        }

        // // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        // ~SocketClientBase()
        // {
        //     // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

    }
}
