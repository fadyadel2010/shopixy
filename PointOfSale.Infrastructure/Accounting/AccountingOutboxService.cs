using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Refunds;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>
/// Outbox service for guaranteed delivery of financial postings.
/// 
/// PHASE 3.5: Transactional outbox pattern.
/// 
/// Enqueue methods must be called WITHIN the business transaction.
/// The background processor will pick up and process messages.
/// </summary>
public class AccountingOutboxService : IAccountingOutboxService
{
    private readonly ApplicationDbContext _db;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;
    private readonly ILogger<AccountingOutboxService> _logger;
    
    public AccountingOutboxService(
        ApplicationDbContext db,
        ICorrelationIdAccessor correlationIdAccessor,
        ILogger<AccountingOutboxService> logger)
    {
        _db = db;
        _correlationIdAccessor = correlationIdAccessor;
        _logger = logger;
    }
    
    public async Task EnqueuePosSaleAsync(
        Guid tenantId,
        Guid invoiceId,
        int? mutationVersion = null,
        CancellationToken ct = default)
    {
        var idempotencyKey = $"SALE:{tenantId}:{invoiceId}";
        await EnqueueMessageAsync(tenantId, "PosSale", invoiceId,
            new
            {
                InvoiceId = invoiceId,
                MutationVersion = mutationVersion
            },
            idempotencyKey,
            mutationVersion,
            ct);
    }

    public async Task EnqueueSaleReversalAsync(
        Guid tenantId,
        Guid invoiceId,
        string operationKey,
        int mutationVersion,
        CancellationToken ct = default)
    {
        ValidateOperationKey(operationKey);

        var idempotencyKey = BuildSalePostingIdempotencyKey(tenantId, invoiceId, operationKey);

        await EnqueueMessageAsync(
            tenantId,
            "SaleReversal",
            invoiceId,
            new
            {
                InvoiceId = invoiceId,
                OperationKey = operationKey,
                MutationVersion = mutationVersion
            },
            idempotencyKey,
            mutationVersion,
            ct);
    }
    
    public async Task EnqueuePosPurchaseAsync(
        Guid tenantId,
        Guid invoiceId,
        int mutationVersion,
        decimal cashAmount = 0m,
        decimal cardAmount = 0m,
        CancellationToken ct = default)
    {
        if (cashAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cashAmount),
                cashAmount,
                "Cash amount cannot be negative.");
        }

        if (cardAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cardAmount),
                cardAmount,
                "Card amount cannot be negative.");
        }

        var idempotencyKey = BuildPurchasePostingIdempotencyKey(tenantId, invoiceId, null, cashAmount, cardAmount, mutationVersion);
        await EnqueueMessageAsync(tenantId, "PosPurchase", invoiceId,
            new
            {
                InvoiceId = invoiceId,
                CashAmount = cashAmount,
                CardAmount = cardAmount,
                MutationVersion = mutationVersion
            },
            idempotencyKey,
            mutationVersion,
            ct);
    }

    public async Task EnqueuePurchaseReversalAsync(
        Guid tenantId,
        Guid invoiceId,
        string operationKey,
        int mutationVersion,
        decimal cashAmount = 0m,
        decimal cardAmount = 0m,
        CancellationToken ct = default)
    {
        ValidateOperationKey(operationKey);

        if (cashAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cashAmount),
                cashAmount,
                "Cash amount cannot be negative.");
        }

        if (cardAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cardAmount),
                cardAmount,
                "Card amount cannot be negative.");
        }

        var idempotencyKey = BuildPurchasePostingIdempotencyKey(tenantId, invoiceId, operationKey, cashAmount, cardAmount, mutationVersion);

        await EnqueueMessageAsync(
            tenantId,
            "PurchaseReversal",
            invoiceId,
            new
            {
                InvoiceId = invoiceId,
                OperationKey = operationKey,
                CashAmount = cashAmount,
                CardAmount = cardAmount,
                MutationVersion = mutationVersion
            },
            idempotencyKey,
            mutationVersion,
            ct);
    }
    
    public async Task EnqueueSupplierPaymentAsync(Guid tenantId, Guid paymentId, CancellationToken ct = default)
    {
        var idempotencyKey = $"SUPPLIER_PAYMENT:{tenantId}:{paymentId}";
        await EnqueueMessageAsync(tenantId, "SupplierPayment", paymentId,
            new { PaymentId = paymentId }, idempotencyKey, null, ct);
    }

    public async Task EnqueueCustomerPaymentAsync(Guid tenantId, Guid paymentId, CancellationToken ct = default)
    {
        var idempotencyKey = $"CUSTOMER_PAYMENT:{tenantId}:{paymentId}";
        await EnqueueMessageAsync(tenantId, "CustomerPayment", paymentId,
            new { PaymentId = paymentId }, idempotencyKey, null, ct);
    }

    public async Task EnqueueSupplierOpeningBalanceAsync(Guid tenantId, Guid openingBalanceId, CancellationToken ct = default)
    {
        var idempotencyKey = $"SUPPLIER_OPENING_BALANCE:{tenantId}:{openingBalanceId}";
        await EnqueueMessageAsync(tenantId, "SupplierOpeningBalance", openingBalanceId,
            new { OpeningBalanceId = openingBalanceId }, idempotencyKey, null, ct);
    }

    public async Task EnqueueCustomerOpeningBalanceAsync(Guid tenantId, Guid openingBalanceId, CancellationToken ct = default)
    {
        var idempotencyKey = $"CUSTOMER_OPENING_BALANCE:{tenantId}:{openingBalanceId}";
        await EnqueueMessageAsync(tenantId, "CustomerOpeningBalance", openingBalanceId,
            new { OpeningBalanceId = openingBalanceId }, idempotencyKey, null, ct);
    }

    public async Task EnqueuePartyLedgerAdjustmentAsync(Guid tenantId, Guid adjustmentId, CancellationToken ct = default)
    {
        var idempotencyKey = $"PARTY_ADJUSTMENT:{tenantId}:{adjustmentId}";
        await EnqueueMessageAsync(tenantId, "PartyLedgerAdjustment", adjustmentId,
            new { AdjustmentId = adjustmentId }, idempotencyKey, null, ct);
    }
    
    public async Task EnqueuePosRefundAsync(
        Guid tenantId,
        Guid returnId,
        Guid originalInvoiceId,
        RefundMethod refundMethod,
        decimal cashAmount,
        decimal cardAmount,
        int? mutationVersion = null,
        CancellationToken ct = default)
    {
        var resolution = RefundAllocationSupport.Resolve(refundMethod, cashAmount, cardAmount);
        if (!resolution.Success)
        {
            throw new ArgumentException(resolution.ErrorMessage, nameof(refundMethod));
        }

        var allocation = resolution.Allocation;
        var idempotencyKey = $"REFUND:{tenantId}:{returnId}";
        await EnqueueMessageAsync(tenantId, "PosRefund", returnId,
            new
            {
                ReturnId = returnId,
                OriginalInvoiceId = originalInvoiceId,
                RefundMethod = allocation.Method.ToString(),
                CashAmount = allocation.CashAmount,
                CardAmount = allocation.CardAmount,
                MutationVersion = mutationVersion
            },
            idempotencyKey,
            mutationVersion,
            ct);
    }

    public async Task EnqueuePurchaseReturnAsync(
        Guid tenantId,
        Guid returnId,
        Guid? originalInvoiceId = null,
        int? mutationVersion = null,
        CancellationToken ct = default)
    {
        var idempotencyKey = $"PURCHASE_RETURN:{tenantId}:{returnId}";
        await EnqueueMessageAsync(tenantId, "PurchaseReturn", returnId,
            new
            {
                ReturnId = returnId,
                OriginalInvoiceId = originalInvoiceId,
                MutationVersion = mutationVersion
            },
            idempotencyKey,
            mutationVersion,
            ct);
    }
    
    public async Task EnqueueOnlinePaymentAsync(Guid tenantId, Guid orderId, string paymentIntentId, string gateway, CancellationToken ct = default)
    {
        var idempotencyKey = $"ONLINE_PAYMENT:{tenantId}:{orderId}:{paymentIntentId}";
        var aggregateVersion = await ResolveRequiredOrderAggregateVersionAsync(tenantId, orderId, "OnlinePayment", ct);
        await EnqueueMessageAsync(tenantId, "OnlinePayment", orderId,
            new { OrderId = orderId, PaymentIntentId = paymentIntentId, Gateway = gateway, MutationVersion = aggregateVersion },
            idempotencyKey,
            aggregateVersion,
            ct);
    }

    public async Task EnqueueUnresolvedOnlinePaymentAsync(
        Guid tenantId,
        decimal amount,
        string currency,
        string gateway,
        string providerEventId,
        Guid businessEntityId = default,
        string? paymentReference = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(gateway);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventId);

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Amount must be greater than zero.");
        }

        var normalizedGateway = gateway.Trim().ToLowerInvariant();
        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        var normalizedProviderEventId = providerEventId.Trim();
        var normalizedPaymentReference = string.IsNullOrWhiteSpace(paymentReference)
            ? null
            : paymentReference.Trim();

        var idempotencyKey = $"UNRESOLVED_PAYMENT:{tenantId}:{normalizedGateway}:{normalizedProviderEventId}";

        await EnqueueMessageAsync(
            tenantId,
            "UnresolvedOnlinePayment",
            businessEntityId,
            new
            {
                Gateway = normalizedGateway,
                Amount = amount,
                Currency = normalizedCurrency,
                ProviderEventId = normalizedProviderEventId,
                PaymentReference = normalizedPaymentReference
            },
            idempotencyKey,
            aggregateVersion: null,
            ct);
    }

    public async Task EnqueueResolvedUnresolvedOnlinePaymentAsync(
        Guid tenantId,
        Guid orderId,
        decimal amount,
        string gateway,
        string providerEventId,
        CancellationToken ct = default)
    {
        // Validate inputs before reserving idempotency slot
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gateway);
        
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Amount must be greater than zero.");
        }

        // Normalize gateway and providerEventId for consistent idempotency keys
        var normalizedGateway = gateway.Trim().ToLowerInvariant();
        var normalizedProviderEventId = providerEventId.Trim();

        var idempotencyKey = $"RESOLVED_UNRESOLVED_PAYMENT:{tenantId}:{orderId}:{normalizedProviderEventId}:{normalizedGateway}";
        
        // Legacy compatibility: check for existing records with raw gateway value
        // TODO: Remove this legacy lookup after migration window (e.g., 30 days after deployment)
        var legacyGateway = gateway.Trim();
        var legacyIdempotencyKey = legacyGateway != normalizedGateway
            ? $"RESOLVED_UNRESOLVED_PAYMENT:{tenantId}:{orderId}:{normalizedProviderEventId}:{legacyGateway}"
            : null;
        
        var aggregateVersion = await ResolveRequiredOrderAggregateVersionAsync(tenantId, orderId, "ResolvedUnresolvedOnlinePayment", ct);
        await EnqueueMessageWithLegacyLookupAsync(
            tenantId,
            "ResolvedUnresolvedOnlinePayment",
            orderId,
            new
            {
                OrderId = orderId,
                Amount = amount,
                Gateway = normalizedGateway,
                ProviderEventId = normalizedProviderEventId,
                MutationVersion = aggregateVersion
            },
            idempotencyKey,
            legacyIdempotencyKey,
            aggregateVersion,
            ct);
    }

    public async Task EnqueueResolvedUnresolvedCommerceHubPaymentAsync(
        Guid tenantId,
        Guid paymentId,
        decimal amount,
        string gateway,
        string providerEventId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gateway);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventId);

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Amount must be greater than zero.");
        }

        var normalizedGateway = gateway.Trim().ToLowerInvariant();
        var normalizedProviderEventId = providerEventId.Trim();
        var idempotencyKey =
            $"CH_RESOLVED_UNRESOLVED_PAYMENT:{tenantId}:{paymentId}:{normalizedProviderEventId}:{normalizedGateway}";

        await EnqueueMessageAsync(
            tenantId,
            "ResolvedUnresolvedCommerceHubPayment",
            paymentId,
            new
            {
                PaymentId = paymentId,
                Amount = amount,
                Gateway = normalizedGateway,
                ProviderEventId = normalizedProviderEventId
            },
            idempotencyKey,
            aggregateVersion: null,
            ct);
    }
    
    public async Task<OutboxEnqueueResult> EnqueueCodDeliveredAsync(Guid tenantId, Guid orderId, CancellationToken ct = default)
    {
        Console.WriteLine($"[CONSOLE-OUTBOX] EnqueueCodDeliveredAsync ENTRY | TenantId: {tenantId} | OrderId: {orderId}");
        _logger.LogInformation("[OUTBOX-COD] EnqueueCodDeliveredAsync ENTRY | TenantId: {TenantId} | OrderId: {OrderId}", tenantId, orderId);
        var idempotencyKey = $"COD_DELIVERED:{tenantId}:{orderId}";
        var aggregateVersion = await ResolveRequiredOrderAggregateVersionAsync(tenantId, orderId, "CodDelivered", ct);
        return await EnqueueMessageAsync(tenantId, "CodDelivered", orderId,
            new { OrderId = orderId, MutationVersion = aggregateVersion },
            idempotencyKey,
            aggregateVersion,
            ct);
    }
    
    public async Task EnqueueWalletApprovedAsync(Guid tenantId, Guid orderId, CancellationToken ct = default)
    {
        var idempotencyKey = $"WALLET_APPROVED:{tenantId}:{orderId}";
        var aggregateVersion = await ResolveRequiredOrderAggregateVersionAsync(tenantId, orderId, "WalletApproved", ct);
        await EnqueueMessageAsync(tenantId, "WalletApproved", orderId,
            new { OrderId = orderId, MutationVersion = aggregateVersion },
            idempotencyKey,
            aggregateVersion,
            ct);
    }
    
    public async Task EnqueueEcommerceRefundAsync(Guid tenantId, Guid orderId, decimal refundAmount, string refundId, CancellationToken ct = default)
    {
        var idempotencyKey = $"ECOM_REFUND:{tenantId}:{orderId}:{refundId}";
        var aggregateVersion = await ResolveRequiredOrderAggregateVersionAsync(tenantId, orderId, "EcommerceRefund", ct);
        await EnqueueMessageAsync(tenantId, "EcommerceRefund", orderId,
            new { OrderId = orderId, RefundAmount = refundAmount, RefundId = refundId, MutationVersion = aggregateVersion },
            idempotencyKey,
            aggregateVersion,
            ct);
    }

    public async Task EnqueueSettledEcommerceRefundAsync(
        Guid tenantId,
        Guid orderId,
        decimal refundAmount,
        string refundId,
        EcommerceRefundAccountingContext settlementContext,
        CancellationToken ct = default)
    {
        settlementContext.EnsureValidForPostSettlementRefund();

        var idempotencyKey = $"ECOM_REFUND:{tenantId}:{orderId}:{refundId}";
        var aggregateVersion = await ResolveRequiredOrderAggregateVersionAsync(tenantId, orderId, "EcommerceRefund", ct);
        await EnqueueMessageAsync(
            tenantId,
            "EcommerceRefund",
            orderId,
            new
            {
                OrderId = orderId,
                RefundAmount = refundAmount,
                RefundId = refundId,
                settlementContext.IsPostSettlementRefund,
                settlementContext.SettlementBatchId,
                settlementContext.SettlementType,
                settlementContext.SettlementId,
                settlementContext.PayoutBatchId,
                settlementContext.SettledAtUtc,
                MutationVersion = aggregateVersion
            },
            idempotencyKey,
            aggregateVersion,
            ct);
    }
    
    public async Task EnqueueGatewaySettlementAsync(Guid tenantId, Guid settlementBatchId, string gateway, decimal grossAmount, decimal fees, string settlementId, CancellationToken ct = default)
    {
        var idempotencyKey = $"GATEWAY_SETTLEMENT:{tenantId}:{settlementId}";
        await EnqueueMessageAsync(tenantId, "GatewaySettlement", settlementBatchId,
            new { SettlementBatchId = settlementBatchId, Gateway = gateway, GrossAmount = grossAmount, Fees = fees, SettlementId = settlementId }, idempotencyKey, null, ct);
    }
    
    public async Task EnqueueCodSettlementAsync(Guid tenantId, Guid settlementBatchId, decimal grossAmount, decimal fees, string settlementId, CancellationToken ct = default)
    {
        var idempotencyKey = $"COD_SETTLEMENT:{tenantId}:{settlementId}";
        await EnqueueMessageAsync(tenantId, "CodSettlement", settlementBatchId,
            new { SettlementBatchId = settlementBatchId, GrossAmount = grossAmount, Fees = fees, SettlementId = settlementId }, idempotencyKey, null, ct);
    }
    
    /// <summary>
    /// PHASE LCR: Enqueue expense for ledger posting.
    /// </summary>
    public async Task EnqueuePosExpenseAsync(
        Guid tenantId,
        Guid expenseId,
        string? operationKey = null,
        int? mutationVersion = null,
        CancellationToken ct = default)
    {
        if (operationKey != null)
        {
            ValidateOperationKey(operationKey);
        }

        var idempotencyKey = BuildExpensePostingIdempotencyKey(tenantId, expenseId, operationKey);
        await EnqueueMessageAsync(tenantId, "PosExpense", expenseId,
            new
            {
                ExpenseId = expenseId,
                OperationKey = operationKey,
                MutationVersion = mutationVersion
            },
            idempotencyKey,
            mutationVersion,
            ct);
    }

    public async Task EnqueueExpenseReversalAsync(
        Guid tenantId,
        Guid expenseId,
        string operationKey,
        int mutationVersion,
        CancellationToken ct = default)
    {
        ValidateOperationKey(operationKey);

        var idempotencyKey = $"EXPENSE_REVERSAL:{tenantId}:{expenseId}:{operationKey}";
        await EnqueueMessageAsync(
            tenantId,
            "ExpenseReversal",
            expenseId,
            new
            {
                ExpenseId = expenseId,
                OperationKey = operationKey,
                MutationVersion = mutationVersion
            },
            idempotencyKey,
            mutationVersion,
            ct);
    }

    private static readonly Regex OperationKeyPattern = new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    /// <summary>
    /// Validates that the operation key contains only safe characters.
    /// Prevents delimiter characters like ':' and control characters that would break idempotency matching.
    /// </summary>
    /// <param name="operationKey">The operation key to validate.</param>
    /// <exception cref="ArgumentException">Thrown when operationKey is null, empty, whitespace, or contains invalid characters.</exception>
    private static void ValidateOperationKey(string operationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);

        if (!OperationKeyPattern.IsMatch(operationKey))
        {
            throw new ArgumentException(
                "Operation key must contain only alphanumeric characters, dots, underscores, or hyphens. " +
                "Invalid characters like ':', control characters (newline, tab, null), and other special characters are not allowed.",
                nameof(operationKey));
        }
    }

    private static string BuildSalePostingIdempotencyKey(Guid tenantId, Guid invoiceId, string operationKey) =>
        $"SALE:{tenantId}:{invoiceId}:{operationKey}";

    private static string BuildPurchasePostingIdempotencyKey(
        Guid tenantId,
        Guid invoiceId,
        string? operationKey = null,
        decimal? cashAmount = null,
        decimal? cardAmount = null,
        int? mutationVersion = null)
    {
        var baseKey = operationKey == null
            ? $"PURCHASE:{tenantId}:{invoiceId}"
            : $"PURCHASE:{tenantId}:{invoiceId}:{operationKey}";

        // Include payment split in idempotency key to distinguish different splits
        if (cashAmount.HasValue || cardAmount.HasValue)
        {
            var cash = cashAmount ?? 0m;
            var card = cardAmount ?? 0m;
            baseKey = $"{baseKey}:C{cash:F2}:D{card:F2}";
        }

        // Include mutation version to distinguish different mutations
        if (mutationVersion.HasValue)
        {
            baseKey = $"{baseKey}:V{mutationVersion.Value}";
        }

        return baseKey;
    }

    private static string BuildExpensePostingIdempotencyKey(Guid tenantId, Guid expenseId, string? operationKey) =>
        operationKey == null
            ? $"EXPENSE:{tenantId}:{expenseId}"
            : $"EXPENSE:{tenantId}:{expenseId}:{operationKey}";
    
    /// <summary>
    /// Enqueues a message with legacy idempotency key lookup support.
    /// Checks both the current idempotency key and a legacy key to avoid duplicate enqueues
    /// when the key format has changed (e.g., gateway normalization).
    /// </summary>
    private async Task<OutboxEnqueueResult> EnqueueMessageWithLegacyLookupAsync(
        Guid tenantId,
        string eventType,
        Guid businessEntityId,
        object payload,
        string idempotencyKey,
        string? legacyIdempotencyKey,
        int? aggregateVersion,
        CancellationToken ct)
    {
        var correlationId = _correlationIdAccessor.GetCorrelationId();
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TenantId"] = tenantId,
            ["EventType"] = eventType,
            ["BusinessEntityId"] = businessEntityId,
            ["IdempotencyKey"] = idempotencyKey,
            ["LegacyIdempotencyKey"] = legacyIdempotencyKey,
        });

        _logger.LogInformation(
            "[OUTBOX] EnqueueMessageWithLegacyLookupAsync called: {EventType} | EntityId: {EntityId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            eventType, businessEntityId, tenantId, correlationId);

        EnsureTransactionalEnqueueBoundary(eventType, businessEntityId, idempotencyKey);
        
        // Check if already enqueued using current OR legacy idempotency key
        var query = _db.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.IdempotencyKey == idempotencyKey);
        
        if (!string.IsNullOrEmpty(legacyIdempotencyKey))
        {
            query = _db.AccountingOutboxMessages
                .IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId && 
                           (m.IdempotencyKey == idempotencyKey || m.IdempotencyKey == legacyIdempotencyKey));
            
            _logger.LogInformation(
                "[OUTBOX] Performing dual-format idempotency lookup | Current: {CurrentKey} | Legacy: {LegacyKey}",
                idempotencyKey, legacyIdempotencyKey);
        }
        
        var existingMessage = await query
            .Select(m => new { m.Id, m.IdempotencyKey, m.ProcessedAtUtc, m.IsFailed, m.NextRetryAtUtc, m.AttemptCount })
            .FirstOrDefaultAsync(ct);
        
        if (existingMessage != null)
        {
            var status = existingMessage.ProcessedAtUtc != null ? "PROCESSED" :
                         existingMessage.IsFailed ? "FAILED" :
                         existingMessage.NextRetryAtUtc > DateTime.UtcNow ? "WAITING_RETRY" : "PENDING";
            
            var isLegacyMatch = !string.IsNullOrEmpty(legacyIdempotencyKey) && 
                               existingMessage.IdempotencyKey == legacyIdempotencyKey;
            
            _logger.LogWarning(
                "[OUTBOX] Message already exists | IdempotencyKey: {IdempotencyKey} | ExistingKey: {ExistingKey} | LegacyMatch: {LegacyMatch} | ExistingId: {ExistingId} | Status: {Status} | Attempts: {Attempts} | CorrelationId: {CorrelationId}",
                idempotencyKey, existingMessage.IdempotencyKey, isLegacyMatch, existingMessage.Id, status, existingMessage.AttemptCount, correlationId);
            
            return OutboxEnqueueResult.Exists(existingMessage.Id, status, existingMessage.IdempotencyKey);
        }
        
        var payloadJson = JsonSerializer.Serialize(payload);
        aggregateVersion ??= TryExtractAggregateVersion(payloadJson);
        aggregateVersion ??= ResolveTrackedAggregateVersion(businessEntityId);

        var message = AccountingOutboxMessage.Create(
            tenantId,
            eventType,
            businessEntityId,
            eventType, // Use eventType: anonymous .FullName exceeds varchar(255)
            payloadJson,
            idempotencyKey,
            correlationId,
            aggregateVersion);
        
        _logger.LogInformation(
            "[OUTBOX-DEBUG] Saving message {Id} to DB | CorrelationId: {CorrelationId}", message.Id, correlationId);
        
        _db.AccountingOutboxMessages.Add(message);
        
        var saved = await _db.SaveChangesAsync(ct);
        
        _logger.LogInformation(
            "[OUTBOX-DEBUG] Message SAVED: {EventType} | MessageId: {MessageId} | EntityId: {EntityId} | RowsSaved: {RowsSaved} | CorrelationId: {CorrelationId}",
            eventType, message.Id, businessEntityId, saved, correlationId);
        
        return OutboxEnqueueResult.Created(message.Id, idempotencyKey);
    }
    
    private async Task<OutboxEnqueueResult> EnqueueMessageAsync(
        Guid tenantId,
        string eventType,
        Guid businessEntityId,
        object payload,
        string idempotencyKey,
        int? aggregateVersion,
        CancellationToken ct)
    {
        var correlationId = _correlationIdAccessor.GetCorrelationId();
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TenantId"] = tenantId,
            ["EventType"] = eventType,
            ["BusinessEntityId"] = businessEntityId,
            ["IdempotencyKey"] = idempotencyKey,
        });

        _logger.LogInformation(
            "[OUTBOX] EnqueueMessageAsync called: {EventType} | EntityId: {EntityId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            eventType, businessEntityId, tenantId, correlationId);

        EnsureTransactionalEnqueueBoundary(eventType, businessEntityId, idempotencyKey);
        
        // Check if already enqueued (idempotency at enqueue level)
        // IMPORTANT: Get the existing message with its status to understand WHY we're skipping
        var existingMessage = await _db.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.IdempotencyKey == idempotencyKey)
            .Select(m => new { m.Id, m.ProcessedAtUtc, m.IsFailed, m.NextRetryAtUtc, m.AttemptCount })
            .FirstOrDefaultAsync(ct);
        
        if (existingMessage != null)
        {
            // Determine the status of the existing message
            var status = existingMessage.ProcessedAtUtc != null ? "PROCESSED" :
                         existingMessage.IsFailed ? "FAILED" :
                         existingMessage.NextRetryAtUtc > DateTime.UtcNow ? "WAITING_RETRY" : "PENDING";
            
            _logger.LogWarning(
                "[OUTBOX] Message already exists | IdempotencyKey: {IdempotencyKey} | ExistingId: {ExistingId} | Status: {Status} | Attempts: {Attempts} | CorrelationId: {CorrelationId}",
                idempotencyKey, existingMessage.Id, status, existingMessage.AttemptCount, correlationId);
            
            return OutboxEnqueueResult.Exists(existingMessage.Id, status, idempotencyKey);
        }
        
        var payloadJson = JsonSerializer.Serialize(payload);
        aggregateVersion ??= TryExtractAggregateVersion(payloadJson);
        aggregateVersion ??= ResolveTrackedAggregateVersion(businessEntityId);

        var message = AccountingOutboxMessage.Create(
            tenantId,
            eventType,
            businessEntityId,
            eventType, // Use eventType: anonymous .FullName exceeds varchar(255)
            payloadJson,
            idempotencyKey,
            correlationId,
            aggregateVersion);
        
        _logger.LogInformation(
            "[OUTBOX-DEBUG] Saving message {Id} to DB | CorrelationId: {CorrelationId}", message.Id, correlationId);
        
        _db.AccountingOutboxMessages.Add(message);
        
        // Save immediately while the caller's explicit transaction is active. This keeps the business
        // mutation and accounting outbox row atomic without allowing this service to silently commit
        // a larger workflow that the caller still expects to be rollbackable.
        var saved = await _db.SaveChangesAsync(ct);
        
        _logger.LogInformation(
            "[OUTBOX-DEBUG] Message SAVED: {EventType} | MessageId: {MessageId} | EntityId: {EntityId} | RowsSaved: {RowsSaved} | CorrelationId: {CorrelationId}",
            eventType, message.Id, businessEntityId, saved, correlationId);
        
        return OutboxEnqueueResult.Created(message.Id, idempotencyKey);
    }

    private static int? TryExtractAggregateVersion(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        using var payload = JsonDocument.Parse(payloadJson);
        return payload.RootElement.TryGetProperty("MutationVersion", out var mutationVersionProperty) &&
               mutationVersionProperty.ValueKind == JsonValueKind.Number &&
               mutationVersionProperty.TryGetInt32(out var mutationVersion)
            ? mutationVersion
            : null;
    }

    private int? ResolveTrackedAggregateVersion(Guid businessEntityId)
    {
        foreach (var entry in _db.ChangeTracker.Entries<IProjectionVersioned>())
        {
            if (entry.State is EntityState.Detached)
            {
                continue;
            }

            var idProperty = entry.Metadata.FindProperty("Id");
            if (idProperty == null)
            {
                continue;
            }

            var currentId = entry.Property("Id").CurrentValue;
            if (currentId is not Guid entityId || entityId != businessEntityId)
            {
                continue;
            }

            var sourceVersion = ResolvePendingProjectionVersion(entry);

            return ProductEventVersionHelper.ToProductEventVersion(sourceVersion);
        }

        return null;
    }

    private async Task<int> ResolveRequiredOrderAggregateVersionAsync(
        Guid tenantId,
        Guid orderId,
        string eventType,
        CancellationToken cancellationToken)
    {
        var trackedVersion = ResolveTrackedAggregateVersion(orderId);
        if (trackedVersion.HasValue)
        {
            if (trackedVersion.Value <= 0)
            {
                throw new InvalidOperationException(
                    $"Critical accounting event {eventType} for order {orderId} requires a positive aggregate version.");
            }
            return trackedVersion.Value;
        }

        var persistedVersion = await _db.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(order => order.TenantId == tenantId && order.Id == orderId)
            .Select(order => (long?)order.ProjectionMutationVersion)
            .FirstOrDefaultAsync(cancellationToken);

        if (!persistedVersion.HasValue)
        {
            throw new InvalidOperationException(
                $"Critical accounting event {eventType} cannot resolve order aggregate version because order {orderId} was not found for tenant {tenantId}.");
        }

        var version = ProductEventVersionHelper.ToProductEventVersion(persistedVersion.Value);
        if (version <= 0)
        {
            throw new InvalidOperationException(
                $"Critical accounting event {eventType} for order {orderId} requires a positive aggregate version.");
        }

        return version;
    }

    private static long ResolvePendingProjectionVersion(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<IProjectionVersioned> entry)
    {
        return entry.State switch
        {
            EntityState.Added => entry.Entity.ProjectionMutationVersion > 0
                ? entry.Entity.ProjectionMutationVersion
                : 1,
            EntityState.Modified or EntityState.Deleted => IsProjectionVersionManuallyAdvanced(entry)
                ? entry.Entity.ProjectionMutationVersion
                : entry.Entity.ProjectionMutationVersion + 1,
            _ => entry.Entity.ProjectionMutationVersion
        };
    }

    private static bool IsProjectionVersionManuallyAdvanced(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<IProjectionVersioned> entry)
    {
        var property = entry.Property(nameof(IProjectionVersioned.ProjectionMutationVersion));
        return property.IsModified;
    }

    private void EnsureTransactionalEnqueueBoundary(
        string eventType,
        Guid businessEntityId,
        string idempotencyKey)
    {
        if (_db.Database.CurrentTransaction != null)
        {
            return;
        }

        var hasPendingBusinessMutation = _db.ChangeTracker
            .Entries()
            .Any(entry =>
                entry.Entity is not AccountingOutboxMessage &&
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

        throw new InvalidOperationException(
            (hasPendingBusinessMutation
                ? "Accounting outbox enqueue found pending business mutations without an active database transaction. "
                : "Accounting outbox enqueue must be paired with the authoritative business mutation inside an active database transaction. ") +
            "Wrap the business write and accounting enqueue in IAccountingTransactionBoundary or an explicit DbContext transaction. " +
            $"EventType={eventType}; BusinessEntityId={businessEntityId}; IdempotencyKey={idempotencyKey}");
    }
}
