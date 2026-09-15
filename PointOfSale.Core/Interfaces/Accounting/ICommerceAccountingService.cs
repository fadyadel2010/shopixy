using PointOfSale.Core.Entities;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Core.Models.Refunds;

namespace PointOfSale.Core.Interfaces.Accounting;

/// <summary>
/// Commerce-to-Ledger integration service.
/// Translates POS and E-commerce operations into journal entries.
/// 
/// PHASE 3: This service bridges business operations with the financial ledger.
/// All methods are idempotent via unique idempotency keys.
/// </summary>
public interface ICommerceAccountingService
{
    // ═══════════════════════════════════════════════════════════════════════════
    // POS OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Posts journal entries for a POS sale.
    /// 
    /// Creates TWO journals:
    /// 1. Revenue Journal: DEBIT Cash/Card/Clearing, CREDIT Sales
    /// 2. COGS Journal: DEBIT COGS, CREDIT Inventory
    /// 
    /// Idempotency: SALE:{TenantId}:{InvoiceId}
    /// Updated-sale reapplications append the operation key so each update
    /// creates a distinct, replay-safe journal set after reversal.
    /// </summary>
    /// <param name="operationKey">
    /// Optional operation key to append to the idempotency key for distinct operations.
    /// Must be null or a non-empty, non-whitespace string. Empty/whitespace strings
    /// will throw ArgumentException to prevent silent key reuse.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when operationKey is empty or whitespace.</exception>
    Task<PostingResult> PostPosSaleAsync(
        SalesInvoice invoice,
        CancellationToken ct = default,
        string? operationKey = null);
    
    /// <summary>
    /// Posts journal entry for a POS purchase (from supplier).
    /// 
    /// Creates:
    /// - DEBIT Inventory
    /// - CREDIT Cash/Card treasury accounts for paid portions
    /// - CREDIT Supplier Payables for any unpaid remainder
    /// 
    /// Idempotency: PURCHASE:{TenantId}:{InvoiceId}
    /// Updated-purchase reapplications append the operation key so each mutation
    /// creates a distinct, replay-safe journal after prior postings are reversed.
    /// </summary>
    /// <param name="cashAmount">
    /// The cash portion of the payment. Null means "unspecified" (not zero).
    /// When null, the implementation will infer the cash amount from invoice data or other sources.
    /// Pass 0 explicitly to indicate no cash payment was made.
    /// Null vs. zero distinction: null = "let the system determine", 0 = "definitely no cash payment".
    /// 
    /// IMPORTANT: Some ICommerceAccountingService implementations may require explicit cash/card splits
    /// for certain payment modes (e.g., Mixed or Credit payments) and will reject or throw if values
    /// are not provided. Callers should provide explicit amounts (including zero for no-payment) rather
    /// than null when handling mixed or store-credit payment scenarios.
    /// </param>
    /// <param name="cardAmount">
    /// The card portion of the payment. Null means "unspecified" (not zero).
    /// When null, the implementation will infer the card amount from invoice data or other sources.
    /// Pass 0 explicitly to indicate no card payment was made.
    /// Null vs. zero distinction: null = "let the system determine", 0 = "definitely no card payment".
    /// 
    /// IMPORTANT: Some ICommerceAccountingService implementations may require explicit cash/card splits
    /// for certain payment modes (e.g., Mixed or Credit payments) and will reject or throw if values
    /// are not provided. Callers should provide explicit amounts (including zero for no-payment) rather
    /// than null when handling mixed or store-credit payment scenarios.
    /// </param>
    Task<PostingResult> PostPosPurchaseAsync(
        PurchaseInvoice invoice,
        CancellationToken ct = default,
        string? operationKey = null,
        decimal? cashAmount = null,
        decimal? cardAmount = null,
        Guid? fundingInstrumentId = null,
        Guid? fundingLedgerAccountId = null,
        int? fundingInstrumentVersion = null);
    
    /// <summary>
    /// Posts journal entry for supplier payment.
    /// 
    /// DEBIT Supplier Payables
    /// CREDIT Cash/Bank
    /// 
    /// Idempotency: SUPPLIER_PAYMENT:{TenantId}:{PaymentId}
    /// </summary>
    Task<PostingResult> PostSupplierPaymentAsync(
        SupplierPayment payment,
        CancellationToken ct = default);

    /// <summary>
    /// Posts a customer account payment by debiting received funds and crediting receivables.
    /// </summary>
    Task<PostingResult> PostCustomerPaymentAsync(
        CustomerPayment payment,
        CancellationToken ct = default);

    /// <summary>
    /// Posts journal entry for a supplier opening balance import.
    ///
    /// Debit opening balance:
    /// - DR Supplier Payables
    /// - CR Opening Balance Equity
    ///
    /// Credit opening balance:
    /// - DR Opening Balance Equity
    /// - CR Supplier Payables
    ///
    /// Idempotency: SUPPLIER_OPENING_BALANCE:{TenantId}:{OpeningBalanceId}
    /// </summary>
    Task<PostingResult> PostSupplierOpeningBalanceAsync(
        AccountLedger openingBalance,
        CancellationToken ct = default);

    /// <summary>
    /// Posts journal entry for a customer opening balance import.
    ///
    /// Debit opening balance:
    /// - DR Accounts Receivable
    /// - CR Opening Balance Equity
    ///
    /// Credit opening balance:
    /// - DR Opening Balance Equity
    /// - CR Accounts Receivable
    ///
    /// Idempotency: CUSTOMER_OPENING_BALANCE:{TenantId}:{OpeningBalanceId}
    /// </summary>
    Task<PostingResult> PostCustomerOpeningBalanceAsync(
        AccountLedger openingBalance,
        CancellationToken ct = default);

    /// <summary>
    /// Posts journal entry for a manual party ledger adjustment.
    ///
    /// Customer adjustments:
    /// - Debit note / debit adjustment: DR Accounts Receivable, CR Accounts Receivable Adjustments
    /// - Credit note / credit adjustment: DR Accounts Receivable Adjustments, CR Accounts Receivable
    ///
    /// Supplier adjustments:
    /// - Debit note / debit adjustment: DR Supplier Payables, CR Accounts Payable Adjustments
    /// - Credit note / credit adjustment: DR Accounts Payable Adjustments, CR Supplier Payables
    ///
    /// Idempotency: PARTY_ADJUSTMENT:{TenantId}:{AdjustmentId}
    /// </summary>
    Task<PostingResult> PostPartyLedgerAdjustmentAsync(
        PartyLedgerAdjustment adjustment,
        CancellationToken ct = default);
    
    /// <summary>
    /// Posts journal entry for a POS refund (customer return).
    /// Creates a reversal of the original sale.
    /// 
    /// Idempotency: REFUND:{TenantId}:{ReturnId}
    /// </summary>
    Task<PostingResult> PostPosRefundAsync(
        Return returnObj,
        RefundAllocation allocation,
        CancellationToken ct = default);

    /// <summary>
    /// Posts journal entry for a purchase return.
    ///
    /// DEBIT Supplier Payables
    /// CREDIT Inventory
    ///
    /// Idempotency: PURCHASE_RETURN:{TenantId}:{ReturnId}
    /// </summary>
    Task<PostingResult> PostPurchaseReturnAsync(
        Return returnObj,
        CancellationToken ct = default);
    
    /// <summary>
    /// Posts journal entry for a POS expense.
    /// PHASE LCR: Added for expense ledger posting.
    /// 
    /// DEBIT Expense Category Account
    /// CREDIT Cash/Bank (based on payment method)
    /// 
    /// Idempotency: EXPENSE:{TenantId}:{ExpenseId}
    /// Updated or re-approved expense reapplications append the operation key
    /// so each mutation creates a distinct journal after prior postings are reversed.
    /// </summary>
    Task<PostingResult> PostPosExpenseAsync(
        Expense expense,
        CancellationToken ct = default,
        string? operationKey = null);
    
    // ═══════════════════════════════════════════════════════════════════════════
    // E-COMMERCE OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Posts journal entry when online payment is captured (Stripe/Paymob).
    /// 
    /// DEBIT {Gateway} Clearing
    /// CREDIT E-commerce Sales
    /// 
    /// Idempotency: ONLINE_PAYMENT:{TenantId}:{OrderId}:{PaymentIntentId}
    /// </summary>
    Task<PostingResult> PostOnlinePaymentCapturedAsync(
        Order order,
        string paymentIntentId,
        string gateway,
        CancellationToken ct = default);
    
    /// <summary>
    /// Posts journal entry when COD order is delivered.
    /// Cash is collected by courier.
    /// 
    /// DEBIT COD Clearing
    /// CREDIT E-commerce Sales
    /// 
    /// Idempotency: COD_DELIVERED:{TenantId}:{OrderId}
    /// </summary>
    Task<PostingResult> PostCodDeliveredAsync(
        Order order,
        CancellationToken ct = default);
    
    /// <summary>
    /// Posts journal entry when wallet payment is approved (Vodafone Cash, etc.).
    /// 
    /// DEBIT Wallet Clearing
    /// CREDIT E-commerce Sales
    /// 
    /// Idempotency: WALLET_APPROVED:{TenantId}:{OrderId}
    /// </summary>
    Task<PostingResult> PostWalletApprovedAsync(
        Order order,
        CancellationToken ct = default);
    
    /// <summary>
    /// Posts journal entry for e-commerce refund.
    /// Reverses the original payment journal.
    /// 
    /// Idempotency: ECOM_REFUND:{TenantId}:{OrderId}:{RefundId}
    /// </summary>
    Task<PostingResult> PostEcommerceRefundAsync(
        Order order,
        decimal refundAmount,
        string refundId,
        CancellationToken ct = default);

    Task<PostingResult> PostEcommerceRefundAsync(
        Order order,
        decimal refundAmount,
        string refundId,
        EcommerceRefundAccountingContext? refundContext,
        CancellationToken ct = default);
    
    /// <summary>
    /// Posts journal entry for an unresolved online payment (Suspense Cash Ledger).
    /// Used when a gateway captures money but the system cannot resolve the internal order/payment intent.
    /// 
    /// DEBIT Gateway Clearing
    /// CREDIT Treasury Suspense
    /// 
    /// Idempotency: UNRESOLVED_PAYMENT:{TenantId}:{Gateway}:{EventId}
    /// </summary>
    Task<PostingResult> PostUnresolvedPaymentAsync(
        Guid tenantId,
        string gateway,
        decimal amount,
        string currency,
        string providerEventId,
        CancellationToken ct = default);

    /// <summary>
    /// Clears Treasury Suspense to E-commerce Sales after a previously unresolved
    /// provider capture has been matched to a real storefront order.
    ///
    /// DEBIT Treasury Suspense
    /// CREDIT E-commerce Sales
    ///
    /// Idempotency: 
    /// - Legacy (no gateway): RESOLVED_UNRESOLVED_PAYMENT:{TenantId}:{OrderId}:{ProviderEventId}
    /// - With gateway: RESOLVED_UNRESOLVED_PAYMENT:{TenantId}:{OrderId}:{ProviderEventId}:{Gateway}
    /// </summary>
    Task<PostingResult> PostResolvedUnresolvedPaymentAsync(
        Order order,
        decimal amount,
        string? gateway,
        string providerEventId,
        CancellationToken ct = default);

    /// <summary>
    /// Reverses a tenant suspense posting for CommerceHub captured cash after the provider-success
    /// webhook has been matched to an already-paid local marketplace payment row.
    ///
    /// DEBIT Treasury Suspense
    /// CREDIT Gateway/Bank clearing
    ///
    /// This removes the temporary suspense journal while preserving the normal CommerceHub
    /// payment accounting that was posted through the paid local payment path.
    /// 
    /// Idempotency: CH_RESOLVED_UNRESOLVED_PAYMENT:{TenantId}:{PaymentId}:{ProviderEventId}:{Gateway}
    /// - TenantId: Guid in default format (lowercase with hyphens)
    /// - PaymentId: Guid in default format (lowercase with hyphens)
    /// - ProviderEventId: Case-sensitive string as provided by the payment gateway (trimmed)
    /// - Gateway: Normalized gateway string (trimmed and lowercased)
    /// 
    /// Duplicate keys: Replay is a no-op and returns the original PostingResult from the first successful post.
    /// The underlying journal posting service ensures idempotency via the IdempotencyKey.
    /// </summary>
    Task<PostingResult> PostResolvedUnresolvedCommerceHubPaymentAsync(
        Guid tenantId,
        Guid paymentId,
        decimal amount,
        string gateway,
        string providerEventId,
        CancellationToken ct = default);
    
    // ═══════════════════════════════════════════════════════════════════════════
    // T-08 E-COMMERCE / COMMERCEHUB MERCHANDISE-COST (COGS) OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════
    //
    // Forward-looking cost lifecycle only. Historical e-commerce volume without
    // COGS is immutable pre-remediation history and must never be back-posted here.
    //
    // Sale:   DEBIT 5010 COGS / CREDIT 1310 Inventory (from frozen line snapshots)
    // Return: DEBIT 1310 Inventory / CREDIT 5010 COGS (original snapshot × returned qty)
    //
    // COGS journals touch only 5010/1310: no cash, clearing, AR/AP, or revenue leg.

    /// <summary>
    /// Posts the Storefront sale COGS journal for one inventory-commit event.
    /// Cost basis is the immutable OrderItem.CostPrice snapshot captured at checkout.
    ///
    /// Idempotency: ECOM_COGS:{TenantId}:{OrderId}:{CommitEventId}
    /// SourceType on the <c>JournalEntry</c>: <c>"Order_COGS"</c>
    /// </summary>
    Task<PostingResult> PostEcommerceCogsAsync(
        EcommerceCogsPostingRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Posts the Storefront COGS reversal journal for one return event.
    /// Reversal reuses the original snapshot cost, never live cost, and cumulative
    /// reversals for an order line can never exceed the posted COGS for that line.
    ///
    /// Idempotency: ECOM_COGS_REV:{TenantId}:{OrderId}:{ReturnEventId}
    /// SourceType on the <c>JournalEntry</c>: <c>"Order_COGS_Reversal"</c>
    /// </summary>
    Task<PostingResult> PostEcommerceCogsReversalAsync(
        EcommerceCogsReversalPostingRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Posts the CommerceHub sale COGS journal for one item delivery-commit event.
    /// Cost basis is the immutable CommerceHubSubOrderItem snapshot captured at checkout.
    ///
    /// Idempotency: CH_COGS:{TenantId}:{SubOrderId}:{DeliveryCommitEventId}
    /// SourceType on the <c>JournalEntry</c>: <c>"CommerceHub_COGS"</c>
    /// </summary>
    Task<PostingResult> PostCommerceHubCogsAsync(
        CommerceHubCogsPostingRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Posts the CommerceHub COGS reversal journal for one return receipt event
    /// (resellable quantity only; damaged goods stay in COGS by policy).
    ///
    /// Idempotency: CH_COGS_REV:{TenantId}:{SubOrderId}:{ReturnEventId}
    /// SourceType on the <c>JournalEntry</c>: <c>"CommerceHub_COGS_Reversal"</c>
    /// </summary>
    Task<PostingResult> PostCommerceHubCogsReversalAsync(
        CommerceHubCogsReversalPostingRequest request,
        CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════
    // SETTLEMENT OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Posts journal entry for gateway settlement (funds received).
    /// 
    /// DEBIT Bank (net amount)
    /// DEBIT Gateway Fees
    /// CREDIT Clearing Account
    /// 
    /// Idempotency: GATEWAY_SETTLEMENT:{TenantId}:{SettlementId}
    /// </summary>
    Task<PostingResult> PostGatewaySettlementAsync(
        Guid tenantId,
        Guid settlementBatchId,
        string gateway,
        decimal grossAmount,
        decimal fees,
        string settlementId,
        DateTime settledAtUtc,
        CancellationToken ct = default);
    
    /// <summary>
    /// Posts journal entry for COD settlement (courier pays store).
    /// 
    /// DEBIT Bank (net amount)
    /// DEBIT Shipping Fees (if any)
    /// CREDIT COD Clearing
    /// 
    /// Idempotency: COD_SETTLEMENT:{TenantId}:{SettlementId}
    /// </summary>
    Task<PostingResult> PostCodSettlementAsync(
        Guid tenantId,
        Guid settlementBatchId,
        decimal grossAmount,
        decimal fees,
        string settlementId,
        DateTime settledAtUtc,
        CancellationToken ct = default);
    
    // ═══════════════════════════════════════════════════════════════════════════
    // INVENTORY OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Posts journal entry for inventory adjustment.
    /// 
    /// Increase: DEBIT Inventory, CREDIT Other Income
    /// Decrease: DEBIT Expense/Loss, CREDIT Inventory
    /// 
    /// Idempotency: INVENTORY_ADJUST:{TenantId}:{AdjustmentId}
    /// </summary>
    Task<PostingResult> PostInventoryAdjustmentAsync(
        Guid tenantId,
        decimal costValue,
        bool isIncrease,
        string reason,
        Guid adjustmentId,
        CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════
    // COMMERCEHUB MARKETPLACE OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Posts a GL journal entry that mirrors a CommerceHub operational ledger entry
    /// (<see cref="PointOfSale.Core.Entities.CommerceHubFinance.CommerceHubFinancialTransaction"/>)
    /// into the General Ledger so the <c>CommerceHubFinancialReadinessGate</c> can verify
    /// accounting coverage before authorising outward money movement.
    ///
    /// Double-entry mapping:
    /// <list type="bullet">
    ///   <item>Vendor OrderPayment/Settlement CREDIT  → DR EcommerceSales, CR MarketplaceVendorPayable</item>
    ///   <item>Vendor OrderPayment/Settlement DEBIT   → DR MarketplaceVendorPayable, CR EcommerceSales</item>
    ///   <item>Vendor Commission DEBIT               → DR MarketplaceVendorPayable, CR EcommerceSales</item>
    ///   <item>Platform entries                      → DR/CR EcommerceSales ↔ TreasurySuspense</item>
    ///   <item>All other transaction types           → EcommerceSales ↔ TreasurySuspense</item>
    /// </list>
    ///
    /// Idempotency: COMMERCEHUB_TX:{TenantId}:{TransactionId}
    /// SourceType on the <c>JournalEntry</c>: <c>"CommerceHubFinancialTransaction"</c>
    /// </summary>
    Task<PostingResult> PostCommerceHubFinancialTransactionAsync(
        Guid tenantId,
        Guid transactionId,
        string transactionType,
        string direction,
        string entityType,
        string financialBucket,
        decimal amount,
        string currency,
        string description,
        DateTime occurredAtUtc,
        CancellationToken ct = default);
}

/// <summary>
/// Immutable accounting context for an e-commerce refund.
/// Post-settlement refunds must carry the settlement snapshot so retries and
/// accounting postings cannot be recalculated from mutable payment state.
/// </summary>
public sealed record EcommerceRefundAccountingContext(
    bool IsPostSettlementRefund,
    Guid? SettlementBatchId = null,
    string? SettlementType = null,
    string? SettlementId = null,
    string? PayoutBatchId = null,
    DateTime? SettledAtUtc = null)
{
    public static EcommerceRefundAccountingContext PreSettlement { get; } = new(false);

    public void EnsureValidForPostSettlementRefund()
    {
        if (!IsPostSettlementRefund)
        {
            return;
        }

        if (!SettlementBatchId.HasValue ||
            SettlementBatchId.Value == Guid.Empty ||
            string.IsNullOrWhiteSpace(SettlementType) ||
            string.IsNullOrWhiteSpace(SettlementId) ||
            !SettledAtUtc.HasValue)
        {
            throw new InvalidOperationException(
                "Post-settlement refunds require a durable settlement snapshot.");
        }
    }
}

/// <summary>
/// Result of a posting operation.
/// </summary>
public record PostingResult
{
    public bool Success { get; init; }
    public Guid? JournalId { get; init; }
    public string? JournalNumber { get; init; }
    public string? ErrorMessage { get; init; }
    public bool WasIdempotent { get; init; }
    
    public static PostingResult Succeeded(Guid journalId, string journalNumber, bool wasIdempotent = false)
        => new() { Success = true, JournalId = journalId, JournalNumber = journalNumber, WasIdempotent = wasIdempotent };
    
    public static PostingResult Failed(string error)
        => new() { Success = false, ErrorMessage = error };
}
