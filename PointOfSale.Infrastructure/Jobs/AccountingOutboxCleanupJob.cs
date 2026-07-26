using Microsoft.Extensions.Logging;
using PointOfSale.Core.Interfaces.Accounting;

namespace PointOfSale.Infrastructure.Jobs;

public sealed class AccountingOutboxCleanupJob
{
    private readonly IAccountingOutboxProcessor _outboxProcessor;
    private readonly ILogger<AccountingOutboxCleanupJob> _logger;

    public AccountingOutboxCleanupJob(
        IAccountingOutboxProcessor outboxProcessor,
        ILogger<AccountingOutboxCleanupJob> logger)
    {
        _outboxProcessor = outboxProcessor;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var result = await _outboxProcessor.CleanupAsync(cancellationToken);

        if (result.TotalDeleted > 0)
        {
            _logger.LogInformation(
                "Accounting outbox cleanup completed | DeletedProcessed: {DeletedProcessed} | DeletedFailed: {DeletedFailed}",
                result.ProcessedDeleted,
                result.FailedDeleted);
        }
        else
        {
            _logger.LogDebug("Accounting outbox cleanup found no expired messages to delete");
        }
    }
}
