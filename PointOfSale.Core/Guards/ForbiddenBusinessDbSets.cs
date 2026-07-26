namespace PointOfSale.Core.Guards;

/// <summary>
/// PHASE C3: SOURCE ENFORCEMENT GUARDS
/// 
/// Defines the forbidden business table DbSets that Ledger-Only services
/// must NEVER access.
/// 
/// This list is used by architecture tests to enforce that financial
/// reporting services read exclusively from the immutable ledger.
/// </summary>
public static class ForbiddenBusinessDbSets
{
    /// <summary>
    /// Business table DbSet names that are forbidden for ILedgerOnlyService implementations.
    /// </summary>
    public static readonly string[] Names =
    {
        // Sales & Revenue
        "SalesInvoices",
        "SalesInvoiceItems",
        "SalesInvoicePayments",
        
        // Purchases & Costs
        "PurchaseInvoices",
        "PurchaseInvoiceItems",
        "PurchaseInvoicePayments",
        
        // Returns
        "Returns",
        "ReturnItems",
        
        // Parties
        "Customers",
        "Suppliers",
        
        // Products & Inventory
        "Products",
        "ProductVariants",
        "WarehouseStocks",
        "StockMovements",
        "WarehouseTransfers",
        
        // Treasury/Cashbox (business operations, not ledger)
        "CashboxTransactions",
        "VisaTransactions",
        "Cashboxes",
        
        // Party Transactions (these are business records, not ledger)
        "CustomerTransactions",
        "SupplierTransactions",
        
        // Financial Transactions (legacy - not ledger)
        "FinancialTransactions",
        "FinancialAccounts"
    };

    /// <summary>
    /// Allowed ledger DbSet names for ILedgerOnlyService implementations.
    /// </summary>
    public static readonly string[] AllowedLedgerDbSets =
    {
        "Accounts",
        "JournalEntries",
        "LedgerEntries"
    };
}
