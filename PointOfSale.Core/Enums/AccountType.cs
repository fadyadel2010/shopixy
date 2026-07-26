namespace PointOfSale.Core.Enums;

/// <summary>
/// Chart of Accounts - Account Types for proper accounting classification
/// </summary>
public enum AccountType
{
    /// <summary>
    /// Assets - Resources owned by the business (Cash, Bank, Inventory, Equipment)
    /// Normal balance: Debit
    /// </summary>
    Asset = 1,
    
    /// <summary>
    /// Liabilities - Debts owed by the business (Accounts Payable, Loans)
    /// Normal balance: Credit
    /// </summary>
    Liability = 2,
    
    /// <summary>
    /// Equity - Owner's investment and retained earnings
    /// Normal balance: Credit
    /// </summary>
    Equity = 3,
    
    /// <summary>
    /// Revenue - Income from sales and services
    /// Normal balance: Credit
    /// </summary>
    Revenue = 4,
    
    /// <summary>
    /// Expense - Costs incurred in business operations
    /// Normal balance: Debit
    /// </summary>
    Expense = 5,
    
    /// <summary>
    /// Cost of Goods Sold - Direct costs of products sold
    /// Normal balance: Debit
    /// </summary>
    CostOfGoodsSold = 6
}
