using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Wombat.Network;

namespace Wombat.Network.Sockets
{
    public sealed class TcpSocketClientConfiguration : SocketConfiguration
    {
        public TcpSocketClientConfiguration()
    : this(new SegmentBufferManager(100, 8192, 1, true))
        {
        }

        public TcpSocketClientConfiguration(ISegmentBufferManager bufferManager)
        {
            BufferManager = bufferManager;

            ReceiveBufferSize = 8192;                   // Specifies the total per-socket buffer space reserved for receives. This is unrelated to the maximum message size or the size of a TCP window.
            SendBufferSize = 8192;                      // Specifies the total per-socket buffer space reserved for sends. This is unrelated to the maximum message size or the size of a TCP window.
            ReceiveTimeout = TimeSpan.Zero;             // Receive a time-out. This option applies only to synchronous methods; it has no effect on asynchronous methods such as the BeginSend method.
            SendTimeout = TimeSpan.Zero;                // Send a time-out. This option applies only to synchronous methods; it has no effect on asynchronous methods such as the BeginSend method.
            NoDelay = true;                             // Disables the Nagle algorithm for send coalescing.
            LingerState = new LingerOption(false, 0);   // The socket will linger for x seconds after Socket.Close is called.
            KeepAlive = false;                          // Use keep-alives.
            KeepAliveInterval = TimeSpan.FromSeconds(5);// https://msdn.microsoft.com/en-us/library/system.net.sockets.socketoptionname(v=vs.110).aspx
            ReuseAddress = false;                       // Allows the socket to be bound to an address that is already in use.
            ConnectTimeout = TimeSpan.FromSeconds(5);
        }

    }
}
