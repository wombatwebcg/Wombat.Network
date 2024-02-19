using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Wombat.Network.Sockets;
using Wombat.Network.WebSockets.Extensions;
using Wombat.Network.WebSockets.SubProtocols;

namespace Wombat.Network.WebSockets
{
    public sealed class WebSocketClientConfiguration: SocketConfiguration
    {
        public WebSocketClientConfiguration()
        {
            BufferManager = new SegmentBufferManager(100, 8192, 1, true);
            ReceiveBufferSize = 8192;
            SendBufferSize = 8192;
            ReceiveTimeout = TimeSpan.Zero;
            SendTimeout = TimeSpan.Zero;
            NoDelay = true;
            LingerState = new LingerOption(false, 0); // The socket will linger for x seconds after Socket.Close is called.

            ConnectTimeout = TimeSpan.FromSeconds(10);
            CloseTimeout = TimeSpan.FromSeconds(5);
            KeepAliveInterval = TimeSpan.FromSeconds(30);
            KeepAliveTimeout = TimeSpan.FromSeconds(5);
            ReasonableFragmentSize = 4096;


            EnabledExtensions = new Dictionary<string, IWebSocketExtensionNegotiator>()
            {
                { PerMessageCompressionExtension.RegisteredToken, new PerMessageCompressionExtensionNegotiator() },
            };
            EnabledSubProtocols = new Dictionary<string, IWebSocketSubProtocolNegotiator>();

            OfferedExtensions = new List<WebSocketExtensionOfferDescription>()
            {
                new WebSocketExtensionOfferDescription(PerMessageCompressionExtension.RegisteredToken),
            };
            RequestedSubProtocols = new List<WebSocketSubProtocolRequestDescription>();

        }


        public TimeSpan CloseTimeout { get; set; }
        public TimeSpan KeepAliveTimeout { get; set; }
        public int ReasonableFragmentSize { get; set; }

        public Dictionary<string, IWebSocketExtensionNegotiator> EnabledExtensions { get; set; }
        public Dictionary<string, IWebSocketSubProtocolNegotiator> EnabledSubProtocols { get; set; }
        public List<WebSocketExtensionOfferDescription> OfferedExtensions { get; set; }
        public List<WebSocketSubProtocolRequestDescription> RequestedSubProtocols { get; set; }



    }
}
