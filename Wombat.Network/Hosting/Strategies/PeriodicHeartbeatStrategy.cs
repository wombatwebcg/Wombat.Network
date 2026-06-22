using System;
using System.Threading;
using System.Threading.Tasks;

namespace Wombat.Network.Hosting.Strategies;

public sealed class PeriodicHeartbeatStrategy : IConnectionHeartbeatStrategy, IKeepAliveStrategy
{
    private readonly TimeSpan _interval;
    private CancellationTokenSource _cts;
    private Task _loopTask;

    public PeriodicHeartbeatStrategy(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        _interval = interval;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
        => StartAsync(_ => Task.CompletedTask, cancellationToken);

    public Task StartAsync(Func<CancellationToken, Task> onHeartbeatAsync, CancellationToken cancellationToken = default)
    {
        if (onHeartbeatAsync == null)
        {
            throw new ArgumentNullException(nameof(onHeartbeatAsync));
        }

        if (_loopTask != null)
        {
            throw new InvalidOperationException("Strategy already started.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunAsync(onHeartbeatAsync, _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts == null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            await (_loopTask ?? Task.CompletedTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _loopTask = null;
            cts.Dispose();
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> onHeartbeatAsync, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            await onHeartbeatAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
