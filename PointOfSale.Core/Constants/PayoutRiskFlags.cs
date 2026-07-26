namespace PointOfSale.Core.Constants;

/// <summary>
/// Payout risk flag constants used in vendor payout processing.
/// 
/// ARCHITECTURAL RULE:
/// - All risk flag strings MUST be defined here as constants
/// - NO magic strings allowed in code
/// - Any new risk flag must be added here first
/// 
/// These flags indicate data authority conflicts or currency mismatches
/// that block automatic payout processing and require manual review.
/// </summary>
public static class PayoutRiskFlags
{
    /// <summary>
    /// Payout is blocked due to a data authority conflict.
    /// This is the primary blocking flag that indicates manual review is required.
    /// </summary>
    public const string PayoutBlockedByAuthorityConflict = "PayoutBlockedByAuthorityConflict";
    
    /// <summary>
    /// Ledger contains transactions in multiple currencies for this vendor.
    /// This indicates a currency split that requires manual reconciliation.
    /// </summary>
    public const string LedgerCurrencySplit = "LedgerCurrencySplit";
    
    /// <summary>
    /// Vendor's operational currency doesn't match the authority currency.
    /// Sub-orders have currency mismatches that need resolution.
    /// </summary>
    public const string OperationalCurrencyMismatch = "OperationalCurrencyMismatch";
    
    /// <summary>
    /// Payout transactions have currency mismatches.
    /// Payout rows don't align with the authority currency.
    /// </summary>
    public const string PayoutCurrencyMismatch = "PayoutCurrencyMismatch";
    
    /// <summary>
    /// Refund transactions have currency mismatches.
    /// Refund rows don't align with the authority currency.
    /// </summary>
    public const string RefundCurrencyMismatch = "RefundCurrencyMismatch";

    /// <summary>
    /// A previously handed-off payout is quarantined in reconciliation-required state.
    /// New outward money authority for the vendor must remain blocked until that payout is resolved.
    /// </summary>
    public const string ExternalTransferReconciliationRequired = "ExternalTransferReconciliationRequired";

    /// <summary>
    /// A payout has remained in Processing beyond the safe reconciliation window.
    /// New outward money authority for the vendor must remain blocked until it is quarantined and resolved.
    /// </summary>
    public const string StaleExternalTransferProcessing = "StaleExternalTransferProcessing";
    
    /// <summary>
    /// Backing set for authority-blocking risk flags.
    /// </summary>
    private static readonly HashSet<string> _authorityBlockingFlags = new(StringComparer.Ordinal)
    {
        PayoutBlockedByAuthorityConflict,
        LedgerCurrencySplit,
        OperationalCurrencyMismatch,
        PayoutCurrencyMismatch,
        RefundCurrencyMismatch,
        ExternalTransferReconciliationRequired,
        StaleExternalTransferProcessing
    };
    
    /// <summary>
    /// All authority-blocking risk flags that prevent automatic payout processing.
    /// If any of these flags are present, the payout requires manual review.
    /// </summary>
    public static IReadOnlyCollection<string> AuthorityBlockingFlags => 
        Array.AsReadOnly(_authorityBlockingFlags.ToArray());
    
    /// <summary>
    /// Checks if a risk flag is an authority-blocking risk that prevents automatic payout.
    /// </summary>
    public static bool IsAuthorityBlockingRisk(string riskFlag)
    {
        return _authorityBlockingFlags.Contains(riskFlag);
    }
}
