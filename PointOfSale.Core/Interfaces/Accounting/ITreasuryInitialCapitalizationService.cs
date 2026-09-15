using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Core.Interfaces.Accounting;

public sealed record TreasuryInitialCapitalizationAllocationCommand(Guid TreasuryAccountId, decimal Amount);

public sealed record CreateTreasuryInitialCapitalizationCommand(
    Guid OperationId,
    DateTime CutoverDate,
    IReadOnlyList<TreasuryInitialCapitalizationAllocationCommand> Allocations,
    string EvidenceReference,
    string? Notes,
    string DeclarationVersion,
    bool DeclarationAcknowledged,
    Guid? PredecessorCapitalizationId = null);

public sealed record ReverseTreasuryInitialCapitalizationCommand(
    Guid OperationId,
    string Reason,
    bool Confirmed);

public sealed record TreasuryInitialCapitalizationDestination(
    Guid TreasuryAccountId,
    string Name,
    string? NameAr,
    TreasuryAccountType Type,
    Guid LedgerAccountId,
    string LedgerAccountCode,
    string LedgerAccountName,
    decimal CurrentBalance);

public interface ITreasuryInitialCapitalizationService
{
    Task<IReadOnlyList<TreasuryInitialCapitalizationDestination>> GetEligibleDestinationsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TreasuryInitialCapitalization>> GetAllAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<TreasuryInitialCapitalization?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<TreasuryInitialCapitalization> CreateAndPostAsync(
        Guid tenantId,
        Guid ownerUserId,
        CreateTreasuryInitialCapitalizationCommand command,
        CancellationToken cancellationToken = default);

    Task<TreasuryInitialCapitalization> ReverseAsync(
        Guid tenantId,
        Guid ownerUserId,
        Guid capitalizationId,
        ReverseTreasuryInitialCapitalizationCommand command,
        CancellationToken cancellationToken = default);
}
