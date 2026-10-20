using Npgsql;

namespace PointOfSale.API.Services.Checkout;

/// <summary>Retries only rolled-back SQL order creation, never post-commit provider work.</summary>
internal static class CheckoutOrderCommitRetry
{
    internal const int MaxAttempts = 3;

    internal static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> attempt,
        Action<Exception, int> retrying,
        Guid cartId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        for (var number = 1; ; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await attempt(); }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsRetryable(exception))
            {
                if (number == MaxAttempts)
                    throw new CheckoutConcurrencyConflictException(cartId, correlationId, number, exception);
                retrying(exception, number);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * number), cancellationToken);
            }
        }
    }

    internal static bool IsRetryable(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } ||
        (exception.GetType().FullName == "Microsoft.Data.SqlClient.SqlException" &&
         exception.GetType().GetProperty("Number")?.GetValue(exception) is int number && number == 1205) ||
        (exception is not (OperationCanceledException or AggregateException) && exception.InnerException != null && IsRetryable(exception.InnerException));
}
