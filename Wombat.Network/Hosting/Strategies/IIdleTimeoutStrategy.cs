using System;
using System.Threading;
using System.Threading.Tasks;

namespace Wombat.Network.Hosting.Strategies;

public interface IIdleTimeoutStrategy
{
    void NotifyActivity();
    Task StartAsync(Func<CancellationToken, Task> onTimeoutAsync, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
