using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.Audit;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services;

public sealed class FinancialIntegrityService : IFinancialIntegrityService
{
    private readonly ApplicationDbContext _context;
    private readonly IPartyBalanceMaintenanceService _partyBalanceMaintenanceService;
    private readonly IAuditService _auditService;
    private readonly ILogger<FinancialIntegrityService> _logger;

    public FinancialIntegrityService(
        ApplicationDbContext context,
        IPartyBalanceMaintenanceService partyBalanceMaintenanceService,
        IAuditService auditService,
        ILogger<FinancialIntegrityService> logger)
    {
        _context = context;
        _partyBalanceMaintenanceService = partyBalanceMaintenanceService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<PartyBalanceReconciliationResult> ReconcileHistoricalBalancesAsync(
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        var items = new List<PartyBalanceReconciliationItem>();
        var mismatches = 0;
        var corrected = 0;
        var totalScanned = 0;

        var ownsTransaction = _context.Database.IsRelational() && _context.Database.CurrentTransaction == null;
        await using var transaction = ownsTransaction
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var customers = await _context.Customers
                .AsNoTracking()
                .Select(customer => new { customer.Id, customer.Name, customer.Balance })
                .ToListAsync(cancellationToken);

            var customerLedgerBalances = await _context.AccountLedgers
                .Where(entry => entry.PartyType == PartyType.Customer)
                .GroupBy(entry => entry.PartyId)
                .Select(group => new
                {
                    PartyId = group.Key,
                    Balance = group.Sum(entry => entry.DebitAmount - entry.CreditAmount),
                })
                .ToDictionaryAsync(item => item.PartyId, item => item.Balance, cancellationToken);

            foreach (var customer in customers)
            {
                totalScanned++;
                var ledgerBalance = customerLedgerBalances.GetValueOrDefault(customer.Id);
                var mismatch = customer.Balance != ledgerBalance;

                if (mismatch)
                {
                    mismatches++;
                    if (!dryRun)
                    {
                        await _partyBalanceMaintenanceService.RebuildPartyBalanceAsync(
                            PartyType.Customer,
                            customer.Id,
                            "Historical customer balance reconciliation",
                            writeAuditTrail: true,
                            cancellationToken: cancellationToken);
                        corrected++;
                    }
                }

                items.Add(new PartyBalanceReconciliationItem(
                    PartyType.Customer,
                    customer.Id,
                    customer.Name,
                    customer.Balance,
                    ledgerBalance,
                    mismatch,
                    mismatch && !dryRun));
            }

            var suppliers = await _context.Suppliers
                .AsNoTracking()
                .Select(supplier => new { supplier.Id, supplier.Name, supplier.Balance })
                .ToListAsync(cancellationToken);

            var supplierLedgerBalances = await _context.AccountLedgers
                .Where(entry => entry.PartyType == PartyType.Supplier)
                .GroupBy(entry => entry.PartyId)
                .Select(group => new
                {
                    PartyId = group.Key,
                    Balance = group.Sum(entry => entry.DebitAmount - entry.CreditAmount),
                })
                .ToDictionaryAsync(item => item.PartyId, item => item.Balance, cancellationToken);

            foreach (var supplier in suppliers)
            {
                totalScanned++;
                var ledgerBalance = supplierLedgerBalances.GetValueOrDefault(supplier.Id);
                var mismatch = supplier.Balance != ledgerBalance;

                if (mismatch)
                {
                    mismatches++;
                    if (!dryRun)
                    {
                        await _partyBalanceMaintenanceService.RebuildPartyBalanceAsync(
                            PartyType.Supplier,
                            supplier.Id,
                            "Historical supplier balance reconciliation",
                            writeAuditTrail: true,
                            cancellationToken: cancellationToken);
                        corrected++;
                    }
                }

                items.Add(new PartyBalanceReconciliationItem(
                    PartyType.Supplier,
                    supplier.Id,
                    supplier.Name,
                    supplier.Balance,
                    ledgerBalance,
                    mismatch,
                    mismatch && !dryRun));
            }

            if (!dryRun)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            if (ownsTransaction && transaction != null)
            {
                if (dryRun)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                else
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            }

            await _auditService.LogAction(
                actionType: dryRun ? "ReconcileHistoricalBalancesDryRun" : "ReconcileHistoricalBalances",
                entityType: "FinancialIntegrity",
                entityId: "PartyBalances",
                description: $"Historical balance reconciliation completed. DryRun={dryRun}.",
                beforeData: null,
                afterData: new
                {
                    DryRun = dryRun,
                    TotalPartiesScanned = totalScanned,
                    MismatchCount = mismatches,
                    CorrectedCount = corrected,
                },
                reference: null,
                success: true,
                source: "Internal");

            return new PartyBalanceReconciliationResult(
                dryRun,
                totalScanned,
                mismatches,
                corrected,
                items);
        }
        catch
        {
            if (ownsTransaction && transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
    }

    public async Task<FinancialAuditValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var issues = new List<FinancialAuditIssue>();
        var checks = new List<FinancialAuditCheckResult>();

        await RunValidationCheckAsync(
            "Stored balances vs ledger",
            ValidateStoredBalancesAgainstLedgerAsync,
            issues,
            checks,
            cancellationToken);
        await RunValidationCheckAsync(
            "Supplier opening balance uniqueness",
            ValidateDuplicateSupplierOpeningBalancesAsync,
            issues,
            checks,
            cancellationToken);
        await RunValidationCheckAsync(
            "Supplier opening balance legacy fields",
            ValidateSupplierOpeningBalanceFieldsAsync,
            issues,
            checks,
            cancellationToken);
        await RunValidationCheckAsync(
            "Party transactions vs ledger",
            ValidateOrphanPartyTransactionsAsync,
            issues,
            checks,
            cancellationToken);
        await RunValidationCheckAsync(
            "Ledger entries vs party transactions",
            ValidateOrphanLedgerEntriesAsync,
            issues,
            checks,
            cancellationToken);
        await RunValidationCheckAsync(
            "POS sale customer/accounting coverage",
            ValidatePosSaleCustomerAccountingCoverageAsync,
            issues,
            checks,
            cancellationToken);
        await RunValidationCheckAsync(
            "Expense accounting coverage",
            ValidateExpenseLinksAsync,
            issues,
            checks,
            cancellationToken);
        await RunValidationCheckAsync(
            "Supplier payment coverage",
            ValidateSupplierPaymentLinksAsync,
            issues,
            checks,
            cancellationToken);
        await RunValidationCheckAsync(
            "Adjustment coverage",
            ValidateAdjustmentLinksAsync,
            issues,
            checks,
            cancellationToken);
        await RunValidationCheckAsync(
            "Purchase return coverage",
            ValidatePurchaseReturnLinksAsync,
            issues,
            checks,
            cancellationToken);
        await RunValidationCheckAsync(
            "Duplicate journals",
            ValidateDuplicateAccountingEntriesAsync,
            issues,
            checks,
            cancellationToken);
        await RunValidationCheckAsync(
            "Duplicate outbox messages",
            ValidateDuplicateOutboxMessagesAsync,
            issues,
            checks,
            cancellationToken);
        await RunValidationCheckAsync(
            "Legacy refund outbox payloads",
            ValidateLegacyRefundOutboxMessagesAsync,
            issues,
            checks,
            cancellationToken);
        await RunValidationCheckAsync(
            "Shared cash account migration",
            ValidateLegacySharedCashAccountBalancesAsync,
            issues,
            checks,
            cancellationToken);
        await RunValidationCheckAsync(
            "Mutation idempotency coverage",
            ValidateMutationIdempotencyCoverageAsync,
            issues,
            checks,
            cancellationToken);

        var summary = new FinancialAuditValidationSummary(
            TotalIssues: issues.Count,
            ErrorCount: issues.Count(issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase)),
            WarningCount: issues.Count(issue => string.Equals(issue.Severity, "Warning", StringComparison.OrdinalIgnoreCase)),
            Checks: checks);
        var result = new FinancialAuditValidationResult(issues.Count == 0, summary, issues);

        _logger.LogInformation(
            "Financial audit validation completed | Healthy: {Healthy} | ErrorCount: {ErrorCount} | WarningCount: {WarningCount} | CheckCount: {CheckCount}",
            result.IsHealthy,
            summary.ErrorCount,
            summary.WarningCount,
            summary.Checks.Count);

        return result;
    }

    private async Task ValidateStoredBalancesAgainstLedgerAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var customerLedgerBalances = await _context.AccountLedgers
            .Where(entry => entry.PartyType == PartyType.Customer)
            .GroupBy(entry => entry.PartyId)
            .Select(group => new { PartyId = group.Key, Balance = group.Sum(entry => entry.DebitAmount - entry.CreditAmount) })
            .ToDictionaryAsync(item => item.PartyId, item => item.Balance, cancellationToken);

        var customers = await _context.Customers
            .AsNoTracking()
            .Select(customer => new { customer.Id, customer.Name, customer.Balance })
            .ToListAsync(cancellationToken);

        foreach (var customer in customers.Where(customer => customer.Balance != customerLedgerBalances.GetValueOrDefault(customer.Id)))
        {
            issues.Add(new FinancialAuditIssue(
                "LEDGER_BALANCE_MISMATCH",
                "Error",
                $"Customer '{customer.Name}' stored balance does not match ledger balance.",
                "Customer",
                customer.Id.ToString()));
        }

        var supplierLedgerBalances = await _context.AccountLedgers
            .Where(entry => entry.PartyType == PartyType.Supplier)
            .GroupBy(entry => entry.PartyId)
            .Select(group => new { PartyId = group.Key, Balance = group.Sum(entry => entry.DebitAmount - entry.CreditAmount) })
            .ToDictionaryAsync(item => item.PartyId, item => item.Balance, cancellationToken);

        var suppliers = await _context.Suppliers
            .AsNoTracking()
            .Select(supplier => new { supplier.Id, supplier.Name, supplier.Balance })
            .ToListAsync(cancellationToken);

        foreach (var supplier in suppliers.Where(supplier => supplier.Balance != supplierLedgerBalances.GetValueOrDefault(supplier.Id)))
        {
            issues.Add(new FinancialAuditIssue(
                "LEDGER_BALANCE_MISMATCH",
                "Error",
                $"Supplier '{supplier.Name}' stored balance does not match ledger balance.",
                "Supplier",
                supplier.Id.ToString()));
        }
    }

    private async Task ValidateDuplicateSupplierOpeningBalancesAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var supplierTransactionDuplicates = await _context.SupplierTransactions
            .AsNoTracking()
            .Where(transaction => transaction.TransactionType == TransactionType.OpeningBalance)
            .GroupBy(transaction => new { transaction.TenantId, transaction.SupplierId })
            .Where(group => group.Count() > 1)
            .Select(group => new
            {
                group.Key.SupplierId,
                Count = group.Count(),
            })
            .ToListAsync(cancellationToken);

        foreach (var duplicate in supplierTransactionDuplicates)
        {
            issues.Add(new FinancialAuditIssue(
                "DUPLICATE_SUPPLIER_OPENING_BALANCE",
                "Error",
                $"Supplier {duplicate.SupplierId} has {duplicate.Count} active supplier opening-balance history rows.",
                "Supplier",
                duplicate.SupplierId.ToString()));
        }

        var supplierLedgerDuplicates = await _context.AccountLedgers
            .AsNoTracking()
            .Where(entry =>
                entry.PartyType == PartyType.Supplier &&
                entry.TransactionType == TransactionType.OpeningBalance)
            .GroupBy(entry => new { entry.TenantId, entry.PartyId })
            .Where(group => group.Count() > 1)
            .Select(group => new
            {
                SupplierId = group.Key.PartyId,
                Count = group.Count(),
            })
            .ToListAsync(cancellationToken);

        foreach (var duplicate in supplierLedgerDuplicates)
        {
            issues.Add(new FinancialAuditIssue(
                "DUPLICATE_SUPPLIER_OPENING_BALANCE",
                "Error",
                $"Supplier {duplicate.SupplierId} has {duplicate.Count} active supplier opening-balance ledger rows.",
                "Supplier",
                duplicate.SupplierId.ToString()));
        }
    }

    private async Task ValidateSupplierOpeningBalanceFieldsAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var suppliers = await _context.Suppliers
            .AsNoTracking()
            .Select(supplier => new
            {
                supplier.TenantId,
                supplier.Id,
                supplier.Name,
                supplier.OpeningBalanceAmount,
                supplier.BalanceType,
                supplier.OpeningBalanceDate,
            })
            .ToListAsync(cancellationToken);

        var supplierOpeningTransactions = await _context.SupplierTransactions
            .AsNoTracking()
            .Where(transaction => transaction.TransactionType == TransactionType.OpeningBalance)
            .Select(transaction => new
            {
                transaction.TenantId,
                transaction.SupplierId,
                transaction.TransactionDate,
                transaction.CreatedAt,
                transaction.Id,
                transaction.DebitAmount,
                transaction.CreditAmount,
            })
            .ToListAsync(cancellationToken);

        var supplierOpeningLedgers = await _context.AccountLedgers
            .AsNoTracking()
            .Where(entry =>
                entry.PartyType == PartyType.Supplier &&
                entry.TransactionType == TransactionType.OpeningBalance)
            .Select(entry => new
            {
                entry.TenantId,
                SupplierId = entry.PartyId,
                entry.TransactionDate,
                entry.CreatedAt,
                entry.Id,
                entry.DebitAmount,
                entry.CreditAmount,
            })
            .ToListAsync(cancellationToken);

        var authoritativeOpenings = supplierOpeningTransactions
            .GroupBy(item => (item.TenantId, item.SupplierId))
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var authoritative = group
                        .OrderBy(item => item.TransactionDate)
                        .ThenBy(item => item.CreatedAt)
                        .ThenBy(item => item.Id)
                        .First();

                    var signedAmount = authoritative.DebitAmount - authoritative.CreditAmount;
                    return (
                        Amount: Math.Abs(signedAmount),
                        BalanceType: signedAmount < 0m ? "Credit" : "Debit",
                        Date: authoritative.TransactionDate);
                });

        foreach (var ledgerOpening in supplierOpeningLedgers
                     .GroupBy(item => (item.TenantId, item.SupplierId)))
        {
            if (authoritativeOpenings.ContainsKey(ledgerOpening.Key))
            {
                continue;
            }

            var authoritative = ledgerOpening
                .OrderBy(item => item.TransactionDate)
                .ThenBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .First();
            var signedAmount = authoritative.DebitAmount - authoritative.CreditAmount;

            authoritativeOpenings[ledgerOpening.Key] = (
                Amount: Math.Abs(signedAmount),
                BalanceType: signedAmount < 0m ? "Credit" : "Debit",
                Date: authoritative.TransactionDate);
        }

        foreach (var supplier in suppliers)
        {
            var key = (supplier.TenantId, supplier.Id);
            var hasLegacyFields =
                supplier.OpeningBalanceAmount.HasValue ||
                !string.IsNullOrWhiteSpace(supplier.BalanceType) ||
                supplier.OpeningBalanceDate.HasValue;

            if (!authoritativeOpenings.TryGetValue(key, out var authoritativeOpening))
            {
                if (!hasLegacyFields)
                {
                    continue;
                }

                issues.Add(new FinancialAuditIssue(
                    "SUPPLIER_OPENING_BALANCE_LEGACY_MISMATCH",
                    "Error",
                    $"Supplier '{supplier.Name}' has legacy opening-balance fields but no surviving authoritative opening-balance history.",
                    "Supplier",
                    supplier.Id.ToString()));
                continue;
            }

            var normalizedBalanceType = NormalizeBalanceType(supplier.BalanceType);
            var amountMismatch = supplier.OpeningBalanceAmount != authoritativeOpening.Amount;
            var balanceTypeMismatch = normalizedBalanceType != authoritativeOpening.BalanceType;
            var dateMismatch = supplier.OpeningBalanceDate != authoritativeOpening.Date;

            if (!amountMismatch && !balanceTypeMismatch && !dateMismatch)
            {
                continue;
            }

            issues.Add(new FinancialAuditIssue(
                "SUPPLIER_OPENING_BALANCE_LEGACY_MISMATCH",
                "Error",
                $"Supplier '{supplier.Name}' opening-balance fields do not match authoritative opening-balance history.",
                "Supplier",
                supplier.Id.ToString()));
        }
    }

    private async Task ValidateOrphanPartyTransactionsAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var ledgerKeys = await _context.AccountLedgers
            .AsNoTracking()
            .Select(entry => BuildLedgerMatchKey(
                entry.PartyType,
                entry.PartyId,
                entry.TransactionType,
                entry.ReferenceId,
                entry.DocumentNumber,
                entry.DebitAmount,
                entry.CreditAmount))
            .ToHashSetAsync(cancellationToken);

        var customerTransactions = await _context.CustomerTransactions
            .AsNoTracking()
            .Select(transaction => new
            {
                transaction.Id,
                transaction.CustomerId,
                transaction.TransactionType,
                transaction.ReferenceId,
                transaction.ReferenceNumber,
                transaction.DebitAmount,
                transaction.CreditAmount,
            })
            .ToListAsync(cancellationToken);

        foreach (var transaction in customerTransactions)
        {
            var key = BuildLedgerMatchKey(
                PartyType.Customer,
                transaction.CustomerId,
                transaction.TransactionType,
                transaction.ReferenceId,
                transaction.ReferenceNumber,
                transaction.DebitAmount,
                transaction.CreditAmount);

            if (!ledgerKeys.Contains(key))
            {
                issues.Add(new FinancialAuditIssue(
                    "ORPHAN_PARTY_TRANSACTION",
                    "Error",
                    "Customer transaction has no matching account-ledger entry.",
                    "CustomerTransaction",
                    transaction.Id.ToString()));
            }
        }

        var supplierTransactions = await _context.SupplierTransactions
            .AsNoTracking()
            .Select(transaction => new
            {
                transaction.Id,
                transaction.SupplierId,
                transaction.TransactionType,
                transaction.ReferenceId,
                transaction.ReferenceNumber,
                transaction.DebitAmount,
                transaction.CreditAmount,
            })
            .ToListAsync(cancellationToken);

        foreach (var transaction in supplierTransactions)
        {
            var key = BuildLedgerMatchKey(
                PartyType.Supplier,
                transaction.SupplierId,
                transaction.TransactionType,
                transaction.ReferenceId,
                transaction.ReferenceNumber,
                transaction.DebitAmount,
                transaction.CreditAmount);

            if (!ledgerKeys.Contains(key))
            {
                issues.Add(new FinancialAuditIssue(
                    "ORPHAN_PARTY_TRANSACTION",
                    "Error",
                    "Supplier transaction has no matching account-ledger entry.",
                    "SupplierTransaction",
                    transaction.Id.ToString()));
            }
        }
    }

    private async Task ValidateOrphanLedgerEntriesAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var partyTransactionKeys = await _context.CustomerTransactions
            .AsNoTracking()
            .Select(transaction => BuildLedgerMatchKey(
                PartyType.Customer,
                transaction.CustomerId,
                transaction.TransactionType,
                transaction.ReferenceId,
                transaction.ReferenceNumber,
                transaction.DebitAmount,
                transaction.CreditAmount))
            .Concat(
                _context.SupplierTransactions
                    .AsNoTracking()
                    .Select(transaction => BuildLedgerMatchKey(
                        PartyType.Supplier,
                        transaction.SupplierId,
                        transaction.TransactionType,
                        transaction.ReferenceId,
                        transaction.ReferenceNumber,
                        transaction.DebitAmount,
                        transaction.CreditAmount)))
            .ToHashSetAsync(cancellationToken);

        var ledgerEntries = await _context.AccountLedgers
            .AsNoTracking()
            .Where(entry => entry.ReferenceId.HasValue)
            .Select(entry => new
            {
                entry.Id,
                entry.PartyType,
                entry.PartyId,
                entry.TransactionType,
                entry.ReferenceId,
                entry.DocumentNumber,
                entry.DebitAmount,
                entry.CreditAmount,
            })
            .ToListAsync(cancellationToken);

        foreach (var entry in ledgerEntries)
        {
            var key = BuildLedgerMatchKey(
                entry.PartyType,
                entry.PartyId,
                entry.TransactionType,
                entry.ReferenceId,
                entry.DocumentNumber,
                entry.DebitAmount,
                entry.CreditAmount);

            if (!partyTransactionKeys.Contains(key))
            {
                issues.Add(new FinancialAuditIssue(
                    "ORPHAN_LEDGER_ENTRY",
                    "Error",
                    "Party ledger entry has no matching customer/supplier transaction record.",
                    "AccountLedger",
                    entry.Id.ToString()));
            }
        }
    }

    private async Task ValidatePosSaleCustomerAccountingCoverageAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var invoices = await _context.SalesInvoices
            .AsNoTracking()
            .Where(invoice =>
                invoice.CustomerId.HasValue &&
                invoice.Status != InvoiceStatus.Cancelled)
            .Select(invoice => new
            {
                invoice.Id,
                invoice.TenantId,
                invoice.CustomerId,
                invoice.InvoiceNumber,
                invoice.TotalAmount,
                invoice.PaidAmount,
            })
            .ToListAsync(cancellationToken);

        if (invoices.Count == 0)
        {
            return;
        }

        var invoiceIds = invoices.Select(invoice => invoice.Id).ToHashSet();

        var customerTransactions = await _context.CustomerTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.ReferenceId.HasValue &&
                    invoiceIds.Contains(transaction.ReferenceId.Value))
                .Select(transaction => new
                {
                    transaction.CustomerId,
                    transaction.TransactionType,
                    transaction.ReferenceId,
                    transaction.ReferenceNumber,
                    transaction.DebitAmount,
                    transaction.CreditAmount,
                })
                .ToListAsync(cancellationToken);

        var customerTransactionKeys = customerTransactions
            .Select(transaction => BuildLedgerMatchKey(
                PartyType.Customer,
                transaction.CustomerId,
                transaction.TransactionType,
                transaction.ReferenceId,
                transaction.ReferenceNumber,
                transaction.DebitAmount,
                transaction.CreditAmount))
            .ToHashSet();

        var customerLedgerEntries = await _context.AccountLedgers
                .AsNoTracking()
                .Where(entry =>
                    entry.PartyType == PartyType.Customer &&
                    entry.ReferenceId.HasValue &&
                    invoiceIds.Contains(entry.ReferenceId.Value))
                .Select(entry => new
                {
                    entry.PartyId,
                    entry.TransactionType,
                    entry.ReferenceId,
                    entry.DocumentNumber,
                    entry.DebitAmount,
                    entry.CreditAmount,
                })
                .ToListAsync(cancellationToken);

        var customerLedgerKeys = customerLedgerEntries
            .Select(entry => BuildLedgerMatchKey(
                PartyType.Customer,
                entry.PartyId,
                entry.TransactionType,
                entry.ReferenceId,
                entry.DocumentNumber,
                entry.DebitAmount,
                entry.CreditAmount))
            .ToHashSet();

        var outboxMessagesByInvoiceId = (await _context.AccountingOutboxMessages
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(message =>
                    invoiceIds.Contains(message.BusinessEntityId) &&
                    (message.EventType == "PosSale" || message.EventType == "SaleReversal"))
                .Select(message => new
                {
                    message.BusinessEntityId,
                    message.ProcessedAtUtc,
                    message.LastError,
                    message.IsFailed,
                    message.ClaimedUntilUtc,
                    message.NextRetryAtUtc
                })
                .ToListAsync(cancellationToken))
            .GroupBy(message => message.BusinessEntityId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var postedSaleJournalIds = await _context.JournalEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry =>
                invoiceIds.Contains(entry.SourceId) &&
                entry.SourceType == "SalesInvoice" &&
                entry.Status == JournalStatus.Posted)
            .Select(entry => entry.SourceId)
            .ToHashSetAsync(cancellationToken);

        foreach (var invoice in invoices)
        {
            var customerId = invoice.CustomerId!.Value;
            var saleKey = BuildLedgerMatchKey(
                PartyType.Customer,
                customerId,
                TransactionType.Sale,
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.TotalAmount,
                0m);

            if (!customerTransactionKeys.Contains(saleKey))
            {
                issues.Add(new FinancialAuditIssue(
                    "MISSING_CUSTOMER_SALE_TRANSACTION",
                    "Error",
                    "Sales invoice has no matching customer sale transaction.",
                    "SalesInvoice",
                    invoice.Id.ToString()));
            }

            if (!customerLedgerKeys.Contains(saleKey))
            {
                issues.Add(new FinancialAuditIssue(
                    "MISSING_CUSTOMER_SALE_LEDGER",
                    "Error",
                    "Sales invoice has no matching customer account-ledger sale entry.",
                    "SalesInvoice",
                    invoice.Id.ToString()));
            }

            if (invoice.PaidAmount > 0m)
            {
                // Aggregate all PaymentReceived entries for this invoice (handles split payments)
                var transactionPaymentTotal = customerTransactions
                    .Where(t => t.CustomerId == customerId &&
                                t.TransactionType == TransactionType.PaymentReceived &&
                                t.ReferenceId == invoice.Id)
                    .Sum(t => t.CreditAmount);

                var ledgerPaymentTotal = customerLedgerEntries
                    .Where(e => e.PartyId == customerId &&
                                e.TransactionType == TransactionType.PaymentReceived &&
                                e.ReferenceId == invoice.Id)
                    .Sum(e => e.CreditAmount);

                // Use monetary comparison tolerance (0.01 for typical currency precision)
                const decimal tolerance = 0.01m;

                if (Math.Abs(transactionPaymentTotal - invoice.PaidAmount) > tolerance)
                {
                    issues.Add(new FinancialAuditIssue(
                        "MISSING_CUSTOMER_PAYMENT_TRANSACTION",
                        "Error",
                        $"Paid sales invoice payment total mismatch: expected {invoice.PaidAmount:F2}, found {transactionPaymentTotal:F2} in customer transactions.",
                        "SalesInvoice",
                        invoice.Id.ToString()));
                }

                if (Math.Abs(ledgerPaymentTotal - invoice.PaidAmount) > tolerance)
                {
                    issues.Add(new FinancialAuditIssue(
                        "MISSING_CUSTOMER_PAYMENT_LEDGER",
                        "Error",
                        $"Paid sales invoice payment total mismatch: expected {invoice.PaidAmount:F2}, found {ledgerPaymentTotal:F2} in customer account-ledger.",
                        "SalesInvoice",
                        invoice.Id.ToString()));
                }
            }

            if (!outboxMessagesByInvoiceId.TryGetValue(invoice.Id, out var outboxMessages) ||
                outboxMessages.Count == 0)
            {
                issues.Add(new FinancialAuditIssue(
                    "MISSING_SALE_ACCOUNTING_OUTBOX",
                    "Error",
                    "Sales invoice has customer ledger effects but no accounting outbox message.",
                    "SalesInvoice",
                    invoice.Id.ToString()));
            }

            if (postedSaleJournalIds.Contains(invoice.Id))
            {
                continue;
            }

            if (outboxMessagesByInvoiceId.TryGetValue(invoice.Id, out outboxMessages) &&
                outboxMessages.Any(message => message.ProcessedAtUtc == null))
            {
                // S38C: Filter to only unprocessed messages to avoid false positives from historical processed failures
                var unprocessedOutbox = outboxMessages.Where(m => m.ProcessedAtUtc == null).ToList();
                
                // Classify based on actual outbox state with distinct handling for terminal failures:
                // - IsFailed = true: dead-lettered (max retries exceeded) - TERMINAL FAILURE
                // - NextRetryAtUtc in future: scheduled retry after failure - TRANSIENT FAILURE
                // - ClaimedUntilUtc in future: in-flight/leased by worker - PENDING
                // - Otherwise: pending pickup - PENDING
                var now = DateTime.UtcNow;
                var hasDeadLetter = unprocessedOutbox.Any(message => message.IsFailed);
                var hasInFlight = unprocessedOutbox.Any(message => message.ClaimedUntilUtc.HasValue && message.ClaimedUntilUtc.Value > now);
                var hasScheduledRetry = unprocessedOutbox.Any(message => message.NextRetryAtUtc.HasValue && message.NextRetryAtUtc.Value > now);
                
                // Prioritize dead-letter detection (terminal failure)
                if (hasDeadLetter)
                {
                    issues.Add(new FinancialAuditIssue(
                        "SALE_ACCOUNTING_FAILED",
                        "Error",
                        "Sales invoice customer ledger is committed but the accounting journal has exhausted all retries and is dead-lettered. Manual intervention required.",
                        "SalesInvoice",
                        invoice.Id.ToString()));
                }
                else if (hasScheduledRetry)
                {
                    issues.Add(new FinancialAuditIssue(
                        "SALE_ACCOUNTING_RETRYING",
                        "Error",
                        "Sales invoice customer ledger is committed while the accounting journal is retrying after a posting failure.",
                        "SalesInvoice",
                        invoice.Id.ToString()));
                }
                else
                {
                    // In-flight or pending pickup
                    issues.Add(new FinancialAuditIssue(
                        "SALE_ACCOUNTING_PENDING",
                        "Warning",
                        "Sales invoice customer ledger is committed while the accounting journal is still pending.",
                        "SalesInvoice",
                        invoice.Id.ToString()));
                }
                continue;
            }

            issues.Add(new FinancialAuditIssue(
                "MISSING_SALE_ACCOUNTING_JOURNAL",
                "Error",
                "Sales invoice has customer ledger effects but no posted accounting journal.",
                "SalesInvoice",
                invoice.Id.ToString()));
        }
    }

    private async Task ValidateSupplierPaymentLinksAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var supplierPayments = await _context.SupplierPayments
            .AsNoTracking()
            .Select(payment => new { payment.Id, payment.TenantId })
            .ToListAsync(cancellationToken);

        var linkedTransactionIds = await _context.SupplierTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.TransactionType == TransactionType.PaymentMade &&
                transaction.ReferenceId.HasValue)
            .Select(transaction => transaction.ReferenceId!.Value)
            .ToHashSetAsync(cancellationToken);

        var outboxPaymentIds = await _context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message => message.EventType == "SupplierPayment")
            .Select(message => message.BusinessEntityId)
            .ToHashSetAsync(cancellationToken);

        var journalPaymentIds = await _context.JournalEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry => entry.SourceType == "SupplierPayment")
            .Select(entry => entry.SourceId)
            .ToHashSetAsync(cancellationToken);

        foreach (var payment in supplierPayments)
        {
            if (!linkedTransactionIds.Contains(payment.Id))
            {
                issues.Add(new FinancialAuditIssue(
                    "ORPHAN_SUPPLIER_PAYMENT",
                    "Error",
                    "Supplier payment is missing its supplier transaction record.",
                    "SupplierPayment",
                    payment.Id.ToString()));
            }

            if (!outboxPaymentIds.Contains(payment.Id))
            {
                issues.Add(new FinancialAuditIssue(
                    "MISSING_OUTBOX_MESSAGE",
                    "Error",
                    "Supplier payment is missing its accounting outbox message.",
                    "SupplierPayment",
                    payment.Id.ToString()));
            }

            if (!journalPaymentIds.Contains(payment.Id))
            {
                issues.Add(new FinancialAuditIssue(
                    "MISSING_ACCOUNTING_JOURNAL",
                    "Error",
                    "Supplier payment is missing its accounting journal.",
                    "SupplierPayment",
                    payment.Id.ToString()));
            }
        }
    }

    private async Task ValidateExpenseLinksAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var approvedExpenses = await _context.Expenses
            .AsNoTracking()
            .Where(expense => expense.IsApproved && !expense.IsDeleted)
            .Select(expense => new
            {
                expense.Id,
                expense.TenantId,
                expense.Amount,
                expense.AccountingMutationVersion,
            })
            .ToListAsync(cancellationToken);

        var expenseIds = approvedExpenses
            .Select(expense => expense.Id)
            .ToHashSet();

        var outboxMessages = await _context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.EventType == "PosExpense" &&
                expenseIds.Contains(message.BusinessEntityId))
            .Select(message => new
            {
                message.BusinessEntityId,
                message.AggregateVersion,
                message.ProcessedAtUtc,
                message.IsFailed,
                message.AttemptCount,
                message.LastError,
            })
            .ToListAsync(cancellationToken);

        var outboxByExpenseId = outboxMessages
            .GroupBy(message => message.BusinessEntityId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var journalExpenseIds = await _context.JournalEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry =>
                entry.SourceType == "Expense" &&
                expenseIds.Contains(entry.SourceId))
            .Select(entry => entry.SourceId)
            .ToHashSetAsync(cancellationToken);

        foreach (var expense in approvedExpenses)
        {
            if (expense.Amount <= 0m)
            {
                issues.Add(new FinancialAuditIssue(
                    "INVALID_EXPENSE_AMOUNT",
                    "Error",
                    "Approved expense has a non-positive amount and cannot be safely posted.",
                    "Expense",
                    expense.Id.ToString()));
            }

            if (!outboxByExpenseId.TryGetValue(expense.Id, out var messages) ||
                messages.Count == 0)
            {
                issues.Add(new FinancialAuditIssue(
                    "MISSING_OUTBOX_MESSAGE",
                    "Error",
                    "Approved expense is missing its accounting outbox message.",
                    "Expense",
                    expense.Id.ToString()));
            }
            else if (messages.All(message => message.ProcessedAtUtc == null))
            {
                var hasFailure = messages.Any(message => message.IsFailed || message.AttemptCount > 0 || !string.IsNullOrWhiteSpace(message.LastError));
                issues.Add(new FinancialAuditIssue(
                    hasFailure ? "EXPENSE_ACCOUNTING_RETRYING" : "EXPENSE_ACCOUNTING_PENDING",
                    hasFailure ? "Warning" : "Warning",
                    hasFailure
                        ? "Approved expense accounting outbox has failed attempts and is still awaiting a successful journal post."
                        : "Approved expense accounting outbox is pending journal post.",
                    "Expense",
                    expense.Id.ToString()));
            }

            if (!journalExpenseIds.Contains(expense.Id) &&
                (!outboxByExpenseId.TryGetValue(expense.Id, out var currentMessages) ||
                 currentMessages.All(message => message.ProcessedAtUtc != null)))
            {
                issues.Add(new FinancialAuditIssue(
                    "MISSING_ACCOUNTING_JOURNAL",
                    "Error",
                    "Approved expense is missing its accounting journal.",
                    "Expense",
                    expense.Id.ToString()));
            }
        }
    }

    private async Task ValidateAdjustmentLinksAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var adjustments = await _context.PartyLedgerAdjustments
            .AsNoTracking()
            .Select(adjustment => new
            {
                adjustment.Id,
                adjustment.PartyType,
                adjustment.TenantId,
            })
            .ToListAsync(cancellationToken);

        var customerReferenceIds = await _context.CustomerTransactions
            .AsNoTracking()
            .Where(transaction => transaction.ReferenceId.HasValue)
            .Select(transaction => transaction.ReferenceId!.Value)
            .ToHashSetAsync(cancellationToken);

        var supplierReferenceIds = await _context.SupplierTransactions
            .AsNoTracking()
            .Where(transaction => transaction.ReferenceId.HasValue)
            .Select(transaction => transaction.ReferenceId!.Value)
            .ToHashSetAsync(cancellationToken);

        var outboxAdjustmentIds = await _context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message => message.EventType == "PartyLedgerAdjustment")
            .Select(message => message.BusinessEntityId)
            .ToHashSetAsync(cancellationToken);

        var journalAdjustmentIds = await _context.JournalEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry => entry.SourceType == "PartyLedgerAdjustment")
            .Select(entry => entry.SourceId)
            .ToHashSetAsync(cancellationToken);

        foreach (var adjustment in adjustments)
        {
            var isLinked = adjustment.PartyType == PartyType.Customer
                ? customerReferenceIds.Contains(adjustment.Id)
                : supplierReferenceIds.Contains(adjustment.Id);

            if (!isLinked)
            {
                issues.Add(new FinancialAuditIssue(
                    "ORPHAN_ADJUSTMENT",
                    "Error",
                    "Party ledger adjustment is missing its corresponding party transaction record.",
                    "PartyLedgerAdjustment",
                    adjustment.Id.ToString()));
            }

            if (!outboxAdjustmentIds.Contains(adjustment.Id))
            {
                issues.Add(new FinancialAuditIssue(
                    "MISSING_OUTBOX_MESSAGE",
                    "Error",
                    "Party ledger adjustment is missing its accounting outbox message.",
                    "PartyLedgerAdjustment",
                    adjustment.Id.ToString()));
            }

            if (!journalAdjustmentIds.Contains(adjustment.Id))
            {
                issues.Add(new FinancialAuditIssue(
                    "MISSING_ACCOUNTING_JOURNAL",
                    "Error",
                    "Party ledger adjustment is missing its accounting journal.",
                    "PartyLedgerAdjustment",
                    adjustment.Id.ToString()));
            }
        }
    }

    private async Task ValidatePurchaseReturnLinksAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var purchaseReturns = await _context.Returns
            .AsNoTracking()
            .Where(returnObj => returnObj.ReturnType == ReturnType.PurchaseReturn)
            .Select(returnObj => new
            {
                returnObj.Id,
                returnObj.TenantId,
                returnObj.OriginalPurchaseInvoiceId
            })
            .ToListAsync(cancellationToken);

        var purchaseInvoiceIds = purchaseReturns
            .Where(returnObj => returnObj.OriginalPurchaseInvoiceId.HasValue)
            .Select(returnObj => returnObj.OriginalPurchaseInvoiceId!.Value)
            .ToHashSet();
        var authoritativePurchaseInvoiceIds = await _context.PurchaseInvoices
            .AsNoTracking()
            .Where(invoice => purchaseInvoiceIds.Contains(invoice.Id))
            .Select(invoice => invoice.Id)
            .ToHashSetAsync(cancellationToken);

        var supplierReferenceIds = await _context.SupplierTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.TransactionType == TransactionType.PurchaseReturn &&
                transaction.ReferenceId.HasValue)
            .Select(transaction => transaction.ReferenceId!.Value)
            .ToHashSetAsync(cancellationToken);

        var purchaseReturnRequestIds = await _context.PurchaseReturnRequestIdempotencyRecords
            .AsNoTracking()
            .Where(record => record.ReturnId.HasValue)
            .Select(record => record.ReturnId!.Value)
            .ToHashSetAsync(cancellationToken);

        var outboxReturnIds = await _context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message => message.EventType == "PurchaseReturn")
            .Select(message => message.BusinessEntityId)
            .ToHashSetAsync(cancellationToken);

        var journalReturnIds = await _context.JournalEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry => entry.SourceType == "PurchaseReturn")
            .Select(entry => entry.SourceId)
            .ToHashSetAsync(cancellationToken);

        foreach (var purchaseReturn in purchaseReturns)
        {
            if (!purchaseReturn.OriginalPurchaseInvoiceId.HasValue ||
                !authoritativePurchaseInvoiceIds.Contains(purchaseReturn.OriginalPurchaseInvoiceId.Value))
            {
                issues.Add(new FinancialAuditIssue(
                    "INVALID_PURCHASE_RETURN_LINEAGE",
                    "Error",
                    "Purchase return is missing its authoritative purchase invoice lineage.",
                    "PurchaseReturn",
                    purchaseReturn.Id.ToString()));
            }

            if (!supplierReferenceIds.Contains(purchaseReturn.Id))
            {
                issues.Add(new FinancialAuditIssue(
                    "ORPHAN_PURCHASE_RETURN",
                    "Error",
                    "Purchase return is missing its supplier transaction record.",
                    "PurchaseReturn",
                    purchaseReturn.Id.ToString()));
            }

            if (!purchaseReturnRequestIds.Contains(purchaseReturn.Id))
            {
                issues.Add(new FinancialAuditIssue(
                    "MISSING_IDEMPOTENCY_RECORD",
                    "Error",
                    "Purchase return is missing its request idempotency record.",
                    "PurchaseReturn",
                    purchaseReturn.Id.ToString()));
            }

            if (!outboxReturnIds.Contains(purchaseReturn.Id))
            {
                issues.Add(new FinancialAuditIssue(
                    "MISSING_OUTBOX_MESSAGE",
                    "Error",
                    "Purchase return is missing its accounting outbox message.",
                    "PurchaseReturn",
                    purchaseReturn.Id.ToString()));
            }

            if (!journalReturnIds.Contains(purchaseReturn.Id))
            {
                issues.Add(new FinancialAuditIssue(
                    "MISSING_ACCOUNTING_JOURNAL",
                    "Error",
                    "Purchase return is missing its accounting journal.",
                    "PurchaseReturn",
                    purchaseReturn.Id.ToString()));
            }
        }
    }

    private async Task ValidateDuplicateAccountingEntriesAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var duplicates = await _context.JournalEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry => !string.IsNullOrWhiteSpace(entry.IdempotencyKey))
            .GroupBy(entry => new { entry.TenantId, entry.IdempotencyKey })
            .Where(group => group.Count() > 1)
            .Select(group => new
            {
                group.Key.TenantId,
                group.Key.IdempotencyKey,
                Count = group.Count(),
            })
            .ToListAsync(cancellationToken);

        foreach (var duplicate in duplicates)
        {
            issues.Add(new FinancialAuditIssue(
                "DUPLICATE_ACCOUNTING_ENTRY",
                "Error",
                $"Duplicate journal idempotency key '{duplicate.IdempotencyKey}' detected ({duplicate.Count} entries).",
                "JournalEntry",
                duplicate.TenantId.ToString()));
        }
    }

    private async Task ValidateDuplicateOutboxMessagesAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var duplicates = await _context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message => !string.IsNullOrWhiteSpace(message.IdempotencyKey))
            .GroupBy(message => new { message.TenantId, message.IdempotencyKey })
            .Where(group => group.Count() > 1)
            .Select(group => new
            {
                group.Key.TenantId,
                group.Key.IdempotencyKey,
                Count = group.Count(),
            })
            .ToListAsync(cancellationToken);

        foreach (var duplicate in duplicates)
        {
            issues.Add(new FinancialAuditIssue(
                "DUPLICATE_OUTBOX_MESSAGE",
                "Error",
                $"Duplicate accounting outbox idempotency key '{duplicate.IdempotencyKey}' detected ({duplicate.Count} messages).",
                "AccountingOutboxMessage",
                duplicate.TenantId.ToString()));
        }
    }

    private async Task ValidateLegacyRefundOutboxMessagesAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var pendingRefundMessages = await _context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.EventType == "PosRefund" &&
                message.ProcessedAtUtc == null)
            .Select(message => new
            {
                message.Id,
                message.BusinessEntityId,
                message.PayloadJson,
            })
            .ToListAsync(cancellationToken);

        foreach (var message in pendingRefundMessages)
        {
            try
            {
                using var payload = JsonDocument.Parse(message.PayloadJson);
                if (HasExplicitRefundAllocation(payload.RootElement))
                {
                    continue;
                }

                issues.Add(new FinancialAuditIssue(
                    "LEGACY_REFUND_OUTBOX_MESSAGE",
                    "Error",
                    "Pending POS refund outbox payload is missing explicit refund allocation and will require replay or remediation before deployment.",
                    "AccountingOutboxMessage",
                    message.Id.ToString()));
            }
            catch (JsonException)
            {
                issues.Add(new FinancialAuditIssue(
                    "INVALID_REFUND_OUTBOX_PAYLOAD",
                    "Error",
                    "Pending POS refund outbox payload is invalid JSON.",
                    "AccountingOutboxMessage",
                    message.Id.ToString()));
            }
        }
    }

    private async Task ValidateLegacySharedCashAccountBalancesAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var cashboxTreasuryAccounts = await _context.TreasuryAccounts
            .AsNoTracking()
            .Include(account => account.Account)
            .Where(account =>
                account.Type == TreasuryAccountType.Cashbox &&
                account.IsActive)
            .ToListAsync(cancellationToken);

        var dedicatedReferenceCodes = cashboxTreasuryAccounts
            .Where(account => !string.IsNullOrWhiteSpace(account.ReferenceCode))
            .Select(account => account.ReferenceCode!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var account in cashboxTreasuryAccounts.Where(account =>
                     string.IsNullOrWhiteSpace(account.ReferenceCode) &&
                     string.Equals(account.Account?.AccountCode, AccountCodes.CashboxMain, StringComparison.OrdinalIgnoreCase)))
        {
            var balance = account.Account?.CurrentBalance ?? 0m;
            if (balance == 0m)
            {
                continue;
            }

            var message = dedicatedReferenceCodes.Count > 0
                ? $"Shared cash account '{account.Name}' still holds balance {balance:N2} even though dedicated cashbox accounts exist."
                : $"Shared cash account '{account.Name}' still holds balance {balance:N2} and no dedicated cashbox target is provisioned.";

            issues.Add(new FinancialAuditIssue(
                "LEGACY_SHARED_CASH_ACCOUNT_BALANCE",
                "Error",
                message,
                "TreasuryAccount",
                account.Id.ToString()));
        }

        var cashboxes = await _context.Cashboxes
            .AsNoTracking()
            .Select(cashbox => new { cashbox.Id, cashbox.Name })
            .ToListAsync(cancellationToken);

        foreach (var cashbox in cashboxes)
        {
            if (dedicatedReferenceCodes.Contains(cashbox.Id.ToString("N")))
            {
                continue;
            }

            issues.Add(new FinancialAuditIssue(
                "MISSING_DEDICATED_CASHBOX_ACCOUNT",
                "Error",
                $"Cashbox '{cashbox.Name}' has no dedicated treasury ledger account mapping.",
                "Cashbox",
                cashbox.Id.ToString()));
        }
    }

    private async Task ValidateMutationIdempotencyCoverageAsync(
        ICollection<FinancialAuditIssue> issues,
        CancellationToken cancellationToken)
    {
        var supplierPaymentsMissingKeys = await _context.SupplierPayments
            .AsNoTracking()
            .Where(payment => string.IsNullOrWhiteSpace(payment.IdempotencyKey))
            .Select(payment => payment.Id)
            .ToListAsync(cancellationToken);

        foreach (var paymentId in supplierPaymentsMissingKeys)
        {
            issues.Add(new FinancialAuditIssue(
                "MISSING_IDEMPOTENCY_KEY",
                "Error",
                "Supplier payment is missing an idempotency key.",
                "SupplierPayment",
                paymentId.ToString()));
        }

        var adjustmentMissingKeys = await _context.PartyLedgerAdjustments
            .AsNoTracking()
            .Where(adjustment => string.IsNullOrWhiteSpace(adjustment.IdempotencyKey))
            .Select(adjustment => adjustment.Id)
            .ToListAsync(cancellationToken);

        foreach (var adjustmentId in adjustmentMissingKeys)
        {
            issues.Add(new FinancialAuditIssue(
                "MISSING_IDEMPOTENCY_KEY",
                "Error",
                "Party ledger adjustment is missing an idempotency key.",
                "PartyLedgerAdjustment",
                adjustmentId.ToString()));
        }
    }

    private static async Task RunValidationCheckAsync(
        string name,
        Func<ICollection<FinancialAuditIssue>, CancellationToken, Task> validator,
        ICollection<FinancialAuditIssue> issues,
        ICollection<FinancialAuditCheckResult> checks,
        CancellationToken cancellationToken)
    {
        var issueCountBefore = issues.Count;
        await validator(issues, cancellationToken);
        var issueCount = issues.Count - issueCountBefore;

        checks.Add(new FinancialAuditCheckResult(
            name,
            Passed: issueCount == 0,
            IssueCount: issueCount));
    }

    private static string BuildLedgerMatchKey(
        PartyType partyType,
        Guid partyId,
        TransactionType transactionType,
        Guid? referenceId,
        string? referenceNumber,
        decimal debitAmount,
        decimal creditAmount)
    {
        return string.Join("|",
            partyType,
            partyId,
            transactionType,
            referenceId?.ToString() ?? "NULL",
            referenceNumber ?? string.Empty,
            debitAmount,
            creditAmount);
    }

    private static bool HasExplicitRefundAllocation(JsonElement payload) =>
        payload.TryGetProperty("RefundMethod", out _) &&
        payload.TryGetProperty("CashAmount", out _) &&
        payload.TryGetProperty("CardAmount", out _);

    private static string? NormalizeBalanceType(string? balanceType)
    {
        if (string.IsNullOrWhiteSpace(balanceType))
        {
            return null;
        }

        if (string.Equals(balanceType, "Debit", StringComparison.OrdinalIgnoreCase))
        {
            return "Debit";
        }

        if (string.Equals(balanceType, "Credit", StringComparison.OrdinalIgnoreCase))
        {
            return "Credit";
        }

        return balanceType.Trim();
    }
}
