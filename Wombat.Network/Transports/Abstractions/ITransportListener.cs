using System.Threading;
using System.Threading.Tasks;

namespace Wombat.Network.Transports.Abstractions;

public interface ITransportListener
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}
