using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Net.Security;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;

namespace Wombat.Network.Sockets
{
   public class ClientSecurityOptions : SslClientAuthenticationOptions
    {
        public ClientSecurityOptions()
        {

            SslEnabled = false;
            SslTargetHost = null;
            SslClientCertificates = new X509CertificateCollection();
            SslEncryptionPolicy = EncryptionPolicy.RequireEncryption;
            SslEnabledProtocols = SslProtocols.Ssl3 | SslProtocols.Tls;
            SslCheckCertificateRevocation = false;
            SslPolicyErrorsBypassed = false;

        }
        public NetworkCredential Credential { get; set; }

    }
}
