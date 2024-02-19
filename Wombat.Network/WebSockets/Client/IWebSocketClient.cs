using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Wombat.Network.Sockets;
using Wombat.Network.WebSockets.Extensions;
using Wombat.Network.WebSockets.SubProtocols;

namespace Wombat.Network.WebSockets
{
    public interface IWebSocketClient : ISocketClient
    {


        void UsgLogger(ILogger log);


        #region Properties

        Uri Uri { get; }

        TimeSpan KeepAliveTimeout { get; }

        IDictionary<string, IWebSocketExtensionNegotiator> EnabledExtensions { get; }
        IDictionary<string, IWebSocketSubProtocolNegotiator> EnabledSubProtocols { get; }
        IEnumerable<WebSocketExtensionOfferDescription> OfferedExtensions { get; }
        IEnumerable<WebSocketSubProtocolRequestDescription> RequestedSubProtocols { get; }


        #endregion

        #region Send

        Task SendTextAsync(string text);

        Task SendBinaryAsync(byte[] data);

        Task SendBinaryAsync(byte[] data, int offset, int count);

        Task SendBinaryAsync(ArraySegment<byte> segment);

        Task SendStreamAsync(Stream stream);

        #endregion





    }
}
