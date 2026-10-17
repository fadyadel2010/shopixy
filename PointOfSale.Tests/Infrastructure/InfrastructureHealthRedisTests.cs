using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.HealthChecks;
using PointOfSale.Infrastructure.HealthChecks;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using StackExchange.Redis;

namespace PointOfSale.Tests.Infrastructure;

public sealed class InfrastructureHealthRedisTests
{
    [Theory]
    [InlineData(false, 1, HealthStatus.Healthy)]
    [InlineData(false, 49, HealthStatus.Healthy)]
    [InlineData(false, 50, HealthStatus.Degraded)]
    [InlineData(false, 199, HealthStatus.Degraded)]
    [InlineData(false, 200, HealthStatus.Unhealthy)]
    [InlineData(true, 99, HealthStatus.Healthy)]
    [InlineData(true, 100, HealthStatus.Unhealthy)]
    public async Task Latency_IsOneReadOnlyCommand_WithExistingThresholds(bool readiness, int latency, HealthStatus expected)
    {
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        db.Setup(d => d.PingAsync(CommandFlags.None)).ReturnsAsync(TimeSpan.FromMilliseconds(latency));
        using var factory = Factory(db.Object);
        var result = await Check(readiness, factory).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(expected, result.Status);
        Assert.Equal("PING", result.Data["Probe"]);
        db.Verify(d => d.PingAsync(CommandFlags.None), Times.Once);
        db.VerifyNoOtherCalls(); // No writes, deletes, scans or retries are permitted.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfiguredCommandBudget_BoundsANeverCompletingProbe_WithoutFalseSuccess(bool readiness)
    {
        var pending = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        db.Setup(d => d.PingAsync(CommandFlags.None)).Returns(pending.Task);
        using var factory = Factory(db.Object, timeoutMs: 25);
        var result = await Check(readiness, factory).CheckHealthAsync(new HealthCheckContext()).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("timeout", result.Data["FailureCategory"]);
        Assert.IsAssignableFrom<TimeoutException>(result.Exception);
        db.Verify(d => d.PingAsync(CommandFlags.None), Times.Once);
        db.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellation_PropagatesDuringAnOutstandingCommand(bool readiness)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        db.Setup(d => d.PingAsync(CommandFlags.None)).Returns(() => { entered.SetResult(); return pending.Task; });
        using var factory = Factory(db.Object, timeoutMs: 5000);
        using var cancellation = new CancellationTokenSource();
        var task = Check(readiness, factory).CheckHealthAsync(new HealthCheckContext(), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
        db.Verify(d => d.PingAsync(CommandFlags.None), Times.Once);
        db.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlreadyCancelledCaller_DoesNotStartACommand(bool readiness)
    {
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        using var factory = Factory(db.Object);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Check(readiness, factory).CheckHealthAsync(new HealthCheckContext(), new CancellationToken(true)));
        db.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CommandFailure_IsUnhealthy_AndNeverRetried(bool readiness, bool timeout)
    {
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        db.Setup(d => d.PingAsync(CommandFlags.None)).ThrowsAsync(timeout
            ? new TimeoutException("Synthetic timeout")
            : new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Synthetic unavailable dependency"));
        using var factory = Factory(db.Object);
        var result = await Check(readiness, factory).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(timeout ? "timeout" : "command-failure", result.Data["FailureCategory"]);
        db.Verify(d => d.PingAsync(CommandFlags.None), Times.Once);
        db.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingConnection_CannotReportHealthy(bool readiness)
    {
        using var factory = new RedisConnectionFactory(Options.Create(new RedisSettings()), NullLogger<RedisConnectionFactory>.Instance, null);
        var result = await Check(readiness, factory).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("unavailable", result.Data["FailureCategory"]);
    }

    [Theory]
    [InlineData(false, HealthStatus.Degraded)]
    [InlineData(true, HealthStatus.Unhealthy)]
    public async Task DisabledDependency_RemainsVisible_WithoutCommands(bool readiness, HealthStatus expected)
    {
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        using var factory = Factory(db.Object, enabled: false);
        Assert.Equal(expected, (await Check(readiness, factory).CheckHealthAsync(new HealthCheckContext())).Status);
        db.VerifyNoOtherCalls();
    }

    private static RedisConnectionFactory Factory(IDatabase db, int timeoutMs = 1000, bool enabled = true)
    {
        var connection = new Mock<IConnectionMultiplexer>();
        connection.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db);
        connection.SetupGet(c => c.IsConnected).Returns(true);
        return new RedisConnectionFactory(Options.Create(new RedisSettings { Enabled = enabled, AsyncTimeoutMs = timeoutMs }),
            NullLogger<RedisConnectionFactory>.Instance, connection.Object);
    }

    private static IHealthCheck Check(bool readiness, RedisConnectionFactory factory) => readiness
        ? new RedisDependencyHealthCheck(factory, NullLogger<RedisDependencyHealthCheck>.Instance)
        : new RedisLatencyHealthCheck(factory, NullLogger<RedisLatencyHealthCheck>.Instance);
}
