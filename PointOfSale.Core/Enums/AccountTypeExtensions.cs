namespace PointOfSale.Core.Enums;

/// <summary>
/// Extension methods for AccountType enum.
/// </summary>
public static class AccountTypeExtensions
{
    /// <summary>
    /// Gets the normal balance (debit or credit) for an account type.
    /// Asset, Expense, COGS = Debit normal
    /// Liability, Equity, Revenue = Credit normal
    /// </summary>
    public static NormalBalance GetNormalBalance(this AccountType accountType)
    {
        return accountType switch
        {
            AccountType.Asset => NormalBalance.Debit,
            AccountType.Expense => NormalBalance.Debit,
            AccountType.CostOfGoodsSold => NormalBalance.Debit,
            AccountType.Liability => NormalBalance.Credit,
            AccountType.Equity => NormalBalance.Credit,
            AccountType.Revenue => NormalBalance.Credit,
            _ => throw new InvalidOperationException($"Unknown AccountType: {accountType}")
        };
    }
    
    /// <summary>
    /// Returns true if this account type has a debit normal balance.
    /// </summary>
    public static bool IsDebitNormal(this AccountType accountType)
    {
        return accountType.GetNormalBalance() == NormalBalance.Debit;
    }
    
    /// <summary>
    /// Returns true if this account type has a credit normal balance.
    /// </summary>
    public static bool IsCreditNormal(this AccountType accountType)
    {
        return accountType.GetNormalBalance() == NormalBalance.Credit;
    }
}
