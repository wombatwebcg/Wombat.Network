using FluentAssertions;
using System;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Hosting.Strategies;
using Xunit;

namespace Wombat.Network.UnitTest.NewModel;

public class StrategyTests
{
    [Fact]
    public async Task PeriodicHeartbeatStrategy_ShouldInvokeCallback()
    {
        var count = 0;
        var strategy = new PeriodicHeartbeatStrategy(TimeSpan.FromMilliseconds(50));

        await strategy.StartAsync(_ =>
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });

        await Task.Delay(130);
        await strategy.StopAsync();

        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task IdleTimeoutStrategy_ShouldTriggerAfterIdlePeriod()
    {
        var triggered = false;
        var strategy = new IdleTimeoutStrategy(TimeSpan.FromMilliseconds(80));

        await strategy.StartAsync(_ =>
        {
            triggered = true;
            return Task.CompletedTask;
        });

        await Task.Delay(140);
        await strategy.StopAsync();

        triggered.Should().BeTrue();
    }
}
