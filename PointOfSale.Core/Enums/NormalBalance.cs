namespace PointOfSale.Core.Enums;

/// <summary>
/// The side of the T-account where increases are recorded.
/// Determines how balance is calculated from ledger entries.
/// </summary>
public enum NormalBalance
{
    /// <summary>
    /// Balance increases with debits, decreases with credits.
    /// Used by: Asset, Expense, COGS.
    /// </summary>
    Debit = 1,
    
    /// <summary>
    /// Balance increases with credits, decreases with debits.
    /// Used by: Liability, Equity, Revenue.
    /// </summary>
    Credit = 2
}
