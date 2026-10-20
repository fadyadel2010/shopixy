using System.Reflection;
using Npgsql;
using PointOfSale.API.Services.Checkout;

namespace PointOfSale.Tests.Payments;

public sealed class CheckoutOrderCommitRetryTests
{
    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    public async Task RolledBackSqlConflict_RetriesAtMostThreeTimes(string state)
    {
        var attempts = 0;
        var retries = 0;
        var value = await Execute(() => ++attempts < 3 ? Task.FromException<int>(Conflict(state)) : Task.FromResult(42), (_, _) => retries++);
        Assert.Equal(42, value);
        Assert.Equal(3, attempts);
        Assert.Equal(2, retries);
        attempts = 0;
        var error = await Assert.ThrowsAsync<CheckoutConcurrencyConflictException>(() => Execute<int>(() => { attempts++; throw Conflict(state); }, (_, _) => { }));
        Assert.Equal(3, attempts);
        Assert.Equal(3, error.AttemptCount);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData(PostgresErrorCodes.UniqueViolation)]
    public async Task IntegrityAndBusinessFailures_AreNotRetried(string state)
    {
        var attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Execute<int>(() => { attempts++; throw Conflict(state); }, (_, _) => { }));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Cancellation_DoesNotStartOrRepeatWork()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var attempts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Execute(() => Task.FromResult(++attempts), (_, _) => { }, cts.Token));
        Assert.Equal(0, attempts);
    }

    [Fact]
    public async Task FailedRollback_IsNotSafeToRetry()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<AggregateException>(() => Execute<int>(() =>
        {
            attempts++;
            throw new AggregateException(Conflict(PostgresErrorCodes.SerializationFailure), new Exception("rollback failed"));
        }, (_, _) => { }));
        Assert.Equal(1, attempts);
    }

    private static Exception Conflict(string state) => new InvalidOperationException("SQL transaction failed", new PostgresException("QA conflict", "ERROR", "ERROR", state));
    private static Task<T> Execute<T>(Func<Task<T>> attempt, Action<Exception, int> retrying, CancellationToken ct = default) =>
        (Task<T>)typeof(CheckoutService).Assembly.GetType("PointOfSale.API.Services.Checkout.CheckoutOrderCommitRetry")!
            .GetMethod("ExecuteAsync", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(T))
            .Invoke(null, [attempt, retrying, Guid.NewGuid(), "SHQA-unit", ct])!;
}
