using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Wombat.Network;

namespace Wombat.Network.Sockets
{
   public class SslClientAuthenticationOptions
    {

        /// <summary>
        /// 获取或设置一个值，指示是否允许重新协商连接。
        /// </summary>
        public bool AllowRenegotiation { get; set; }

        /// <summary>
        /// 获取或设置支持的应用层协议列表。
        /// </summary>
        public List<SslApplicationProtocol> ApplicationProtocols { get; set; }

        /// <summary>
        /// 获取或设置证书吊销检查模式。
        /// </summary>
        public X509RevocationMode CertificateRevocationCheckMode { get; set; }

        /// <summary>
        /// 获取或设置本地证书选择回调。
        /// </summary>
        public LocalCertificateSelectionCallback LocalCertificateSelectionCallback { get; set; }

        /// <summary>
        /// 获取或设置远程证书验证回调。
        /// </summary>
        public RemoteCertificateValidationCallback RemoteCertificateValidationCallback { get; set; }

        /// <summary>
        /// 获取或设置一个值，该值指示是否启用 SSL/TLS 加密
        /// </summary>
        public bool SslEnabled { get; set; }

        /// <summary>
        /// 获取或设置 SSL/TLS 目标主机名
        /// </summary>
        public string SslTargetHost { get; set; }

        /// <summary>
        /// 获取或设置 SSL/TLS 客户端证书集合
        /// </summary>
        public X509CertificateCollection SslClientCertificates { get; set; }

        /// <summary>
        /// 获取或设置 SSL/TLS 加密策略
        /// </summary>
        public EncryptionPolicy SslEncryptionPolicy { get; set; }

        /// <summary>
        /// 获取或设置 SSL/TLS 启用的协议版本
        /// </summary>
        public SslProtocols SslEnabledProtocols { get; set; }

        /// <summary>
        /// 获取或设置一个值，该值指示是否检查证书吊销
        /// </summary>
        public bool SslCheckCertificateRevocation { get; set; }

        /// <summary>
        /// 获取或设置一个值，该值指示是否绕过 SSL/TLS 策略错误
        /// </summary>
        public bool SslPolicyErrorsBypassed { get; set; }



    }
}
