using Npgsql;

namespace PointOfSale.API.Services.Payments;

/// <summary>Retry only a rolled-back local admission transaction, never an external charge.</summary>
internal static class ManualWalletAdmissionRetry
{
    internal static async Task<T> ExecuteAsync<T>(Func<Task<T>> attempt, Action<Exception, int> retrying, CancellationToken cancellationToken)
    {
        for (var number = 1; ; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await attempt(); }
            catch (Exception exception) when (number < 3 && IsSerializationFailure(exception))
            {
                retrying(exception, number);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * number), cancellationToken);
            }
        }
    }

    private static bool IsSerializationFailure(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } ||
        (exception.InnerException != null && IsSerializationFailure(exception.InnerException));
}
