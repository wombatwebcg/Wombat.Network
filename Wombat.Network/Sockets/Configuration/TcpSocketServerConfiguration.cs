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
    public sealed class TcpSocketServerConfiguration : SocketConfiguration
    {
        public TcpSocketServerConfiguration()
    : this(new SegmentBufferManager(1024, 8192, 1, true))
        {
        }

        public TcpSocketServerConfiguration(ISegmentBufferManager bufferManager)
        {
            BufferManager = bufferManager;

            ReceiveBufferSize = 8192;
            SendBufferSize = 8192;
            ReceiveTimeout = TimeSpan.Zero;
            SendTimeout = TimeSpan.Zero;
            NoDelay = true;
            LingerState = new LingerOption(false, 0);
            KeepAlive = false;
            KeepAliveInterval = TimeSpan.FromSeconds(5);
            ReuseAddress = false;

            ConnectTimeout = TimeSpan.FromSeconds(15);
        }


        /// <summary>
        /// 待处理连接队列
        /// </summary>
        public int PendingConnectionBacklog { get; set; }

        /// <summary>
        /// 是否允许网络地址转换（NAT）穿越
        /// </summary>
        public bool AllowNatTraversal { get; set; }


    }
}
