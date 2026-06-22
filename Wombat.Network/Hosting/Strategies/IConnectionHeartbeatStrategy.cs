using System;
using System.Threading;
using System.Threading.Tasks;

namespace Wombat.Network.Hosting.Strategies;

public interface IConnectionHeartbeatStrategy
{
    Task StartAsync(Func<CancellationToken, Task> onHeartbeatAsync, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
