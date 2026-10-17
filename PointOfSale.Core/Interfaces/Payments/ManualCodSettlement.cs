namespace PointOfSale.Core.Interfaces.Payments;

/// <summary>Operator selection only. Money, currency, destination and actor are server authority.</summary>
public sealed record ManualCodSettlementRequest(Guid SettlementId, IReadOnlyList<Guid> OrderIds);

public sealed record ManualCodSettlementResult(
    Guid BatchId, Guid SettlementId, string Status, int SettledOrderCount,
    decimal GrossAmount, string Currency, Guid DestinationAccountId,
    string DestinationAccountCode, string DestinationAccountName,
    Guid JournalId, string JournalReference, string AccountingOutcome, bool WasIdempotent);

public sealed record ManualCodSettlementOrder(Guid OrderId, string OrderReference, decimal Amount,
    string Currency, string CollectionStatus, bool Eligible);

public sealed record ManualCodSettlementSelection(IReadOnlyList<ManualCodSettlementOrder> Orders,
    string Currency, Guid DestinationAccountId, string DestinationAccountCode,
    string DestinationAccountName, int Page, bool HasMore);
