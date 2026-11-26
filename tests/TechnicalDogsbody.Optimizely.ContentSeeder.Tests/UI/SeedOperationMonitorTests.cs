using TechnicalDogsbody.Optimizely.ContentSeeder.UI;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.UI;

public class SeedOperationMonitorTests
{
    [Fact]
    public void Current_BeforeAnyOperation_IsIdle()
    {
        var monitor = new SeedOperationMonitor();

        Assert.False(monitor.Current.IsRunning);
        Assert.Null(monitor.Current.Name);
    }

    [Fact]
    public void TryBegin_WhileRunning_ReturnsFalse()
    {
        var monitor = new SeedOperationMonitor();

        Assert.True(monitor.TryBegin("Seed"));
        Assert.False(monitor.TryBegin("Delete"));
        Assert.Equal("Seed", monitor.Current.Name);
        Assert.True(monitor.Current.IsRunning);
        Assert.NotNull(monitor.Current.StartedUtc);
    }

    [Fact]
    public void Complete_ReleasesSlotAndRecordsSuccess()
    {
        var monitor = new SeedOperationMonitor();
        monitor.TryBegin("Seed");

        monitor.Complete();

        Assert.False(monitor.Current.IsRunning);
        Assert.True(monitor.Current.Succeeded);
        Assert.Null(monitor.Current.Error);
        Assert.True(monitor.TryBegin("Reseed"));
    }

    [Fact]
    public void Complete_WithError_RecordsFailureMessage()
    {
        var monitor = new SeedOperationMonitor();
        monitor.TryBegin("Delete");

        monitor.Complete(new InvalidOperationException("Database unavailable"));

        Assert.False(monitor.Current.Succeeded);
        Assert.Equal("Database unavailable", monitor.Current.Error);
        Assert.Equal("Delete", monitor.Current.Name);
    }

    [Fact]
    public void RequestCancel_WhileRunning_CancelsTheTokenAndShowsStopping()
    {
        var monitor = new SeedOperationMonitor();
        monitor.TryBegin("Reseed");
        var token = monitor.Token;

        Assert.True(monitor.RequestCancel());

        Assert.True(token.IsCancellationRequested);
        Assert.True(monitor.Current.IsRunning);
        Assert.True(monitor.Current.IsCancelling);
    }

    [Fact]
    public void RequestCancel_WhenNothingIsRunning_ReturnsFalse()
    {
        var monitor = new SeedOperationMonitor();

        Assert.False(monitor.RequestCancel());

        monitor.TryBegin("Seed");
        monitor.Complete();
        Assert.False(monitor.RequestCancel());
    }

    [Fact]
    public void Complete_AfterCancel_RecordsStoppedNotFailed()
    {
        var monitor = new SeedOperationMonitor();
        monitor.TryBegin("Seed");
        monitor.RequestCancel();

        monitor.Complete(new OperationCanceledException(monitor.Token));

        Assert.False(monitor.Current.IsRunning);
        Assert.False(monitor.Current.IsCancelling);
        Assert.True(monitor.Current.Cancelled);
        Assert.False(monitor.Current.Succeeded);
        Assert.Null(monitor.Current.Error);
    }

    [Fact]
    public void Complete_WithACancellationNobodyAskedFor_IsAFailure()
    {
        var monitor = new SeedOperationMonitor();
        monitor.TryBegin("Seed");

        monitor.Complete(new OperationCanceledException("Timed out"));

        Assert.False(monitor.Current.Cancelled);
        Assert.Equal("Timed out", monitor.Current.Error);
    }

    [Fact]
    public void NextOperation_GetsAFreshToken()
    {
        var monitor = new SeedOperationMonitor();
        monitor.TryBegin("Seed");
        monitor.RequestCancel();
        monitor.Complete(new OperationCanceledException());

        monitor.TryBegin("Seed");

        Assert.False(monitor.Token.IsCancellationRequested);
        Assert.False(monitor.Current.Cancelled);
        Assert.False(monitor.Current.IsCancelling);
    }
}
