using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Core.Interfaces.Accounting;

/// <summary>
/// Resolves ledger accounts by code for a tenant.
/// Used by CommerceAccountingService to get account IDs for posting.
/// </summary>
public interface IAccountResolver
{
    /// <summary>
    /// Gets an account by its code.
    /// Throws if account not found.
    /// </summary>
    Task<Account> GetByCodeAsync(Guid tenantId, string accountCode, CancellationToken ct = default);
    
    /// <summary>
    /// Gets an account by code, or null if not found.
    /// </summary>
    Task<Account?> GetByCodeOrDefaultAsync(Guid tenantId, string accountCode, CancellationToken ct = default);
    
    /// <summary>
    /// Gets multiple accounts by codes.
    /// Throws if any account not found.
    /// </summary>
    Task<Dictionary<string, Account>> GetByCodesAsync(Guid tenantId, IEnumerable<string> accountCodes, CancellationToken ct = default);
    
    /// <summary>
    /// Gets the target or clearing account for a payment method.
    /// Returns TargetAccountId if set, otherwise ClearingAccountId.
    /// </summary>
    Task<Guid> GetPaymentAccountIdAsync(Guid tenantId, string paymentMethodCode, CancellationToken ct = default);
    
    /// <summary>
    /// Gets the fees account for a payment method.
    /// Returns null if no fees account configured.
    /// </summary>
    Task<Guid?> GetFeesAccountIdAsync(Guid tenantId, string paymentMethodCode, CancellationToken ct = default);
}

/// <summary>
/// Standard account codes for the system.
/// </summary>
public static class AccountCodes
{
    // Assets
    public const string CashboxMain = "1010";
    public const string PosVisaAccount = "1020";
    public const string BankAccount = "1030";
    public const string CashFloatReserve = "1040";
    public const string VisaClearing = "1110";
    public const string StripeClearing = "1120";
    public const string CodClearing = "1130";
    public const string WalletClearing = "1140";
    public const string Inventory = "1310";
    public const string InputTaxReceivable = "1320";
    public const string AccountsReceivable = "1410";
    public const string MarketplaceVendorReceivable = "1420"; // CommerceHub: vendor debt owed back to the platform
    
    // Liabilities
    public const string SupplierPayables = "2010";
    public const string CustomerDeposits = "2020";
    public const string TreasurySuspense = "2030";
    public const string MarketplaceVendorPayable = "2040"; // CommerceHub: vendor payable (earnings owed to vendors)
    public const string SalesTaxPayable = "2050";
    public const string AffiliatePayable = "2105";  // AF-14: Affiliate commission payable

    // Equity
    public const string OpeningBalanceEquity = "3010";

    // Revenue
    public const string PosSales = "4010";
    public const string EcommerceSales = "4020";
    public const string ShippingRevenue = "4030";
    
    // COGS
    public const string CostOfGoodsSold = "5010";
    
    // Expenses
    public const string GatewayFees = "6010";
    public const string ShippingFees = "6020";
    public const string BankFees = "6030";
    public const string Expenses = "6100";  // PHASE LCR: General expenses account
    public const string AffiliateMarketingExpense = "6110";  // AF-14: Affiliate commission expense
    public const string AccountsReceivableAdjustments = "6120";
    public const string AccountsPayableAdjustments = "6130";
}
