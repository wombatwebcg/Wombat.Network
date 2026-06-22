using System.Net;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Wombat.Network.Transports.Abstractions;

public interface ITransportConnection
{
    string Id { get; }
    EndPoint LocalEndPoint { get; }
    EndPoint RemoteEndPoint { get; }
    IDuplexPipe Transport { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}
