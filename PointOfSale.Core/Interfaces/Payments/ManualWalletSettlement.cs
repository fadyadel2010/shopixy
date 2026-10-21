namespace PointOfSale.Core.Interfaces.Payments;

// Whole accepted payments only. No caller-authored money, tenant, accounts or state.
public sealed record ManualWalletSettlementRequest(Guid SettlementId, IReadOnlyList<Guid> OrderIds, string PreviewToken);
public sealed record ManualWalletSettlementPayment(Guid OrderId, string OrderReference, Guid PaymentIntentId,
    string PaymentReference, decimal Amount, decimal RemainingAmount, string Currency, DateTime ApprovedAtUtc,
    bool Eligible, string? BlockedReason, string? CustomerDisplayName = null);
public sealed record ManualWalletSettlementSelection(IReadOnlyList<ManualWalletSettlementPayment> Payments,
    string Currency, Guid DestinationAccountId, string DestinationAccountCode, string DestinationAccountName,
    Guid SourceAccountId, string SourceAccountCode, decimal CashboxBalance, decimal ClearingBalance, int Page, bool HasMore);
public sealed record ManualWalletSettlementPreview(IReadOnlyList<ManualWalletSettlementPayment> Payments,
    decimal GrossAmount, string Currency, string SourceAccountCode, string DestinationAccountCode, string PreviewToken);
public sealed record ManualWalletSettlementResult(Guid BatchId, Guid SettlementId, string Status, int SettledOrderCount,
    decimal GrossAmount, string Currency, Guid DestinationAccountId, string DestinationAccountCode,
    string DestinationAccountName, Guid JournalId, string JournalReference, string AccountingOutcome, bool WasIdempotent);
