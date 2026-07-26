namespace PointOfSale.API.SuperAdmin.Models;

public sealed class SuperAdminLoginAttemptResult
{
    public int FailureCount { get; init; }
    public int DistinctEmailCount { get; init; }
    public bool IsSuspicious { get; init; }
}
