using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services;

public interface IFinancialIntegrityService
{
    Task<PartyBalanceReconciliationResult> ReconcileHistoricalBalancesAsync(
        bool dryRun = true,
        CancellationToken cancellationToken = default);

    Task<FinancialAuditValidationResult> ValidateAsync(
        CancellationToken cancellationToken = default);
}

public sealed record PartyBalanceReconciliationResult(
    bool DryRun,
    int TotalPartiesScanned,
    int MismatchCount,
    int CorrectedCount,
    IReadOnlyList<PartyBalanceReconciliationItem> Items);

public sealed record PartyBalanceReconciliationItem(
    PartyType PartyType,
    Guid PartyId,
    string PartyName,
    decimal StoredBalance,
    decimal LedgerBalance,
    bool WasMismatch,
    bool WasCorrected);

public sealed record FinancialAuditValidationResult(
    bool IsHealthy,
    FinancialAuditValidationSummary Summary,
    IReadOnlyList<FinancialAuditIssue> Issues);

public sealed record FinancialAuditValidationSummary(
    int TotalIssues,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<FinancialAuditCheckResult> Checks);

public sealed record FinancialAuditCheckResult(
    string Name,
    bool Passed,
    int IssueCount);

public sealed record FinancialAuditIssue(
    string Code,
    string Severity,
    string Message,
    string EntityType,
    string? EntityId = null);
