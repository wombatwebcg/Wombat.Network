using System.Threading;
using System.Threading.Tasks;

namespace Wombat.Network.Hosting.Strategies;

public interface IKeepAliveStrategy
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
