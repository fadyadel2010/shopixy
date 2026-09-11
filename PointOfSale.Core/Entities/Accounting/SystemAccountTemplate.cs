using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities.Accounting;

/// <summary>
/// Blueprint for seeding system accounts.
/// These templates define the baseline Chart of Accounts for every tenant.
/// 
/// NOT an entity - this is a value object/configuration used by the seeder.
/// </summary>
public class SystemAccountTemplate
{
    /// <summary>
    /// Account code (e.g., "1010", "4010").
    /// </summary>
    public required string Code { get; init; }
    
    /// <summary>
    /// Account name in English.
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Account name in Arabic (optional).
    /// </summary>
    public string? NameAr { get; init; }
    
    /// <summary>
    /// Account classification.
    /// </summary>
    public required AccountType Type { get; init; }
    
    /// <summary>
    /// If true, this is a clearing/transit account.
    /// </summary>
    public bool IsClearing { get; init; }
    
    /// <summary>
    /// If true, balance cannot go negative.
    /// </summary>
    public bool PreventNegative { get; init; }
    
    /// <summary>
    /// Parent account code (for hierarchy).
    /// </summary>
    public string? ParentCode { get; init; }
    
    /// <summary>
    /// Description of the account purpose.
    /// </summary>
    public string? Description { get; init; }
    
    /// <summary>
    /// For clearing accounts: the source type.
    /// </summary>
    public string? ClearingSourceType { get; init; }
    
    /// <summary>
    /// For clearing accounts: the provider code.
    /// </summary>
    public string? ClearingSourceCode { get; init; }
    
    /// <summary>
    /// Expected settlement days (for clearing accounts).
    /// </summary>
    public int? ExpectedSettlementDays { get; init; }
}

/// <summary>
/// Defines the baseline system accounts that every tenant must have.
/// These accounts are automatically seeded when a tenant is created.
/// </summary>
public static class SystemAccountTemplates
{
    // ═══════════════════════════════════════════════════════════════════════
    // ASSETS (1xxx)
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Parent account for cash and bank assets.
    /// </summary>
    public static readonly SystemAccountTemplate CashAndBank = new()
    {
        Code = "1000",
        Name = "Cash and Bank",
        NameAr = "النقد والبنك",
        Type = AccountType.Asset
    };
    
    /// <summary>
    /// Main cashbox for POS cash transactions.
    /// </summary>
    public static readonly SystemAccountTemplate CashboxMain = new()
    {
        Code = "1010",
        Name = "Cashbox Main",
        NameAr = "صندوق النقدية الرئيسي",
        Type = AccountType.Asset,
        PreventNegative = true,
        ParentCode = "1000",
        Description = "Main cash drawer for POS operations"
    };
    
    /// <summary>
    /// POS Visa/Card terminal account.
    /// </summary>
    public static readonly SystemAccountTemplate PosVisaAccount = new()
    {
        Code = "1020",
        Name = "POS Visa Account",
        NameAr = "حساب الفيزا نقطة البيع",
        Type = AccountType.Asset,
        ParentCode = "1000",
        Description = "POS card terminal transactions"
    };
    
    /// <summary>
    /// Bank account for deposits and settlements.
    /// </summary>
    public static readonly SystemAccountTemplate BankAccount = new()
    {
        Code = "1030",
        Name = "Bank Account",
        NameAr = "الحساب البنكي",
        Type = AccountType.Asset,
        PreventNegative = true,
        ParentCode = "1000",
        Description = "Primary bank account for deposits"
    };

    /// <summary>
    /// Cash float reserve account used to fund cashbox opening balances.
    /// </summary>
    public static readonly SystemAccountTemplate CashFloatReserve = new()
    {
        Code = "1040",
        Name = "Cash Float Reserve",
        NameAr = "احتياطي عهدة النقدية",
        Type = AccountType.Asset,
        PreventNegative = true,
        ParentCode = "1000",
        Description = "Reserve account used to fund cashbox opening floats"
    };
    
    // ═══════════════════════════════════════════════════════════════════════
    // CLEARING ACCOUNTS (11xx) - Money in transit
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Parent account for clearing accounts.
    /// </summary>
    public static readonly SystemAccountTemplate ClearingAccounts = new()
    {
        Code = "1100",
        Name = "Clearing Accounts",
        NameAr = "حسابات التسوية",
        Type = AccountType.Asset,
        Description = "Holding accounts for money in transit"
    };
    
    /// <summary>
    /// Visa/card clearing - awaiting settlement from processor.
    /// </summary>
    public static readonly SystemAccountTemplate VisaClearing = new()
    {
        Code = "1110",
        Name = "Visa Clearing",
        NameAr = "تسوية الفيزا",
        Type = AccountType.Asset,
        IsClearing = true,
        ParentCode = "1100",
        ClearingSourceType = "Gateway",
        ClearingSourceCode = "visa",
        ExpectedSettlementDays = 3,
        Description = "Card payments pending bank settlement"
    };
    
    /// <summary>
    /// Stripe clearing - awaiting payout.
    /// </summary>
    public static readonly SystemAccountTemplate StripeClearing = new()
    {
        Code = "1120",
        Name = "Stripe Clearing",
        NameAr = "تسوية سترايب",
        Type = AccountType.Asset,
        IsClearing = true,
        ParentCode = "1100",
        ClearingSourceType = "Gateway",
        ClearingSourceCode = "stripe",
        ExpectedSettlementDays = 7,
        Description = "Stripe payments pending payout"
    };
    
    /// <summary>
    /// COD clearing - awaiting carrier settlement.
    /// </summary>
    public static readonly SystemAccountTemplate CodClearing = new()
    {
        Code = "1130",
        Name = "COD Clearing",
        NameAr = "تسوية الدفع عند الاستلام",
        Type = AccountType.Asset,
        IsClearing = true,
        ParentCode = "1100",
        ClearingSourceType = "Carrier",
        ClearingSourceCode = "cod",
        ExpectedSettlementDays = 14,
        Description = "Cash on delivery pending carrier remittance"
    };
    
    /// <summary>
    /// Wallet clearing - awaiting wallet provider settlement.
    /// </summary>
    public static readonly SystemAccountTemplate WalletClearing = new()
    {
        Code = "1140",
        Name = "Wallet Clearing",
        NameAr = "تسوية المحفظة الإلكترونية",
        Type = AccountType.Asset,
        IsClearing = true,
        ParentCode = "1100",
        ClearingSourceType = "Wallet",
        ClearingSourceCode = "wallet",
        ExpectedSettlementDays = 3,
        Description = "Mobile wallet payments pending settlement"
    };
    
    // ═══════════════════════════════════════════════════════════════════════
    // INVENTORY (13xx)
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Inventory asset account.
    /// </summary>
    public static readonly SystemAccountTemplate Inventory = new()
    {
        Code = "1310",
        Name = "Inventory",
        NameAr = "المخزون",
        Type = AccountType.Asset,
        Description = "Value of goods held for sale"
    };

    public static readonly SystemAccountTemplate InputTaxReceivable = new()
    {
        Code = "1320",
        Name = "Input Tax Receivable",
        NameAr = "ضريبة المدخلات المستحقة",
        Type = AccountType.Asset,
        Description = "Recoverable tax paid on purchases"
    };
    
    // ═══════════════════════════════════════════════════════════════════════
    // RECEIVABLES (14xx)
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Accounts receivable - customer credit.
    /// </summary>
    public static readonly SystemAccountTemplate AccountsReceivable = new()
    {
        Code = "1410",
        Name = "Accounts Receivable",
        NameAr = "المدينون",
        Type = AccountType.Asset,
        Description = "Amounts owed by customers"
    };

    /// <summary>
    /// CommerceHub vendor receivable.
    /// Tracks debt owed by marketplace vendors back to the platform.
    /// </summary>
    public static readonly SystemAccountTemplate MarketplaceVendorReceivable = new()
    {
        Code = "1420",
        Name = "Marketplace Vendor Receivable",
        NameAr = "مديونية بائعي السوق الإلكتروني",
        Type = AccountType.Asset,
        Description = "Amounts owed by marketplace vendors back to the platform"
    };

    /// <summary>
    /// Offset account used for manual customer receivable adjustments.
    /// </summary>
    public static readonly SystemAccountTemplate AccountsReceivableAdjustments = new()
    {
        Code = "6120",
        Name = "Accounts Receivable Adjustments",
        NameAr = "تسويات المدينون",
        Type = AccountType.Expense,
        ParentCode = "6100",
        Description = "Offset account for manual customer receivable adjustments"
    };
    
    // ═══════════════════════════════════════════════════════════════════════
    // LIABILITIES (2xxx)
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Supplier payables.
    /// </summary>
    public static readonly SystemAccountTemplate SupplierPayables = new()
    {
        Code = "2010",
        Name = "Supplier Payables",
        NameAr = "الدائنون الموردين",
        Type = AccountType.Liability,
        Description = "Amounts owed to suppliers"
    };

    /// <summary>
    /// Offset account used for manual supplier payable adjustments.
    /// </summary>
    public static readonly SystemAccountTemplate AccountsPayableAdjustments = new()
    {
        Code = "6130",
        Name = "Accounts Payable Adjustments",
        NameAr = "تسويات الدائنون",
        Type = AccountType.Expense,
        ParentCode = "6100",
        Description = "Offset account for manual supplier payable adjustments"
    };
    
    /// <summary>
    /// Customer deposits/prepayments.
    /// </summary>
    public static readonly SystemAccountTemplate CustomerDeposits = new()
    {
        Code = "2020",
        Name = "Customer Deposits",
        NameAr = "ودائع العملاء",
        Type = AccountType.Liability,
        Description = "Prepayments received from customers"
    };

    /// <summary>
    /// Suspense account used for manual treasury adjustments until they are classified.
    /// </summary>
    public static readonly SystemAccountTemplate TreasurySuspense = new()
    {
        Code = "2030",
        Name = "Treasury Suspense",
        NameAr = "حساب تعليق الخزينة",
        Type = AccountType.Liability,
        Description = "Temporary offset account for manual treasury adjustments"
    };

    public static readonly SystemAccountTemplate SalesTaxPayable = new()
    {
        Code = "2050",
        Name = "Sales Tax Payable",
        NameAr = "ضريبة المبيعات المستحقة",
        Type = AccountType.Liability,
        Description = "Tax collected from customers and owed to the tax authority"
    };
    
    /// <summary>
    /// AF-14: Affiliate commission payable — liability for earned but unpaid commission.
    /// AF-14C: PreventNegative ensures payable can never go below zero.
    /// </summary>
    public static readonly SystemAccountTemplate AffiliatePayable = new()
    {
        Code = "2105",
        Name = "Affiliate Payable",
        NameAr = "مستحقات المسوقين بالعمولة",
        Type = AccountType.Liability,
        PreventNegative = true,
        Description = "Commission earned by affiliates, pending payment"
    };

    /// <summary>
    /// CommerceHub marketplace vendor payable.
    /// Tracks earnings owed to marketplace vendors (net of commission) pending payout.
    /// PreventNegative ensures the balance can never go below zero — a negative balance
    /// would indicate an over-disbursement and signals a data integrity problem.
    /// </summary>
    public static readonly SystemAccountTemplate MarketplaceVendorPayable = new()
    {
        Code = "2040",
        Name = "Marketplace Vendor Payable",
        NameAr = "مستحقات بائعي السوق الإلكتروني",
        Type = AccountType.Liability,
        PreventNegative = true,
        Description = "Vendor earnings (net of platform commission) awaiting payout in the CommerceHub marketplace"
    };

    /// <summary>
    /// Offset account used when importing customer/supplier opening balances.
    /// </summary>
    public static readonly SystemAccountTemplate OpeningBalanceEquity = new()
    {
        Code = "3010",
        Name = "Opening Balance Equity",
        NameAr = "رصيد الافتتاح",
        Type = AccountType.Equity,
        Description = "Equity offset account for imported opening balances"
    };
    
    // ═══════════════════════════════════════════════════════════════════════
    // REVENUE (4xxx)
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// POS sales revenue.
    /// </summary>
    public static readonly SystemAccountTemplate PosSales = new()
    {
        Code = "4010",
        Name = "POS Sales",
        NameAr = "مبيعات نقطة البيع",
        Type = AccountType.Revenue,
        Description = "Revenue from point of sale transactions"
    };
    
    /// <summary>
    /// E-commerce sales revenue.
    /// </summary>
    public static readonly SystemAccountTemplate EcommerceSales = new()
    {
        Code = "4020",
        Name = "E-commerce Sales",
        NameAr = "مبيعات التجارة الإلكترونية",
        Type = AccountType.Revenue,
        Description = "Revenue from online store"
    };
    
    /// <summary>
    /// Shipping revenue (charged to customers).
    /// </summary>
    public static readonly SystemAccountTemplate ShippingRevenue = new()
    {
        Code = "4030",
        Name = "Shipping Revenue",
        NameAr = "إيرادات الشحن",
        Type = AccountType.Revenue,
        Description = "Shipping fees collected from customers"
    };
    
    // ═══════════════════════════════════════════════════════════════════════
    // COST OF GOODS SOLD (5xxx)
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Cost of goods sold.
    /// </summary>
    public static readonly SystemAccountTemplate CostOfGoodsSold = new()
    {
        Code = "5010",
        Name = "Cost of Goods Sold",
        NameAr = "تكلفة البضاعة المباعة",
        Type = AccountType.CostOfGoodsSold,
        Description = "Direct cost of products sold"
    };
    
    // ═══════════════════════════════════════════════════════════════════════
    // EXPENSES (6xxx)
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Payment gateway fees.
    /// </summary>
    public static readonly SystemAccountTemplate GatewayFees = new()
    {
        Code = "6010",
        Name = "Gateway Fees",
        NameAr = "رسوم بوابة الدفع",
        Type = AccountType.Expense,
        Description = "Fees charged by payment processors"
    };
    
    /// <summary>
    /// Shipping costs.
    /// </summary>
    public static readonly SystemAccountTemplate ShippingFees = new()
    {
        Code = "6020",
        Name = "Shipping Fees",
        NameAr = "رسوم الشحن",
        Type = AccountType.Expense,
        Description = "Delivery and shipping costs"
    };
    
    /// <summary>
    /// Bank fees.
    /// </summary>
    public static readonly SystemAccountTemplate BankFees = new()
    {
        Code = "6030",
        Name = "Bank Fees",
        NameAr = "رسوم البنك",
        Type = AccountType.Expense,
        Description = "Bank service charges and fees"
    };
    
    /// <summary>
    /// General business expenses.
    /// </summary>
    public static readonly SystemAccountTemplate GeneralExpenses = new()
    {
        Code = "6100",
        Name = "General Expenses",
        NameAr = "مصروفات عامة",
        Type = AccountType.Expense,
        Description = "General business expenses"
    };
    
    /// <summary>
    /// AF-14: Affiliate commission expense — recognized when commission is earned.
    /// </summary>
    public static readonly SystemAccountTemplate AffiliateMarketingExpense = new()
    {
        Code = "6110",
        Name = "Affiliate Marketing Expense",
        NameAr = "مصروفات التسويق بالعمولة",
        Type = AccountType.Expense,
        ParentCode = "6100",
        Description = "Commission expense for affiliate referrals"
    };
    
    // ═══════════════════════════════════════════════════════════════════════
    // ALL TEMPLATES (for iteration)
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// All system account templates in creation order.
    /// Parent accounts must be created before children.
    /// </summary>
    public static readonly IReadOnlyList<SystemAccountTemplate> All = new[]
    {
        // Assets - Level 1
        CashAndBank,
        ClearingAccounts,
        
        // Assets - Level 2
        CashboxMain,
        PosVisaAccount,
        BankAccount,
        CashFloatReserve,
        VisaClearing,
        StripeClearing,
        CodClearing,
        WalletClearing,
        
        // Other Assets
        Inventory,
        InputTaxReceivable,
        AccountsReceivable,
        MarketplaceVendorReceivable,
        
        // Liabilities
        SupplierPayables,
        CustomerDeposits,
        TreasurySuspense,
        MarketplaceVendorPayable,      // CommerceHub: vendor payable
        SalesTaxPayable,
        AffiliatePayable,              // AF-14

        // Equity
        OpeningBalanceEquity,

        // Revenue
        PosSales,
        EcommerceSales,
        ShippingRevenue,
        
        // COGS
        CostOfGoodsSold,
        
        // Expenses
        GatewayFees,
        ShippingFees,
        BankFees,
        GeneralExpenses,           // Parent for 6110
        AccountsReceivableAdjustments,
        AccountsPayableAdjustments,
        AffiliateMarketingExpense  // AF-14
    };
}
