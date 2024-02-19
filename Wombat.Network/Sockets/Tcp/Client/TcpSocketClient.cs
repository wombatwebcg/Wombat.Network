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
    public class TcpSocketClient : SocketClientBase
    {
        #region Fields

        private static ITcpSocketClientEventDispatcher _dispatcher;
        private static IFrameBuilder _frameBuilder;

        #endregion

        #region Constructors

        public TcpSocketClient(IPAddress localAddress, int localPort, ITcpSocketClientEventDispatcher dispatcher, TcpSocketClientConfiguration configuration = null, ClientSecurityOptions securityOptions = null, IFrameBuilder frameBuilder = null)
            : this(new IPEndPoint(localAddress, localPort), dispatcher, configuration, securityOptions, frameBuilder)
        {
        }

        public TcpSocketClient(ITcpSocketClientEventDispatcher dispatcher, TcpSocketClientConfiguration configuration = null, ClientSecurityOptions securityOptions = null, IFrameBuilder frameBuilder = null)
            : this(null, dispatcher, configuration, securityOptions, frameBuilder)
        {

        }


        public TcpSocketClient(IPEndPoint localEP, ITcpSocketClientEventDispatcher dispatcher, TcpSocketClientConfiguration configuration = null, ClientSecurityOptions securityOptions = null,
            IFrameBuilder frameBuilder = null) : base(localEP, configuration, securityOptions)
        {
            if (dispatcher == null)
                throw new ArgumentNullException("dispatcher");

            _localEndPoint = localEP;
            _dispatcher = dispatcher ?? new DefaultTcpSocketClientEventDispatcher();
            _frameBuilder = frameBuilder ?? new RawBufferFrameBuilder();
            _securityOptions = securityOptions ?? new ClientSecurityOptions();
            _configuration = configuration ?? new TcpSocketClientConfiguration();
            if (_frameBuilder == null)
                throw new InvalidProgramException("The frame handler in configuration cannot be null.");
        }

        #endregion

        public IFrameBuilder FrameBuilder => _frameBuilder;

        private bool _isSubscribe = false;
        public void UseSubscribe()
        {
            _isSubscribe = true;
        }

        public void CancelSubscription()
        {
            _isSubscribe = false;
        }

        #region Connect

        public override async Task ConnectAsync(IPEndPoint remoteEndPoint)
        {
            bool isErrorOccurredInUserSide = false;
            try
            {
                await base.ConnectAsync(remoteEndPoint);
                await _dispatcher.OnServerConnected(this);
            }
            catch (Exception ex) // catch all exceptions from out-side
            {
                isErrorOccurredInUserSide = true;
                await HandleUserSideError(ex);
            }

            if (!isErrorOccurredInUserSide)
            {
                Task.Factory.StartNew(async () =>
                {
                    await Process();
                },
                TaskCreationOptions.None).Forget();
            }
            else
            {
                await CloseAsync(true); // user side handle tcp connection error occurred
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
                while (true)
                {
                    while (State == ConnectionState.Connected & _isSubscribe)
                    {
                        int receiveCount = await _stream.ReadAsync(
                            _receiveBuffer.Array,
                            _receiveBuffer.Offset + _receiveBufferOffset,
                            _receiveBuffer.Count - _receiveBufferOffset);
                        if (receiveCount == 0)
                            break;

                        SegmentBufferDeflector.ReplaceBuffer(_configuration.BufferManager, ref _receiveBuffer, ref _receiveBufferOffset, receiveCount);
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
                                    await _dispatcher.OnServerDataReceived(this, payload, payloadOffset, payloadCount);
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
                            SegmentBufferDeflector.ShiftBuffer(_configuration.BufferManager, consumedLength, ref _receiveBuffer, ref _receiveBufferOffset);
                        }
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
                await CloseAsync(true); // read async buffer returned, remote notifies closed
            }
        }



        #endregion

        #region Close

        public override async Task CloseAsync()
        {
            await CloseAsync(true); // close by external
        }

        private async Task CloseAsync(bool shallNotifyUserSide)
        {

            if (Interlocked.Exchange(ref _state, _closed) == _closed)
            {
                return;
            }
            Shutdown();
            if (shallNotifyUserSide)
            {
                _logger?.LogDebug($"Disconnected from server [{this.RemoteEndPoint}] " +
                    $"with dispatcher [{_dispatcher.GetType().Name}] " +
                    $"on [{DateTime.UtcNow.ToString(@"yyyy-MM-dd HH:mm:ss.fffffff")}].");

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

                await CloseAsync(false); // intend to close the session

                return true;
            }

            return false;
        }

        private async Task HandleUserSideError(Exception ex)
        {
            _logger?.LogError($"Client [{this}] error occurred in user side [{ex.Message}].");
            await Task.CompletedTask;
        }



        #endregion

        #region Send


        public override async Task SendAsync(byte[] data, int offset, int count)
        {
            BufferValidator.ValidateBuffer(data, offset, count, "data");

            if (State != ConnectionState.Connected)
            {
                throw new InvalidOperationException("This client has not connected to server.");
            }

            try
            {
                byte[] frameBuffer;
                int frameBufferOffset;
                int frameBufferLength;
                if (_frameBuilder != null)
                {
                    _frameBuilder.Encoder.EncodeFrame(data, offset, count, out frameBuffer, out frameBufferOffset, out frameBufferLength);
                    await _stream.WriteAsync(frameBuffer, frameBufferOffset, frameBufferLength);
                }
                else
                {
                    await _stream.WriteAsync(data, offset, count);
                }
            }
            catch (Exception ex)
            {
                await HandleSendOperationException(ex);
            }
        }

        public override async ValueTask<int> ReceiveAsync(byte[] data, int offset, int count)
        {
            BufferValidator.ValidateBuffer(data, offset, count, "data");

            if (State != ConnectionState.Connected)
            {
                throw new InvalidOperationException("This client has not connected to server.");
            }
            int frameLength;
            byte[] payload;
            int payloadOffset;
            int payloadCount;
            int consumedLength = 0;
            int receiveCount =  _stream.Read(
                _receiveBuffer.Array,
                _receiveBuffer.Offset + _receiveBufferOffset,
                _receiveBuffer.Count - _receiveBufferOffset);
            if (receiveCount == 0)
                return receiveCount;

            SegmentBufferDeflector.ReplaceBuffer(_configuration.BufferManager, ref _receiveBuffer, ref _receiveBufferOffset, receiveCount);
            consumedLength = 0;
            byte[] resultCahce =new byte[data.Length]; 
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
                        if(payloadCount >= data.Length)
                        {
                            Array.Copy(payload, payloadOffset, data, 0, data.Length);
                            break;

                        }
                        if (data.Length > consumedLength)
                        {
                            Array.Copy(payload, payloadOffset, resultCahce, consumedLength, payloadCount);
                        }
                        else
                        {
                            Array.Copy(resultCahce, 0, data, 0, consumedLength);
                            break;

                        }

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
                    Array.Copy(resultCahce, 0, data, 0, consumedLength);
                    break;
                }
            }

            if (_receiveBuffer != null && _receiveBuffer.Array != null)
            {
                SegmentBufferDeflector.ShiftBuffer(_configuration.BufferManager, consumedLength, ref _receiveBuffer, ref _receiveBufferOffset);
            }

            return receiveCount;
        }

        #endregion

    }
}
