namespace PointOfSale.API.Infrastructure.Queries;

public static class QuerySafetyGuard
{
    public const int MaxGovernanceSubjects = 2_000;
    public const int MaxVendorFinanceVendors = 2_000;
    public const int MaxVendorFinanceReferenceRows = 50_000;
    public const int MaxPayoutQueueVendors = 2_000;
    public const int MaxPayoutQueueSubOrders = 100_000;

    public static void EnsureWithinLimit(int count, int maxAllowed, string operationName)
    {
        if (count <= maxAllowed)
        {
            return;
        }

        throw new BadHttpRequestException(
            $"{operationName} is too broad to process safely. Narrow the filters and try again.");
    }
}
