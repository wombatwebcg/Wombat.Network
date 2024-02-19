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
   public class SocketConfiguration
    {

        /// <summary>
        /// 获取或设置用于管理缓冲区的对象
        /// </summary>
        public ISegmentBufferManager BufferManager { get; set; }

        /// <summary>
        /// 获取或设置接收缓冲区的大小
        /// </summary>
        public int ReceiveBufferSize { get; set; }

        /// <summary>
        /// 获取或设置发送缓冲区的大小
        /// </summary>
        public int SendBufferSize { get; set; }

        /// <summary>
        /// 获取或设置接收操作的超时时间
        /// </summary>
        public TimeSpan ReceiveTimeout { get; set; }

        /// <summary>
        /// 获取或设置发送操作的超时时间
        /// </summary>
        public TimeSpan SendTimeout { get; set; }

        /// <summary>
        /// 获取或设置一个值，该值指示是否禁用 Nagle 算法
        /// </summary>
        public bool NoDelay { get; set; }

        /// <summary>
        /// 获取或设置一个值，该值指示是否启用套接字的延迟关闭
        /// </summary>
        public LingerOption LingerState { get; set; }

        /// <summary>
        /// 获取或设置一个值，该值指示是否启用 TCP keep-alive
        /// </summary>
        public bool KeepAlive { get; set; }

        /// <summary>
        /// 获取或设置 TCP keep-alive 间隔
        /// </summary>
        public TimeSpan KeepAliveInterval { get; set; }

        /// <summary>
        /// 获取或设置一个值，该值指示是否允许多个套接字绑定到同一个端口
        /// </summary>
        public bool ReuseAddress { get; set; }



        /// <summary>
        /// 获取或设置连接操作的超时时间
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; }

    }
}
