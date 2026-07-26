namespace PointOfSale.Infrastructure.Jobs;

public sealed class CommerceHubReindexOptions
{
    public const string SectionName = "CommerceHubReindex";

    public int ParallelBatchSize { get; set; } = 8;
    public int SearchSyncStatusBatchSize { get; set; } = 250;
    public int InterBatchDelayMilliseconds { get; set; } = 100;

    public int BoundedParallelBatchSize => Math.Clamp(ParallelBatchSize, 1, 32);
    public int BoundedSearchSyncStatusBatchSize => Math.Clamp(SearchSyncStatusBatchSize, 1, 1000);
    public int BoundedInterBatchDelayMilliseconds => Math.Clamp(InterBatchDelayMilliseconds, 0, 60000);
}
