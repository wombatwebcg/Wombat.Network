using System;
using System.Threading;
using System.Threading.Tasks;

namespace Wombat.Network.Hosting.Strategies;

public sealed class IdleTimeoutStrategy : IIdleTimeoutStrategy
{
    private readonly TimeSpan _timeout;
    private long _lastActivityTicks;
    private CancellationTokenSource _cts;
    private Task _loopTask;

    public IdleTimeoutStrategy(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _timeout = timeout;
        NotifyActivity();
    }

    public void NotifyActivity()
    {
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
    }

    public Task StartAsync(Func<CancellationToken, Task> onTimeoutAsync, CancellationToken cancellationToken = default)
    {
        if (onTimeoutAsync == null)
        {
            throw new ArgumentNullException(nameof(onTimeoutAsync));
        }

        if (_loopTask != null)
        {
            throw new InvalidOperationException("Strategy already started.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunAsync(onTimeoutAsync, _cts.Token);
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

    private async Task RunAsync(Func<CancellationToken, Task> onTimeoutAsync, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_timeout, cancellationToken).ConfigureAwait(false);
            var idleSince = new DateTime(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);
            if (DateTime.UtcNow - idleSince >= _timeout)
            {
                await onTimeoutAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
        }
    }
}
