using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Core.Models.Refunds;
using PointOfSale.Infrastructure.Data;
using PaymentMethodEnum = PointOfSale.Core.Enums.PaymentMethod;

namespace PointOfSale.Infrastructure.Accounting;


/// <summary>
/// Commerce-to-Ledger integration service.
/// Translates POS and E-commerce operations into journal entries.
/// 
/// PHASE 3: All business operations flow through this service to the ledger.
/// </summary>
public class CommerceAccountingService : ICommerceAccountingService
{
    private readonly IFinancialPostingService _postingService;
    private readonly IAccountResolver _accountResolver;
    private readonly ICashboxLedgerAccountService _cashboxLedgerAccountService;
    private readonly ILogger<CommerceAccountingService> _logger;
    private readonly ApplicationDbContext? _db;
    
    private static readonly Regex OperationKeyPattern = new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);
    
    public CommerceAccountingService(
        IFinancialPostingService postingService,
        IAccountResolver accountResolver,
        ICashboxLedgerAccountService cashboxLedgerAccountService,
        ILogger<CommerceAccountingService> logger,
        ApplicationDbContext? db = null)
    {
        _postingService = postingService;
        _accountResolver = accountResolver;
        _cashboxLedgerAccountService = cashboxLedgerAccountService;
        _logger = logger;
        _db = db;
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // POS OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    public async Task<PostingResult> PostPosSaleAsync(
        SalesInvoice invoice,
        CancellationToken ct = default,
        string? operationKey = null)
    {
        // Validate operationKey - reject empty/whitespace strings and invalid characters to prevent silent key reuse and delimiter conflicts
        if (operationKey != null)
        {
            if (string.IsNullOrWhiteSpace(operationKey))
            {
                throw new ArgumentException(
                    "Operation key cannot be empty or whitespace. Pass null to use the base idempotency key, or provide a valid non-empty key.",
                    nameof(operationKey));
            }

            if (!OperationKeyPattern.IsMatch(operationKey))
            {
                throw new ArgumentException(
                    "Operation key must contain only alphanumeric characters, dots, underscores, or hyphens. " +
                    "Invalid characters like ':', control characters (newline, tab, null), and other special characters are not allowed.",
                    nameof(operationKey));
            }
        }
        
        try
        {
            _logger.LogInformation(
                "Posting POS sale {InvoiceNumber} for tenant {TenantId}, Amount: {Amount}",
                invoice.InvoiceNumber, invoice.TenantId, invoice.TotalAmount);

            // Get required accounts (excluding cash account - resolved conditionally)
            var accounts = await _accountResolver.GetByCodesAsync(
                invoice.TenantId,
                new[] { AccountCodes.VisaClearing, AccountCodes.PosSales, AccountCodes.SalesTaxPayable,
                        AccountCodes.CostOfGoodsSold, AccountCodes.Inventory },
                ct);
            
            // ═══════════════════════════════════════════════════════════════════
            // JOURNAL 1: Revenue Recognition
            // ═══════════════════════════════════════════════════════════════════
            var revenueLines = new List<PostingLine>();
            
            // Determine debit accounts based on payment method
            switch (invoice.PaymentMethod)
            {
                case PaymentMethodEnum.Cash:
                    var cashAccountCodeForCash = await ResolveWarehouseCashboxAccountCodeAsync(
                        invoice.TenantId,
                        invoice.WarehouseId,
                        ct);
                    var cashAccountForCash = await _accountResolver.GetByCodeAsync(
                        invoice.TenantId, cashAccountCodeForCash, ct);
                    revenueLines.Add(PostingLine.Debit(
                        cashAccountForCash.Id,
                        invoice.TotalAmount,
                        $"Cash sale {invoice.InvoiceNumber}"));
                    break;
                    
                case PaymentMethodEnum.Card:
                    revenueLines.Add(PostingLine.Debit(
                        accounts[AccountCodes.VisaClearing].Id,
                        invoice.TotalAmount,
                        $"Card sale {invoice.InvoiceNumber}"));
                    break;
                    
                case PaymentMethodEnum.Mixed:
                    if (invoice.CashAmount > 0)
                    {
                        var cashAccountCodeForMixed = await ResolveWarehouseCashboxAccountCodeAsync(
                            invoice.TenantId,
                            invoice.WarehouseId,
                            ct);
                        var cashAccountForMixed = await _accountResolver.GetByCodeAsync(
                            invoice.TenantId, cashAccountCodeForMixed, ct);
                        revenueLines.Add(PostingLine.Debit(
                            cashAccountForMixed.Id,
                            invoice.CashAmount.Value,
                            $"Cash portion {invoice.InvoiceNumber}"));
                    }
                    if (invoice.CardAmount > 0)
                    {
                        revenueLines.Add(PostingLine.Debit(
                            accounts[AccountCodes.VisaClearing].Id,
                            invoice.CardAmount.Value,
                            $"Card portion {invoice.InvoiceNumber}"));
                    }
                    break;
                    
                case PaymentMethodEnum.Credit:
                    // Credit sale - debit accounts receivable
                    var arAccount = await _accountResolver.GetByCodeAsync(
                        invoice.TenantId, AccountCodes.AccountsReceivable, ct);
                    revenueLines.Add(PostingLine.Debit(
                        arAccount.Id,
                        invoice.TotalAmount,
                        $"Credit sale {invoice.InvoiceNumber}"));
                    break;
                    
                default:
                    var cashAccountCodeForDefault = await ResolveWarehouseCashboxAccountCodeAsync(
                        invoice.TenantId,
                        invoice.WarehouseId,
                        ct);
                    var cashAccountForDefault = await _accountResolver.GetByCodeAsync(
                        invoice.TenantId, cashAccountCodeForDefault, ct);
                    revenueLines.Add(PostingLine.Debit(
                        cashAccountForDefault.Id,
                        invoice.TotalAmount,
                        $"Sale {invoice.InvoiceNumber}"));
                    break;
            }
            
            var netRevenue = invoice.TotalAmount - invoice.TaxAmount;
            if (netRevenue > 0m)
            {
                revenueLines.Add(PostingLine.Credit(
                    accounts[AccountCodes.PosSales].Id,
                    netRevenue,
                    $"Revenue {invoice.InvoiceNumber}"));
            }
            if (invoice.TaxAmount > 0m)
            {
                revenueLines.Add(PostingLine.Credit(
                    accounts[AccountCodes.SalesTaxPayable].Id,
                    invoice.TaxAmount,
                    $"Sales tax {invoice.InvoiceNumber}"));
            }
            
            var revenueRequest = new PostingRequest
            {
                TenantId = invoice.TenantId,
                JournalDate = invoice.InvoiceDate,
                SourceType = "SalesInvoice",
                SourceId = invoice.Id,
                SourceReference = invoice.InvoiceNumber,
                Description = $"POS Sale {invoice.InvoiceNumber}",
                IdempotencyKey = BuildSaleJournalIdempotencyKey("SALE", invoice.TenantId, invoice.Id, operationKey),
                Lines = revenueLines
            };
            
            var revenueJournal = await _postingService.PostJournalAsync(revenueRequest, ct);
            
            // ═══════════════════════════════════════════════════════════════════
            // JOURNAL 2: Cost of Goods Sold (if we have item cost data)
            // ═══════════════════════════════════════════════════════════════════
            var totalCost = CalculateTotalCost(invoice);
            if (totalCost > 0)
            {
                var cogsRequest = new PostingRequest
                {
                    TenantId = invoice.TenantId,
                    JournalDate = invoice.InvoiceDate,
                    SourceType = "SalesInvoice_COGS",
                    SourceId = invoice.Id,
                    SourceReference = invoice.InvoiceNumber,
                    Description = $"COGS for {invoice.InvoiceNumber}",
                    IdempotencyKey = BuildSaleJournalIdempotencyKey("COGS", invoice.TenantId, invoice.Id, operationKey),
                    Lines = new List<PostingLine>
                    {
                        PostingLine.Debit(accounts[AccountCodes.CostOfGoodsSold].Id, totalCost, "Cost of Goods Sold"),
                        PostingLine.Credit(accounts[AccountCodes.Inventory].Id, totalCost, "Inventory Reduction")
                    }
                };
                
                await _postingService.PostJournalAsync(cogsRequest, ct);
            }
            
            _logger.LogInformation(
                "Posted POS sale {InvoiceNumber}, JournalNumber: {JournalNumber}",
                invoice.InvoiceNumber, revenueJournal.JournalNumber);
            
            return PostingResult.Succeeded(revenueJournal.Id, revenueJournal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post POS sale {InvoiceId}", invoice.Id);
            return PostingResult.Failed(ex.Message);
        }
    }
    
    public async Task<PostingResult> PostPosPurchaseAsync(
        PurchaseInvoice invoice,
        CancellationToken ct = default,
        string? operationKey = null,
        decimal? cashAmount = null,
        decimal? cardAmount = null,
        Guid? fundingInstrumentId = null,
        Guid? fundingLedgerAccountId = null,
        int? fundingInstrumentVersion = null)
    {
        if (operationKey != null)
        {
            if (string.IsNullOrWhiteSpace(operationKey))
            {
                throw new ArgumentException(
                    "Operation key cannot be empty or whitespace. Pass null to use the base idempotency key, or provide a valid non-empty key.",
                    nameof(operationKey));
            }

            if (!OperationKeyPattern.IsMatch(operationKey))
            {
                throw new ArgumentException(
                    "Operation key must contain only alphanumeric characters, dots, underscores, or hyphens. " +
                    "Invalid characters like ':', control characters (newline, tab, null), and other special characters are not allowed.",
                    nameof(operationKey));
            }
        }

        try
        {
            _logger.LogInformation(
                "Posting POS purchase {InvoiceNumber} for tenant {TenantId}",
                invoice.InvoiceNumber, invoice.TenantId);

            var normalizedCashAmount = cashAmount ?? 0m;
            var normalizedCardAmount = cardAmount ?? 0m;
            var explicitSplitProvided = cashAmount.HasValue || cardAmount.HasValue;

            if (!explicitSplitProvided && invoice.PaidAmount > 0)
            {
                switch (invoice.PaymentMethod)
                {
                    case PaymentMethodEnum.Cash:
                        normalizedCashAmount = invoice.PaidAmount;
                        break;
                    case PaymentMethodEnum.Card:
                        normalizedCardAmount = invoice.PaidAmount;
                        break;
                    case PaymentMethodEnum.Mixed:
                        throw new InvalidOperationException(
                            $"Purchase invoice {invoice.InvoiceNumber} requires explicit cash/card split for mixed payment posting.");
                    case PaymentMethodEnum.Credit:
                        throw new InvalidOperationException(
                            $"Purchase invoice {invoice.InvoiceNumber} requires explicit payment split for partial credit purchase posting.");
                }
            }

            var paidPortion = normalizedCashAmount + normalizedCardAmount;
            if (paidPortion != invoice.PaidAmount)
            {
                throw new InvalidOperationException(
                    $"Purchase invoice {invoice.InvoiceNumber} payment split does not match PaidAmount. PaidAmount={invoice.PaidAmount}, Cash={normalizedCashAmount}, Card={normalizedCardAmount}.");
            }

            if (paidPortion > invoice.TotalAmount)
            {
                throw new InvalidOperationException(
                    $"Purchase invoice {invoice.InvoiceNumber} cannot pay more than the invoice total. Total={invoice.TotalAmount}, Paid={paidPortion}.");
            }

            var requiredAccountCodes = new List<string>
            {
                AccountCodes.Inventory,
                AccountCodes.InputTaxReceivable,
                AccountCodes.SupplierPayables
            };

            string? cashAccountCode = null;

            if (normalizedCashAmount > 0)
            {
                cashAccountCode = await ResolveWarehouseCashboxAccountCodeAsync(
                    invoice.TenantId,
                    invoice.WarehouseId,
                    ct);
                requiredAccountCodes.Add(cashAccountCode);
            }

            if (normalizedCardAmount > 0)
            {
                if (!fundingInstrumentId.HasValue || !fundingLedgerAccountId.HasValue || !fundingInstrumentVersion.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Purchase invoice {invoice.InvoiceNumber} Card payment requires frozen supplier card funding authority.");
                }
            }
            else if (fundingInstrumentId.HasValue || fundingLedgerAccountId.HasValue || fundingInstrumentVersion.HasValue)
            {
                throw new InvalidOperationException(
                    $"Purchase invoice {invoice.InvoiceNumber} cannot carry supplier card funding authority without a Card amount.");
            }

            var accounts = await _accountResolver.GetByCodesAsync(
                invoice.TenantId,
                requiredAccountCodes,
                ct);
            
            var inventoryAmount = invoice.TotalAmount - invoice.TaxAmount;
            var lines = new List<PostingLine>();
            if (inventoryAmount > 0m)
            {
                lines.Add(PostingLine.Debit(
                    accounts[AccountCodes.Inventory].Id,
                    inventoryAmount,
                    $"Purchase {invoice.InvoiceNumber}"));
            }
            if (invoice.TaxAmount > 0m)
            {
                lines.Add(PostingLine.Debit(
                    accounts[AccountCodes.InputTaxReceivable].Id,
                    invoice.TaxAmount,
                    $"Input tax {invoice.InvoiceNumber}"));
            }

            if (normalizedCashAmount > 0)
            {
                lines.Add(PostingLine.Credit(
                    accounts[cashAccountCode!].Id,
                    normalizedCashAmount,
                    $"Cash payment for {invoice.InvoiceNumber}"));
            }

            if (normalizedCardAmount > 0)
            {
                lines.Add(PostingLine.Credit(
                    fundingLedgerAccountId!.Value,
                    normalizedCardAmount,
                    $"Funded company card payment for {invoice.InvoiceNumber}"));
            }

            var supplierPayableAmount = invoice.TotalAmount - paidPortion;
            if (supplierPayableAmount > 0)
            {
                lines.Add(PostingLine.Credit(
                    accounts[AccountCodes.SupplierPayables].Id,
                    supplierPayableAmount,
                    $"Credit balance {invoice.InvoiceNumber}"));
            }
            
            var request = new PostingRequest
            {
                TenantId = invoice.TenantId,
                JournalDate = invoice.InvoiceDate,
                SourceType = "PurchaseInvoice",
                SourceId = invoice.Id,
                SourceReference = invoice.InvoiceNumber,
                Description = $"Purchase {invoice.InvoiceNumber}",
                IdempotencyKey = BuildPurchaseJournalIdempotencyKey(invoice.TenantId, invoice.Id, operationKey),
                Lines = lines
            };
            
            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post purchase {InvoiceId}", invoice.Id);
            return PostingResult.Failed(ex.Message);
        }
    }

    private static string BuildPurchaseJournalIdempotencyKey(Guid tenantId, Guid invoiceId, string? operationKey) =>
        operationKey == null
            ? $"PURCHASE:{tenantId}:{invoiceId}"
            : $"PURCHASE:{tenantId}:{invoiceId}:{operationKey}";
    
    public async Task<PostingResult> PostSupplierPaymentAsync(SupplierPayment payment, CancellationToken ct = default)
    {
        try
        {
            var tenantId = payment.TenantId;

            var fundingAccountCode = payment.PaymentMethod switch
            {
                PaymentMethodEnum.Cash => await ResolveDefaultCashboxAccountCodeAsync(tenantId, ct),
                PaymentMethodEnum.Card => null,
                PaymentMethodEnum.BankTransfer => AccountCodes.BankAccount,
                PaymentMethodEnum.Credit => throw new InvalidOperationException("Supplier payments cannot use credit as a funding source."),
                PaymentMethodEnum.Mixed => throw new InvalidOperationException("Supplier payments require a single concrete funding source."),
                _ => throw new InvalidOperationException($"Unsupported supplier payment method '{payment.PaymentMethod}'."),
            };

            if (payment.PaymentMethod == PaymentMethodEnum.Card &&
                (!payment.FundingInstrumentId.HasValue || !payment.FundingLedgerAccountId.HasValue || !payment.FundingInstrumentVersion.HasValue))
            {
                throw new InvalidOperationException("Supplier Card payment requires frozen funding instrument authority.");
            }
            if (payment.PaymentMethod != PaymentMethodEnum.Card &&
                (payment.FundingInstrumentId.HasValue || payment.FundingLedgerAccountId.HasValue || payment.FundingInstrumentVersion.HasValue))
            {
                throw new InvalidOperationException("Non-Card supplier payment cannot carry card funding authority.");
            }

            var requiredCodes = fundingAccountCode == null
                ? new[] { AccountCodes.SupplierPayables }
                : new[] { AccountCodes.SupplierPayables, fundingAccountCode };
            var accounts = await _accountResolver.GetByCodesAsync(
                tenantId,
                requiredCodes,
                ct);
            var fundingAccountId = payment.PaymentMethod == PaymentMethodEnum.Card
                ? payment.FundingLedgerAccountId!.Value
                : accounts[fundingAccountCode!].Id;
            
            var request = new PostingRequest
            {
                TenantId = tenantId,
                JournalDate = payment.PaymentDate,
                SourceType = "SupplierPayment",
                SourceId = payment.Id,
                SourceReference = payment.ReferenceNumber,
                Description = $"Supplier payment {payment.ReferenceNumber}",
                IdempotencyKey = $"SUPPLIER_PAYMENT:{tenantId}:{payment.Id}",
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(accounts[AccountCodes.SupplierPayables].Id, payment.Amount, "Reduce payables"),
                    PostingLine.Credit(
                        fundingAccountId,
                        payment.Amount,
                        $"Supplier payment funding via {payment.PaymentMethod}")
                }
            };
            
            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post supplier payment {PaymentId}", payment.Id);
            return PostingResult.Failed(ex.Message);
        }
    }

    public async Task<PostingResult> PostSupplierOpeningBalanceAsync(AccountLedger openingBalance, CancellationToken ct = default)
    {
        try
        {
            if (openingBalance.PartyType != PartyType.Supplier)
            {
                throw new InvalidOperationException(
                    $"Supplier opening balance posting requires PartyType.Supplier, but received {openingBalance.PartyType}.");
            }

            if (openingBalance.TransactionType != TransactionType.OpeningBalance)
            {
                throw new InvalidOperationException(
                    $"Supplier opening balance posting requires TransactionType.OpeningBalance, but received {openingBalance.TransactionType}.");
            }

            if (openingBalance.DebitAmount <= 0m && openingBalance.CreditAmount <= 0m)
            {
                throw new InvalidOperationException(
                    $"Supplier opening balance {openingBalance.Id} must contain a debit or credit amount.");
            }

            if (openingBalance.DebitAmount > 0m && openingBalance.CreditAmount > 0m)
            {
                throw new InvalidOperationException(
                    $"Supplier opening balance {openingBalance.Id} cannot have both debit and credit amounts positive.");
            }

            var tenantId = openingBalance.TenantId;
            var accounts = await _accountResolver.GetByCodesAsync(
                tenantId,
                new[]
                {
                    AccountCodes.SupplierPayables,
                    AccountCodes.OpeningBalanceEquity
                },
                ct);

            var lines = new List<PostingLine>
            {
                openingBalance.DebitAmount > 0m
                    ? PostingLine.Debit(
                        accounts[AccountCodes.SupplierPayables].Id,
                        openingBalance.DebitAmount,
                        $"Supplier opening balance {openingBalance.DocumentNumber}")
                    : PostingLine.Debit(
                        accounts[AccountCodes.OpeningBalanceEquity].Id,
                        openingBalance.CreditAmount,
                        $"Opening balance offset {openingBalance.DocumentNumber}"),
                openingBalance.DebitAmount > 0m
                    ? PostingLine.Credit(
                        accounts[AccountCodes.OpeningBalanceEquity].Id,
                        openingBalance.DebitAmount,
                        $"Opening balance offset {openingBalance.DocumentNumber}")
                    : PostingLine.Credit(
                        accounts[AccountCodes.SupplierPayables].Id,
                        openingBalance.CreditAmount,
                        $"Supplier opening balance {openingBalance.DocumentNumber}")
            };

            var request = new PostingRequest
            {
                TenantId = tenantId,
                JournalDate = openingBalance.TransactionDate,
                SourceType = "SupplierOpeningBalance",
                SourceId = openingBalance.Id,
                SourceReference = openingBalance.DocumentNumber,
                Description = $"Supplier opening balance {openingBalance.DocumentNumber}",
                IdempotencyKey = $"SUPPLIER_OPENING_BALANCE:{tenantId}:{openingBalance.Id}",
                Lines = lines
            };

            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post supplier opening balance for supplier {SupplierId}", openingBalance.PartyId);
            return PostingResult.Failed(ex.Message);
        }
    }

    public async Task<PostingResult> PostCustomerPaymentAsync(CustomerPayment payment, CancellationToken ct = default)
    {
        try
        {
            var tenantId = payment.TenantId;
            var cashboxAccountCode = payment.CashAmount > 0m
                ? await ResolveDefaultCashboxAccountCodeAsync(tenantId, ct)
                : null;
            var requiredAccountCodes = new HashSet<string>(StringComparer.Ordinal)
            {
                AccountCodes.AccountsReceivable
            };

            if (cashboxAccountCode != null)
            {
                requiredAccountCodes.Add(cashboxAccountCode);
            }
            if (payment.CardAmount > 0m)
            {
                requiredAccountCodes.Add(AccountCodes.VisaClearing);
            }
            if (payment.BankTransferAmount > 0m)
            {
                requiredAccountCodes.Add(AccountCodes.BankAccount);
            }

            var accounts = await _accountResolver.GetByCodesAsync(tenantId, requiredAccountCodes, ct);
            var lines = new List<PostingLine>();
            if (payment.CashAmount > 0m)
            {
                lines.Add(PostingLine.Debit(accounts[cashboxAccountCode!].Id, payment.CashAmount, "Customer cash received"));
            }
            if (payment.CardAmount > 0m)
            {
                lines.Add(PostingLine.Debit(accounts[AccountCodes.VisaClearing].Id, payment.CardAmount, "Customer card payment received"));
            }
            if (payment.BankTransferAmount > 0m)
            {
                lines.Add(PostingLine.Debit(accounts[AccountCodes.BankAccount].Id, payment.BankTransferAmount, "Customer bank transfer received"));
            }
            lines.Add(PostingLine.Credit(accounts[AccountCodes.AccountsReceivable].Id, payment.Amount, "Reduce customer receivable"));

            var request = new PostingRequest
            {
                TenantId = tenantId,
                JournalDate = payment.PaymentDate,
                SourceType = "CustomerPayment",
                SourceId = payment.Id,
                SourceReference = payment.ReferenceNumber,
                Description = $"Customer payment {payment.ReferenceNumber}",
                IdempotencyKey = $"CUSTOMER_PAYMENT:{tenantId}:{payment.Id}",
                Lines = lines
            };

            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post customer payment {PaymentId}", payment.Id);
            return PostingResult.Failed(ex.Message);
        }
    }

    public async Task<PostingResult> PostCustomerOpeningBalanceAsync(AccountLedger openingBalance, CancellationToken ct = default)
    {
        try
        {
            if (openingBalance.PartyType != PartyType.Customer)
            {
                throw new InvalidOperationException(
                    $"Customer opening balance posting requires PartyType.Customer, but received {openingBalance.PartyType}.");
            }

            if (openingBalance.TransactionType != TransactionType.OpeningBalance)
            {
                throw new InvalidOperationException(
                    $"Customer opening balance posting requires TransactionType.OpeningBalance, but received {openingBalance.TransactionType}.");
            }

            if (openingBalance.DebitAmount <= 0m && openingBalance.CreditAmount <= 0m)
            {
                throw new InvalidOperationException(
                    $"Customer opening balance {openingBalance.Id} must contain a debit or credit amount.");
            }

            if (openingBalance.DebitAmount > 0m && openingBalance.CreditAmount > 0m)
            {
                throw new InvalidOperationException(
                    $"Customer opening balance {openingBalance.Id} cannot have both debit and credit amounts positive.");
            }

            var tenantId = openingBalance.TenantId;
            var accounts = await _accountResolver.GetByCodesAsync(
                tenantId,
                new[]
                {
                    AccountCodes.AccountsReceivable,
                    AccountCodes.OpeningBalanceEquity
                },
                ct);

            var lines = new List<PostingLine>
            {
                openingBalance.DebitAmount > 0m
                    ? PostingLine.Debit(
                        accounts[AccountCodes.AccountsReceivable].Id,
                        openingBalance.DebitAmount,
                        $"Customer opening balance {openingBalance.DocumentNumber}")
                    : PostingLine.Debit(
                        accounts[AccountCodes.OpeningBalanceEquity].Id,
                        openingBalance.CreditAmount,
                        $"Opening balance offset {openingBalance.DocumentNumber}"),
                openingBalance.DebitAmount > 0m
                    ? PostingLine.Credit(
                        accounts[AccountCodes.OpeningBalanceEquity].Id,
                        openingBalance.DebitAmount,
                        $"Opening balance offset {openingBalance.DocumentNumber}")
                    : PostingLine.Credit(
                        accounts[AccountCodes.AccountsReceivable].Id,
                        openingBalance.CreditAmount,
                        $"Customer opening balance {openingBalance.DocumentNumber}")
            };

            var request = new PostingRequest
            {
                TenantId = tenantId,
                JournalDate = openingBalance.TransactionDate,
                SourceType = "CustomerOpeningBalance",
                SourceId = openingBalance.Id,
                SourceReference = openingBalance.DocumentNumber,
                Description = $"Customer opening balance {openingBalance.DocumentNumber}",
                IdempotencyKey = $"CUSTOMER_OPENING_BALANCE:{tenantId}:{openingBalance.Id}",
                Lines = lines
            };

            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post customer opening balance for customer {CustomerId}", openingBalance.PartyId);
            return PostingResult.Failed(ex.Message);
        }
    }

    public async Task<PostingResult> PostPartyLedgerAdjustmentAsync(PartyLedgerAdjustment adjustment, CancellationToken ct = default)
    {
        try
        {
            var (controlAccountCode, adjustmentAccountCode, isDebitAdjustment) = adjustment.PartyType switch
            {
                PartyType.Customer => (
                    AccountCodes.AccountsReceivable,
                    AccountCodes.AccountsReceivableAdjustments,
                    IsDebitAdjustment(adjustment.TransactionType)),
                PartyType.Supplier => (
                    AccountCodes.SupplierPayables,
                    AccountCodes.AccountsPayableAdjustments,
                    IsDebitAdjustment(adjustment.TransactionType)),
                _ => throw new InvalidOperationException($"Unsupported party type '{adjustment.PartyType}'.")
            };

            var accounts = await _accountResolver.GetByCodesAsync(
                adjustment.TenantId,
                new[] { controlAccountCode, adjustmentAccountCode },
                ct);

            var controlAccountId = accounts[controlAccountCode].Id;
            var adjustmentAccountId = accounts[adjustmentAccountCode].Id;
            var lines = new List<PostingLine>
            {
                isDebitAdjustment
                    ? PostingLine.Debit(controlAccountId, adjustment.Amount, $"Adjustment {adjustment.ReferenceNumber}")
                    : PostingLine.Debit(adjustmentAccountId, adjustment.Amount, $"Adjustment offset {adjustment.ReferenceNumber}"),
                isDebitAdjustment
                    ? PostingLine.Credit(adjustmentAccountId, adjustment.Amount, $"Adjustment offset {adjustment.ReferenceNumber}")
                    : PostingLine.Credit(controlAccountId, adjustment.Amount, $"Adjustment {adjustment.ReferenceNumber}")
            };

            var request = new PostingRequest
            {
                TenantId = adjustment.TenantId,
                JournalDate = adjustment.AdjustmentDate,
                SourceType = "PartyLedgerAdjustment",
                SourceId = adjustment.Id,
                SourceReference = adjustment.ReferenceNumber,
                Description = $"{adjustment.PartyType} adjustment {adjustment.ReferenceNumber}",
                IdempotencyKey = $"PARTY_ADJUSTMENT:{adjustment.TenantId}:{adjustment.Id}",
                Lines = lines
            };

            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post party ledger adjustment {AdjustmentId}", adjustment.Id);
            return PostingResult.Failed(ex.Message);
        }
    }
    
    public async Task<PostingResult> PostPosRefundAsync(
        Return returnObj,
        RefundAllocation allocation,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation(
                "Posting POS refund {ReturnId} for tenant {TenantId}",
                returnObj.Id, returnObj.TenantId);

            if (returnObj.ReturnType != ReturnType.SalesReturn)
            {
                throw new InvalidOperationException(
                    $"Return {returnObj.Id} is '{returnObj.ReturnType}' and cannot be posted as a sales refund.");
            }

            var allocationResolution = RefundAllocationSupport.Resolve(
                allocation.Method,
                allocation.CashAmount,
                allocation.CardAmount);
            if (!allocationResolution.Success)
            {
                throw new InvalidOperationException(allocationResolution.ErrorMessage);
            }

            allocation = allocationResolution.Allocation;
            if (allocation.TotalRefundAmount > returnObj.TotalAmount)
            {
                throw new InvalidOperationException(
                    $"Refund allocation {allocation.TotalRefundAmount} cannot exceed return total {returnObj.TotalAmount} for {returnObj.ReturnNumber}.");
            }

            if (allocation.Method == RefundMethod.AccountCredit)
            {
                if (allocation.TotalRefundAmount != 0m)
                {
                    throw new InvalidOperationException(
                        $"Account credit refund {returnObj.ReturnNumber} cannot include cash or card payout amounts.");
                }
            }
            else if (allocation.TotalRefundAmount != returnObj.TotalAmount)
            {
                throw new InvalidOperationException(
                    $"Refund allocation {allocation.TotalRefundAmount} must equal return total {returnObj.TotalAmount} for {returnObj.ReturnNumber}.");
            }

            var customerCreditAmount = RefundAllocationSupport.RoundCurrency(
                returnObj.TotalAmount - allocation.TotalRefundAmount);

            var requiredAccountCodes = new List<string>
            {
                AccountCodes.PosSales,
                AccountCodes.SalesTaxPayable,
                AccountCodes.Inventory,
                AccountCodes.CostOfGoodsSold
            };

            string? cashAccountCode = null;
            if (allocation.CashAmount > 0m)
            {
                cashAccountCode = await ResolveWarehouseCashboxAccountCodeAsync(
                    returnObj.TenantId,
                    returnObj.WarehouseId,
                    ct);
                requiredAccountCodes.Add(cashAccountCode);
            }

            if (allocation.CardAmount > 0m)
            {
                requiredAccountCodes.Add(AccountCodes.VisaClearing);
            }

            if (customerCreditAmount > 0m)
            {
                requiredAccountCodes.Add(AccountCodes.AccountsReceivable);
            }

            var accounts = await _accountResolver.GetByCodesAsync(
                returnObj.TenantId,
                requiredAccountCodes.Distinct(),
                ct);

            // Reverse the revenue recognition using the explicit refund funding split.
            var netRevenueReversal = returnObj.TotalAmount - returnObj.TaxAmount;
            var lines = new List<PostingLine>();
            if (netRevenueReversal > 0m)
            {
                lines.Add(PostingLine.Debit(
                    accounts[AccountCodes.PosSales].Id,
                    netRevenueReversal,
                    $"Return {returnObj.ReturnNumber}"));
            }
            if (returnObj.TaxAmount > 0m)
            {
                lines.Add(PostingLine.Debit(
                    accounts[AccountCodes.SalesTaxPayable].Id,
                    returnObj.TaxAmount,
                    $"Sales tax reversal {returnObj.ReturnNumber}"));
            }

            if (allocation.CashAmount > 0m)
            {
                lines.Add(PostingLine.Credit(
                    accounts[cashAccountCode!].Id,
                    allocation.CashAmount,
                    $"Cash refund {returnObj.ReturnNumber}"));
            }

            if (allocation.CardAmount > 0m)
            {
                lines.Add(PostingLine.Credit(
                    accounts[AccountCodes.VisaClearing].Id,
                    allocation.CardAmount,
                    $"Card refund {returnObj.ReturnNumber}"));
            }

            if (customerCreditAmount > 0m)
            {
                lines.Add(PostingLine.Credit(
                    accounts[AccountCodes.AccountsReceivable].Id,
                    customerCreditAmount,
                    $"Customer credit {returnObj.ReturnNumber}"));
            }
            
            var request = new PostingRequest
            {
                TenantId = returnObj.TenantId,
                JournalDate = returnObj.ReturnDate,
                SourceType = "Return",
                SourceId = returnObj.Id,
                SourceReference = returnObj.ReturnNumber,
                Description = $"Customer refund {returnObj.ReturnNumber}",
                IdempotencyKey = $"REFUND:{returnObj.TenantId}:{returnObj.Id}",
                Lines = lines
            };
            
            var journal = await _postingService.PostJournalAsync(request, ct);
            
            // Reverse COGS if we have cost data
            var returnCost = CalculateReturnCost(returnObj);
            if (returnCost > 0)
            {
                var cogsReversal = new PostingRequest
                {
                    TenantId = returnObj.TenantId,
                    JournalDate = returnObj.ReturnDate,
                    SourceType = "Return_COGS",
                    SourceId = returnObj.Id,
                    SourceReference = returnObj.ReturnNumber,
                    Description = $"COGS reversal for {returnObj.ReturnNumber}",
                    IdempotencyKey = $"REFUND_COGS:{returnObj.TenantId}:{returnObj.Id}",
                    Lines = new List<PostingLine>
                    {
                        PostingLine.Debit(accounts[AccountCodes.Inventory].Id, returnCost, "Inventory return"),
                        PostingLine.Credit(accounts[AccountCodes.CostOfGoodsSold].Id, returnCost, "COGS reversal")
                    }
                };
                
                await _postingService.PostJournalAsync(cogsReversal, ct);
            }
            
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post refund {ReturnId}", returnObj.Id);
            return PostingResult.Failed(ex.Message);
        }
    }

    public async Task<PostingResult> PostPurchaseReturnAsync(Return returnObj, CancellationToken ct = default)
    {
        if (returnObj.ReturnType != ReturnType.PurchaseReturn)
        {
            throw new InvalidOperationException(
                $"Return {returnObj.Id} is '{returnObj.ReturnType}' and cannot be posted as a purchase return.");
        }

        try
        {
            ValidatePurchaseReturnSettlement(returnObj);

            var requiredAccountCodes = new List<string>
            {
                AccountCodes.SupplierPayables,
                AccountCodes.Inventory,
                AccountCodes.InputTaxReceivable
            };
            string? cashAccountCode = null;
            if (returnObj.CashRefundAmount > 0m)
            {
                cashAccountCode = await ResolveWarehouseCashboxAccountCodeAsync(
                    returnObj.TenantId,
                    returnObj.WarehouseId,
                    ct);
                requiredAccountCodes.Add(cashAccountCode);
            }

            var accounts = await _accountResolver.GetByCodesAsync(
                returnObj.TenantId,
                requiredAccountCodes,
                ct);

            var request = new PostingRequest
            {
                TenantId = returnObj.TenantId,
                JournalDate = returnObj.ReturnDate,
                SourceType = "PurchaseReturn",
                SourceId = returnObj.Id,
                SourceReference = returnObj.ReturnNumber,
                Description = $"Purchase return {returnObj.ReturnNumber}",
                IdempotencyKey = $"PURCHASE_RETURN:{returnObj.TenantId}:{returnObj.Id}",
                Lines = BuildPurchaseReturnLines(returnObj, accounts, cashAccountCode)
            };

            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post purchase return {ReturnId}", returnObj.Id);
            return PostingResult.Failed(ex.Message);
        }
    }
    
    /// <summary>
    /// PHASE LCR: Posts journal entry for a POS expense.
    /// </summary>
    public async Task<PostingResult> PostPosExpenseAsync(
        Expense expense,
        CancellationToken ct = default,
        string? operationKey = null)
    {
        if (operationKey != null)
        {
            if (string.IsNullOrWhiteSpace(operationKey))
            {
                throw new ArgumentException(
                    "Operation key cannot be empty or whitespace. Pass null to use the base idempotency key, or provide a valid non-empty key.",
                    nameof(operationKey));
            }

            if (!OperationKeyPattern.IsMatch(operationKey))
            {
                throw new ArgumentException(
                    "Operation key must contain only alphanumeric characters, dots, underscores, or hyphens. " +
                    "Invalid characters like ':', control characters (newline, tab, null), and other special characters are not allowed.",
                    nameof(operationKey));
            }
        }

        try
        {
            _logger.LogInformation(
                "Posting POS expense {ExpenseId} for tenant {TenantId}, Amount: {Amount}",
                expense.Id, expense.TenantId, expense.Amount);
            
            // Get the expense account code (default to general expenses)
            var expenseAccountCode = AccountCodes.Expenses;

            var fundingAccountCode = expense.PaymentMethod switch
            {
                "Cash" => await ResolveExpenseCashboxAccountCodeAsync(expense, ct),
                "Visa" or "Card" => AccountCodes.VisaClearing,
                "BankTransfer" => AccountCodes.BankAccount,
                _ => throw CreateInvalidPaymentMethodException(expense)
            };

            var accounts = await _accountResolver.GetByCodesAsync(
                expense.TenantId,
                new[] { expenseAccountCode, fundingAccountCode },
                ct);
            
            var lines = new List<PostingLine>
            {
                // Debit Expense account
                PostingLine.Debit(accounts[expenseAccountCode].Id, expense.Amount, $"Expense: {expense.Description}")
            };
            
            // Credit depends on payment method
            switch (expense.PaymentMethod)
            {
                case "Cash":
                    lines.Add(PostingLine.Credit(
                        accounts[fundingAccountCode].Id,
                        expense.Amount,
                        $"Cash expense {expense.ReferenceNumber}"));
                    break;
                    
                case "Visa":
                case "Card":
                    lines.Add(PostingLine.Credit(
                        accounts[fundingAccountCode].Id,
                        expense.Amount,
                        $"Card expense {expense.ReferenceNumber}"));
                    break;
                    
                case "BankTransfer":
                    lines.Add(PostingLine.Credit(
                        accounts[fundingAccountCode].Id,
                        expense.Amount,
                        $"Bank transfer {expense.ReferenceNumber}"));
                    break;
                    
                default:
                    throw CreateInvalidPaymentMethodException(expense);
            }
            
            var request = new PostingRequest
            {
                TenantId = expense.TenantId,
                JournalDate = expense.ExpenseDate,
                SourceType = "Expense",
                SourceId = expense.Id,
                SourceReference = expense.ReferenceNumber ?? expense.Id.ToString(),
                Description = $"Expense: {expense.Description}",
                IdempotencyKey = BuildExpenseJournalIdempotencyKey(expense.TenantId, expense.Id, operationKey),
                Lines = lines
            };
            
            var journal = await _postingService.PostJournalAsync(request, ct);
            
            _logger.LogInformation(
                "Posted expense {ExpenseId}, JournalNumber: {JournalNumber}",
                expense.Id, journal.JournalNumber);
            
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post expense {ExpenseId}", expense.Id);
            return PostingResult.Failed(ex.Message);
        }
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // E-COMMERCE OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    public async Task<PostingResult> PostOnlinePaymentCapturedAsync(
        Order order, string paymentIntentId, string gateway, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation(
                "Posting online payment for order {OrderNumber}, Gateway: {Gateway}",
                order.OrderNumber, gateway);

            EnsureEcommerceOrderPaymentAuthority(order, gateway);
            var paymentAccountId = await ResolveEcommercePaymentAccountIdAsync(order.TenantId, gateway, ct);
            var salesAccount = await _accountResolver.GetByCodeAsync(
                order.TenantId,
                AccountCodes.EcommerceSales,
                ct);
            
            var request = new PostingRequest
            {
                TenantId = order.TenantId,
                JournalDate = DateTime.UtcNow,
                SourceType = "Order_Payment",
                SourceId = order.Id,
                SourceReference = order.OrderNumber,
                Description = $"Online payment {order.OrderNumber} via {gateway}",
                IdempotencyKey = $"ONLINE_PAYMENT:{order.TenantId}:{order.Id}:{paymentIntentId}",
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(paymentAccountId, order.Total, $"{gateway} payment"),
                    PostingLine.Credit(salesAccount.Id, order.Total, $"E-commerce sale #{order.OrderNumber}")
                }
            };
            
            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post online payment for order {OrderId}", order.Id);
            return PostingResult.Failed(ex.Message);
        }
    }
    
    public async Task<PostingResult> PostUnresolvedPaymentAsync(
        Guid tenantId,
        string gateway,
        decimal amount,
        string currency,
        string providerEventId,
        CancellationToken ct = default)
    {
        // Preserve original casing for idempotency and description
        var gatewayTrimmed = gateway?.Trim() ?? string.Empty;
        
        // Reject empty/whitespace gateways to avoid ambiguous lineage in suspense
        if (string.IsNullOrWhiteSpace(gatewayTrimmed))
        {
            _logger.LogError(
                "Cannot post unresolved payment with empty gateway to suspense | TenantId: {TenantId} | Amount: {Amount} | EventId: {EventId}",
                tenantId, amount, providerEventId);
            return PostingResult.Failed("Gateway is required for unresolved payment posting to avoid ambiguous suspense lineage.");
        }

        if (string.IsNullOrWhiteSpace(providerEventId))
        {
            return PostingResult.Failed("Provider event ID is required for unresolved payment posting.");
        }
        
        // Normalize gateway for account resolution and logging only
        var normalizedForResolve = gatewayTrimmed.ToLowerInvariant();
        var normalizedProviderEventId = providerEventId.Trim();
        
        try
        {
            _logger.LogWarning(
                "Posting unresolved payment to suspense ledger | TenantId: {TenantId} | Gateway: {Gateway} | Amount: {Amount} | EventId: {EventId}",
                tenantId, normalizedForResolve, amount, providerEventId);
            
            var paymentAccountId = await ResolveEcommercePaymentAccountIdAsync(tenantId, normalizedForResolve, ct);
            var suspenseAccount = await _accountResolver.GetByCodeAsync(
                tenantId,
                AccountCodes.TreasurySuspense,
                ct);
            
            var request = new PostingRequest
            {
                TenantId = tenantId,
                JournalDate = DateTime.UtcNow,
                SourceType = "UnresolvedPayment",
                SourceId = BuildUnresolvedPaymentSourceId(tenantId, normalizedForResolve, normalizedProviderEventId),
                SourceReference = normalizedProviderEventId,
                Description = $"Unresolved payment from {gatewayTrimmed}: {normalizedProviderEventId}",
                IdempotencyKey = $"UNRESOLVED_PAYMENT:{tenantId}:{gatewayTrimmed}:{normalizedProviderEventId}",
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(paymentAccountId, amount, $"{gatewayTrimmed} unresolved capture"),
                    PostingLine.Credit(suspenseAccount.Id, amount, $"Suspense cash for {normalizedProviderEventId}")
                }
            };
            
            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post unresolved payment to suspense ledger | EventId: {EventId}", providerEventId);
            return PostingResult.Failed(ex.Message);
        }
    }

    private static Guid BuildUnresolvedPaymentSourceId(
        Guid tenantId,
        string normalizedGateway,
        string normalizedProviderEventId)
    {
        var source = Encoding.UTF8.GetBytes(
            $"unresolved-payment|{tenantId:N}|{normalizedGateway}|{normalizedProviderEventId}");
        return new Guid(SHA256.HashData(source).AsSpan(0, 16));
    }

    public async Task<PostingResult> PostResolvedUnresolvedPaymentAsync(
        Order order,
        decimal amount,
        string? gateway,
        string providerEventId,
        CancellationToken ct = default)
    {
        // GATEWAY NORMALIZATION STRATEGY:
        // - gatewayTrimmed: Preserves original casing for journal/accounting idempotency keys
        //   and human-readable descriptions (e.g., "Paymob", "Fawry"). This keeps journal
        //   entries and idempotency keys distinct and readable.
        // - normalizedForResolve: Uses ToLowerInvariant() solely for account resolution
        //   to match the outbox service's lowercased idempotency domain. This ensures
        //   consistent account lookups regardless of gateway casing variations.
        // This intentional divergence maintains both human readability and technical consistency.
        
        // Preserve original casing for idempotency and description
        var gatewayTrimmed = gateway?.Trim() ?? string.Empty;
        var hasGateway = !string.IsNullOrEmpty(gatewayTrimmed);
        
        // Normalize gateway for account resolution and logging only
        var normalizedForResolve = hasGateway ? gatewayTrimmed.ToLowerInvariant() : string.Empty;
        
        // Validate inputs before constructing IdempotencyKey and PostingRequest
        if (string.IsNullOrWhiteSpace(providerEventId))
        {
            throw new ArgumentException(
                "Provider event ID cannot be null, empty, or whitespace.",
                nameof(providerEventId));
        }

        // T-09: suspense cannot be cleared to revenue without explicit provider
        // lineage. This also checks durable order fields for disguised Fawry data.
        EnsureEcommerceOrderPaymentAuthority(order, gatewayTrimmed);

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Amount must be greater than zero.");
        }

        try
        {
            _logger.LogInformation(
                "Clearing unresolved storefront payment suspense to order revenue | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber} | Amount: {Amount} | Gateway: {Gateway} | EventId: {EventId}",
                order.TenantId,
                order.Id,
                order.OrderNumber,
                amount,
                hasGateway ? normalizedForResolve : "(legacy)",
                providerEventId);

            var accounts = await _accountResolver.GetByCodesAsync(
                order.TenantId,
                new[] { AccountCodes.TreasurySuspense, AccountCodes.EcommerceSales },
                ct);

            // Build idempotency key: legacy format (without gateway) for backward compatibility
            // or new format (with gateway) for messages that include it
            // Use original casing (gatewayTrimmed) to preserve idempotency
            var idempotencyKey = hasGateway
                ? $"RESOLVED_UNRESOLVED_PAYMENT:{order.TenantId}:{order.Id}:{providerEventId}:{gatewayTrimmed}"
                : $"RESOLVED_UNRESOLVED_PAYMENT:{order.TenantId}:{order.Id}:{providerEventId}";

            var request = new PostingRequest
            {
                TenantId = order.TenantId,
                JournalDate = order.PaidAt ?? DateTime.UtcNow,
                SourceType = "Order_UnresolvedPaymentClearance",
                SourceId = order.Id,
                SourceReference = order.OrderNumber,
                Description = hasGateway 
                    ? $"Resolved suspense payment {order.OrderNumber} via {gatewayTrimmed}"
                    : $"Resolved suspense payment {order.OrderNumber}",
                IdempotencyKey = idempotencyKey,
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(accounts[AccountCodes.TreasurySuspense].Id, amount, $"Clear suspense for {providerEventId}"),
                    PostingLine.Credit(accounts[AccountCodes.EcommerceSales].Id, amount, $"Recognize storefront sale #{order.OrderNumber}")
                }
            };

            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to clear unresolved storefront payment suspense for order {OrderId} | Gateway: {Gateway} | EventId: {EventId}",
                order.Id,
                normalizedForResolve,
                providerEventId);
            return PostingResult.Failed(ex.Message);
        }
    }

    public async Task<PostingResult> PostResolvedUnresolvedCommerceHubPaymentAsync(
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

        var gatewayTrimmed = gateway.Trim();
        var normalizedGateway = gatewayTrimmed.ToLowerInvariant();
        var providerEventIdTrimmed = providerEventId.Trim();

        // T-09: Fawry suspense must never clear to a matched CommerceHub capture.
        if (Core.Payments.FawryFinancialAuthorityPolicy.IsFawry(gatewayTrimmed))
        {
            throw new InvalidOperationException(
                $"{Core.Payments.FawryFinancialAuthorityPolicy.Code}: {Core.Payments.FawryFinancialAuthorityPolicy.Message}");
        }

        try
        {
            _logger.LogInformation(
                "Clearing CommerceHub unresolved suspense after paid-payment match | TenantId: {TenantId} | PaymentId: {PaymentId} | Amount: {Amount} | Gateway: {Gateway} | EventId: {EventId}",
                tenantId,
                paymentId,
                amount,
                normalizedGateway,
                providerEventIdTrimmed);

            var paymentAccountId = await ResolveEcommercePaymentAccountIdAsync(tenantId, normalizedGateway, ct);
            var suspenseAccount = await _accountResolver.GetByCodeAsync(
                tenantId,
                AccountCodes.TreasurySuspense,
                ct);

            var request = new PostingRequest
            {
                TenantId = tenantId,
                JournalDate = DateTime.UtcNow,
                SourceType = "CommerceHubUnresolvedPaymentClearance",
                SourceId = paymentId,
                SourceReference = providerEventIdTrimmed,
                Description = $"Clear CommerceHub suspense for provider capture {providerEventIdTrimmed}",
                IdempotencyKey = $"CH_RESOLVED_UNRESOLVED_PAYMENT:{tenantId}:{paymentId}:{providerEventIdTrimmed}:{normalizedGateway}",
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(suspenseAccount.Id, amount, $"Clear CommerceHub suspense for {providerEventIdTrimmed}"),
                    PostingLine.Credit(paymentAccountId, amount, $"{gatewayTrimmed} matched CommerceHub capture")
                }
            };

            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to clear CommerceHub unresolved suspense for payment {PaymentId} | TenantId: {TenantId} | Gateway: {Gateway} | EventId: {EventId}",
                paymentId,
                tenantId,
                normalizedGateway,
                providerEventId);
            return PostingResult.Failed(ex.Message);
        }
    }
    
    public async Task<PostingResult> PostCodDeliveredAsync(Order order, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Posting COD delivery for order {OrderNumber}", order.OrderNumber);
            var collectedAmount = order.Total;
            if (order.FulfillmentMode == FulfillmentMode.Carrier || !string.IsNullOrWhiteSpace(order.FulfillmentCarrierCode))
            {
                if (_db == null || _db.Database.CurrentTransaction == null)
                    return PostingResult.Failed("Carrier COD posting requires transactional immutable evidence authority.");
                var canonical = await _db.Orders.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(o => o.Id == order.Id && o.TenantId == order.TenantId, ct);
                if (canonical == null || canonical.Total != order.Total || canonical.Currency != order.Currency || canonical.FulfillmentMode != FulfillmentMode.Carrier)
                    return PostingResult.Failed("Carrier COD order authority changed.");
                var (evidence, code) = await PointOfSale.Infrastructure.Shipping.CarrierCollectionMoneyAuthority.RevalidateAsync(_db, canonical, null, null, ct);
                if (code != null) return PostingResult.Failed(code);
                collectedAmount = evidence!.ObservedAmount!.Value;
            }
            
            var accounts = await _accountResolver.GetByCodesAsync(
                order.TenantId,
                new[] { AccountCodes.CodClearing, AccountCodes.EcommerceSales },
                ct);
            
            var request = new PostingRequest
            {
                TenantId = order.TenantId,
                JournalDate = order.DeliveredAt ?? DateTime.UtcNow,
                SourceType = "Order_COD",
                SourceId = order.Id,
                SourceReference = order.OrderNumber,
                Description = $"COD delivered {order.OrderNumber}",
                IdempotencyKey = $"COD_DELIVERED:{order.TenantId}:{order.Id}",
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(accounts[AccountCodes.CodClearing].Id, collectedAmount, "COD collection"),
                    PostingLine.Credit(accounts[AccountCodes.EcommerceSales].Id, collectedAmount, $"E-commerce sale #{order.OrderNumber}")
                }
            };
            
            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post COD delivery for order {OrderId}", order.Id);
            return PostingResult.Failed(ex.Message);
        }
    }
    
    public async Task<PostingResult> PostWalletApprovedAsync(Order order, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Posting wallet approval for order {OrderNumber}", order.OrderNumber);
            
            var accounts = await _accountResolver.GetByCodesAsync(
                order.TenantId,
                new[] { AccountCodes.WalletClearing, AccountCodes.EcommerceSales },
                ct);
            
            var request = new PostingRequest
            {
                TenantId = order.TenantId,
                JournalDate = order.PaidAt ?? DateTime.UtcNow,
                SourceType = "Order_Wallet",
                SourceId = order.Id,
                SourceReference = order.OrderNumber,
                Description = $"Wallet payment {order.OrderNumber}",
                IdempotencyKey = $"WALLET_APPROVED:{order.TenantId}:{order.Id}",
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(accounts[AccountCodes.WalletClearing].Id, order.Total, "Wallet payment"),
                    PostingLine.Credit(accounts[AccountCodes.EcommerceSales].Id, order.Total, $"E-commerce sale #{order.OrderNumber}")
                }
            };
            
            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post wallet approval for order {OrderId}", order.Id);
            return PostingResult.Failed(ex.Message);
        }
    }
    
    public Task<PostingResult> PostEcommerceRefundAsync(
        Order order, decimal refundAmount, string refundId, CancellationToken ct = default)
    {
        return PostEcommerceRefundAsync(order, refundAmount, refundId, null, ct);
    }

    public async Task<PostingResult> PostEcommerceRefundAsync(
        Order order,
        decimal refundAmount,
        string refundId,
        EcommerceRefundAccountingContext? refundContext,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation(
                "Posting e-commerce refund for order {OrderNumber}, Amount: {Amount}",
                order.OrderNumber, refundAmount);

            var creditAccountId = await ResolveEcommerceRefundCreditAccountIdAsync(order, refundContext, ct);
            var salesAccount = await _accountResolver.GetByCodeAsync(
                order.TenantId,
                AccountCodes.EcommerceSales,
                ct);

            var isPostSettlementRefund = refundContext?.IsPostSettlementRefund == true;
            var refundDescription = isPostSettlementRefund
                ? $"Post-settlement refund for {order.OrderNumber} (Settlement {refundContext!.SettlementId})"
                : $"Refund for {order.OrderNumber}";
            var creditLineDescription = isPostSettlementRefund
                ? $"Refund to customer after settlement {refundContext!.SettlementId}"
                : "Refund to customer";
            
            var request = new PostingRequest
            {
                TenantId = order.TenantId,
                JournalDate = DateTime.UtcNow,
                SourceType = "Order_Refund",
                SourceId = order.Id,
                SourceReference = order.OrderNumber,
                Description = refundDescription,
                IdempotencyKey = $"ECOM_REFUND:{order.TenantId}:{order.Id}:{refundId}",
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(salesAccount.Id, refundAmount, "Refund"),
                    PostingLine.Credit(creditAccountId, refundAmount, creditLineDescription)
                }
            };
            
            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post e-commerce refund for order {OrderId}", order.Id);
            return PostingResult.Failed(ex.Message);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // T-08 E-COMMERCE / COMMERCEHUB MERCHANDISE-COST (COGS) OPERATIONS
    //
    // Forward-looking only: every amount below comes from a frozen line snapshot
    // captured at checkout commit. Live ProductVariant.CostPrice is never read here.
    // Journals touch only 5010 (COGS) and 1310 (Inventory): no cash, clearing,
    // AR/AP, or revenue leg, so Cash Flow classification is unaffected.
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<PostingResult> PostEcommerceCogsAsync(
        EcommerceCogsPostingRequest cogs,
        CancellationToken ct = default)
    {
        try
        {
            ValidateEcommerceCogsRequest(cogs);

            _logger.LogInformation(
                "Posting Storefront COGS for order {OrderNumber} | CommitEvent: {CommitEventId} | TotalCost: {TotalCost}",
                cogs.OrderNumber, cogs.CommitEventId, cogs.TotalCost);

            var accounts = await _accountResolver.GetByCodesAsync(
                cogs.TenantId,
                new[] { AccountCodes.CostOfGoodsSold, AccountCodes.Inventory },
                ct);

            var request = new PostingRequest
            {
                TenantId = cogs.TenantId,
                JournalDate = cogs.CommitOccurredAtUtc,
                SourceType = EcommerceCogsPolicy.StorefrontCogsSourceType,
                SourceId = cogs.OrderId,
                SourceReference = cogs.OrderNumber,
                Description = $"Storefront COGS {cogs.OrderNumber} (commit {cogs.CommitEventId})",
                IdempotencyKey = cogs.IdempotencyKey,
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(accounts[AccountCodes.CostOfGoodsSold].Id, cogs.TotalCost, "Cost of Goods Sold"),
                    PostingLine.Credit(accounts[AccountCodes.Inventory].Id, cogs.TotalCost, "Inventory Reduction")
                }
            };

            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post Storefront COGS for order {OrderId}", cogs.OrderId);
            return PostingResult.Failed(ex.Message);
        }
    }

    public async Task<PostingResult> PostEcommerceCogsReversalAsync(
        EcommerceCogsReversalPostingRequest reversal,
        CancellationToken ct = default)
    {
        try
        {
            ValidateEcommerceCogsReversalRequest(reversal);

            _logger.LogInformation(
                "Posting Storefront COGS reversal for order {OrderNumber} | ReturnEvent: {ReturnEventId} | TotalReversal: {TotalReversal}",
                reversal.OrderNumber, reversal.ReturnEventId, reversal.TotalReversal);

            var accounts = await _accountResolver.GetByCodesAsync(
                reversal.TenantId,
                new[] { AccountCodes.CostOfGoodsSold, AccountCodes.Inventory },
                ct);

            var request = new PostingRequest
            {
                TenantId = reversal.TenantId,
                JournalDate = reversal.ReturnOccurredAtUtc,
                SourceType = EcommerceCogsPolicy.StorefrontCogsReversalSourceType,
                SourceId = reversal.OrderId,
                SourceReference = reversal.OrderNumber,
                Description = $"Storefront COGS reversal {reversal.OrderNumber} (return {reversal.ReturnEventId})",
                IdempotencyKey = reversal.IdempotencyKey,
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(accounts[AccountCodes.Inventory].Id, reversal.TotalReversal, "Inventory Restoration"),
                    PostingLine.Credit(accounts[AccountCodes.CostOfGoodsSold].Id, reversal.TotalReversal, "COGS reversal")
                }
            };

            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post Storefront COGS reversal for order {OrderId}", reversal.OrderId);
            return PostingResult.Failed(ex.Message);
        }
    }

    public async Task<PostingResult> PostCommerceHubCogsAsync(
        CommerceHubCogsPostingRequest cogs,
        CancellationToken ct = default)
    {
        try
        {
            ValidateCommerceHubCogsRequest(cogs);

            _logger.LogInformation(
                "Posting CommerceHub COGS for sub-order {SubOrderNumber} | DeliveryCommitEvent: {DeliveryCommitEventId} | Cost: {CostAmount}",
                cogs.SubOrderNumber, cogs.DeliveryCommitEventId, cogs.CostAmount);

            var accounts = await _accountResolver.GetByCodesAsync(
                cogs.TenantId,
                new[] { AccountCodes.CostOfGoodsSold, AccountCodes.Inventory },
                ct);

            var request = new PostingRequest
            {
                TenantId = cogs.TenantId,
                JournalDate = cogs.CommitOccurredAtUtc,
                SourceType = EcommerceCogsPolicy.CommerceHubCogsSourceType,
                SourceId = cogs.SubOrderId,
                SourceReference = cogs.SubOrderNumber,
                Description = $"CommerceHub COGS {cogs.SubOrderNumber} (delivery {cogs.DeliveryCommitEventId})",
                IdempotencyKey = cogs.IdempotencyKey,
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(accounts[AccountCodes.CostOfGoodsSold].Id, cogs.CostAmount, "Cost of Goods Sold"),
                    PostingLine.Credit(accounts[AccountCodes.Inventory].Id, cogs.CostAmount, "Inventory Reduction")
                }
            };

            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post CommerceHub COGS for sub-order {SubOrderId}", cogs.SubOrderId);
            return PostingResult.Failed(ex.Message);
        }
    }

    public async Task<PostingResult> PostCommerceHubCogsReversalAsync(
        CommerceHubCogsReversalPostingRequest reversal,
        CancellationToken ct = default)
    {
        try
        {
            ValidateCommerceHubCogsReversalRequest(reversal);

            _logger.LogInformation(
                "Posting CommerceHub COGS reversal for sub-order {SubOrderNumber} | Return: {ReturnId} | Reversal: {ReversalAmount}",
                reversal.SubOrderNumber, reversal.ReturnId, reversal.ReversalAmount);

            var accounts = await _accountResolver.GetByCodesAsync(
                reversal.TenantId,
                new[] { AccountCodes.CostOfGoodsSold, AccountCodes.Inventory },
                ct);

            var request = new PostingRequest
            {
                TenantId = reversal.TenantId,
                JournalDate = reversal.ReturnOccurredAtUtc,
                SourceType = EcommerceCogsPolicy.CommerceHubCogsReversalSourceType,
                SourceId = reversal.SubOrderId,
                SourceReference = reversal.SubOrderNumber,
                Description = $"CommerceHub COGS reversal {reversal.SubOrderNumber} (return {reversal.ReturnId})",
                IdempotencyKey = reversal.IdempotencyKey,
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(accounts[AccountCodes.Inventory].Id, reversal.ReversalAmount, "Inventory Restoration"),
                    PostingLine.Credit(accounts[AccountCodes.CostOfGoodsSold].Id, reversal.ReversalAmount, "COGS reversal")
                }
            };

            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post CommerceHub COGS reversal for sub-order {SubOrderId}", reversal.SubOrderId);
            return PostingResult.Failed(ex.Message);
        }
    }

    private static void ValidateEcommerceCogsRequest(EcommerceCogsPostingRequest cogs)
    {
        ArgumentNullException.ThrowIfNull(cogs);
        if (cogs.TenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(cogs));
        if (cogs.OrderId == Guid.Empty) throw new ArgumentException("OrderId is required.", nameof(cogs));
        ArgumentException.ThrowIfNullOrWhiteSpace(cogs.OrderNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(cogs.CommitEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cogs.Currency);
        if (cogs.Lines == null || cogs.Lines.Count == 0)
            throw new ArgumentException("COGS requires at least one snapshot cost line.", nameof(cogs));
        if (cogs.TotalCost <= 0m)
            throw new ArgumentOutOfRangeException(nameof(cogs), cogs.TotalCost, "COGS total must be greater than zero.");

        foreach (var line in cogs.Lines)
        {
            if (line.OrderItemId == Guid.Empty) throw new ArgumentException("OrderItemId is required on every COGS line.", nameof(cogs));
            if (line.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(cogs), line.Quantity, "COGS line quantity must be positive.");
            if (line.UnitCostSnapshot < 0m) throw new ArgumentOutOfRangeException(nameof(cogs), line.UnitCostSnapshot, "Snapshot unit cost cannot be negative.");
            if (line.CostAmount <= 0m) throw new ArgumentOutOfRangeException(nameof(cogs), line.CostAmount, "Snapshot line cost must be positive.");

            var expected = Math.Round(line.UnitCostSnapshot * line.Quantity, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(expected - line.CostAmount) > 0.01m)
                throw new InvalidOperationException(
                    $"COGS line cost {line.CostAmount} does not match snapshot {line.UnitCostSnapshot} × quantity {line.Quantity}. Failing closed.");
        }

        var lineTotal = cogs.Lines.Sum(line => line.CostAmount);
        if (Math.Abs(lineTotal - cogs.TotalCost) > 0.01m)
            throw new InvalidOperationException(
                $"COGS total {cogs.TotalCost} does not match the sum of snapshot lines {lineTotal}. Failing closed.");
    }

    private static void ValidateEcommerceCogsReversalRequest(EcommerceCogsReversalPostingRequest reversal)
    {
        ArgumentNullException.ThrowIfNull(reversal);
        if (reversal.TenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(reversal));
        if (reversal.OrderId == Guid.Empty) throw new ArgumentException("OrderId is required.", nameof(reversal));
        ArgumentException.ThrowIfNullOrWhiteSpace(reversal.OrderNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(reversal.ReturnEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reversal.Currency);
        if (reversal.Lines == null || reversal.Lines.Count == 0)
            throw new ArgumentException("COGS reversal requires at least one line.", nameof(reversal));
        if (reversal.TotalReversal <= 0m)
            throw new ArgumentOutOfRangeException(nameof(reversal), reversal.TotalReversal, "COGS reversal total must be greater than zero.");

        foreach (var line in reversal.Lines)
        {
            if (line.OrderItemId == Guid.Empty) throw new ArgumentException("OrderItemId is required on every reversal line.", nameof(reversal));
            if (line.ReturnQuantity <= 0m) throw new ArgumentOutOfRangeException(nameof(reversal), line.ReturnQuantity, "Reversal quantity must be positive.");
            if (line.ReversalAmount <= 0m) throw new ArgumentOutOfRangeException(nameof(reversal), line.ReversalAmount, "Reversal amount must be positive.");
        }

        var lineTotal = reversal.Lines.Sum(line => line.ReversalAmount);
        if (Math.Abs(lineTotal - reversal.TotalReversal) > 0.01m)
            throw new InvalidOperationException(
                $"COGS reversal total {reversal.TotalReversal} does not match the sum of reversal lines {lineTotal}. Failing closed.");
    }

    private static void ValidateCommerceHubCogsRequest(CommerceHubCogsPostingRequest cogs)
    {
        ArgumentNullException.ThrowIfNull(cogs);
        if (cogs.TenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(cogs));
        if (cogs.MarketplaceOrderId == Guid.Empty) throw new ArgumentException("MarketplaceOrderId is required.", nameof(cogs));
        if (cogs.SubOrderId == Guid.Empty) throw new ArgumentException("SubOrderId is required.", nameof(cogs));
        ArgumentException.ThrowIfNullOrWhiteSpace(cogs.SubOrderNumber);
        if (cogs.SubOrderItemId == Guid.Empty) throw new ArgumentException("SubOrderItemId is required.", nameof(cogs));
        ArgumentException.ThrowIfNullOrWhiteSpace(cogs.DeliveryCommitEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cogs.Currency);
        if (cogs.Quantity <= 0m) throw new ArgumentOutOfRangeException(nameof(cogs), cogs.Quantity, "Committed quantity must be positive.");
        if (cogs.UnitCostSnapshot < 0m) throw new ArgumentOutOfRangeException(nameof(cogs), cogs.UnitCostSnapshot, "Snapshot unit cost cannot be negative.");
        if (cogs.CostAmount <= 0m) throw new ArgumentOutOfRangeException(nameof(cogs), cogs.CostAmount, "Snapshot cost must be positive.");

        var expected = Math.Round(cogs.UnitCostSnapshot * cogs.Quantity, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(expected - cogs.CostAmount) > 0.01m)
            throw new InvalidOperationException(
                $"CommerceHub COGS cost {cogs.CostAmount} does not match snapshot {cogs.UnitCostSnapshot} × quantity {cogs.Quantity}. Failing closed.");
    }

    private static void ValidateCommerceHubCogsReversalRequest(CommerceHubCogsReversalPostingRequest reversal)
    {
        ArgumentNullException.ThrowIfNull(reversal);
        if (reversal.TenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(reversal));
        if (reversal.SubOrderId == Guid.Empty) throw new ArgumentException("SubOrderId is required.", nameof(reversal));
        ArgumentException.ThrowIfNullOrWhiteSpace(reversal.SubOrderNumber);
        if (reversal.SubOrderItemId == Guid.Empty) throw new ArgumentException("SubOrderItemId is required.", nameof(reversal));
        if (reversal.ReturnId == Guid.Empty) throw new ArgumentException("ReturnId is required.", nameof(reversal));
        ArgumentException.ThrowIfNullOrWhiteSpace(reversal.ReturnEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reversal.Currency);
        if (reversal.ResellableQuantity <= 0m) throw new ArgumentOutOfRangeException(nameof(reversal), reversal.ResellableQuantity, "Resellable quantity must be positive.");
        if (reversal.UnitCostSnapshot < 0m) throw new ArgumentOutOfRangeException(nameof(reversal), reversal.UnitCostSnapshot, "Snapshot unit cost cannot be negative.");
        if (reversal.ReversalAmount <= 0m) throw new ArgumentOutOfRangeException(nameof(reversal), reversal.ReversalAmount, "Reversal amount must be positive.");

        var expected = Math.Round(reversal.UnitCostSnapshot * reversal.ResellableQuantity, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(expected - reversal.ReversalAmount) > 0.01m)
            throw new InvalidOperationException(
                $"CommerceHub COGS reversal {reversal.ReversalAmount} does not match snapshot {reversal.UnitCostSnapshot} × resellable {reversal.ResellableQuantity}. Failing closed.");
    }

    private async Task<Guid> ResolveEcommerceRefundCreditAccountIdAsync(
        Order order,
        EcommerceRefundAccountingContext? refundContext,
        CancellationToken ct)
    {
        // T-09: no Fawry clearing lineage is certified, so a Fawry refund must never
        // resolve to Stripe/Visa/Wallet clearing, Bank, or Treasury Suspense.
        if (Core.Payments.FawryFinancialAuthorityPolicy.IsFawry(order.PaymentGateway) ||
            Core.Payments.FawryFinancialAuthorityPolicy.IsFawry(order.PaymentMethodCode))
        {
            throw new InvalidOperationException(
                $"{Core.Payments.FawryFinancialAuthorityPolicy.Code}: {Core.Payments.FawryFinancialAuthorityPolicy.Message}");
        }

        if (refundContext?.IsPostSettlementRefund != true)
        {
            return await ResolveEcommercePaymentAccountIdAsync(order.TenantId, order.PaymentGateway, ct);
        }

        refundContext.EnsureValidForPostSettlementRefund();

        if (_db == null)
            throw new InvalidOperationException("Post-settlement refunds require SQL destination lineage authority.");

        return await SettlementRefundLineage.ResolveDestinationAccountAsync(
            _db, order.TenantId, order.Id, refundContext, ct);
    }

    private async Task<Guid> ResolveEcommercePaymentAccountIdAsync(
        Guid tenantId,
        string? gateway,
        CancellationToken ct)
    {
        var paymentMethodCode = NormalizeEcommercePaymentMethodCode(gateway);
        return await _accountResolver.GetPaymentAccountIdAsync(tenantId, paymentMethodCode, ct);
    }

    private static string NormalizeEcommercePaymentMethodCode(string? gateway)
    {
        // T-09: no unknown-to-Stripe fallback. Every accepted code maps explicitly;
        // Fawry and unknown/blank values fail before any account resolution or posting.
        if (Core.Payments.FawryFinancialAuthorityPolicy.IsFawry(gateway))
        {
            throw new InvalidOperationException(
                $"{Core.Payments.FawryFinancialAuthorityPolicy.Code}: {Core.Payments.FawryFinancialAuthorityPolicy.Message}");
        }

        return gateway?.Trim().ToUpperInvariant() switch
        {
            PaymentMethodCodes.Stripe => PaymentMethodCodes.Stripe,
            "PAYMOB" or "VISA" or PaymentMethodCodes.Card => PaymentMethodCodes.Card,
            PaymentMethodCodes.Cod => PaymentMethodCodes.Cod,
            "MANUALWALLET" or PaymentMethodCodes.Wallet => PaymentMethodCodes.Wallet,
            _ => throw new InvalidOperationException(
                $"E-commerce payment account authority is unavailable for provider '{gateway?.Trim()}'.")
        };
    }

    private static void EnsureEcommerceOrderPaymentAuthority(Order order, string? gateway)
    {
        var requestedPaymentMethodCode = NormalizeEcommercePaymentMethodCode(gateway);

        foreach (var orderProvider in new[] { order.PaymentGateway, order.PaymentMethodCode })
        {
            if (string.IsNullOrWhiteSpace(orderProvider))
            {
                continue;
            }

            var orderPaymentMethodCode = NormalizeEcommercePaymentMethodCode(orderProvider);
            if (!string.Equals(orderPaymentMethodCode, requestedPaymentMethodCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"E-commerce payment provider lineage mismatch for order {order.Id}.");
            }
        }
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // SETTLEMENT OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    public async Task<PostingResult> PostGatewaySettlementAsync(
        Guid tenantId, Guid settlementBatchId, string gateway, decimal grossAmount, decimal fees,
        string settlementId, DateTime settledAtUtc, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation(
                "Posting gateway settlement for {Gateway}, Gross: {Gross}, Fees: {Fees}",
                gateway, grossAmount, fees);

            // T-09: Fawry has no certified clearing account; fail with the stable
            // authority code rather than the generic clearing-authority message.
            if (Core.Payments.FawryFinancialAuthorityPolicy.IsFawry(gateway))
            {
                throw new InvalidOperationException(
                    $"{Core.Payments.FawryFinancialAuthorityPolicy.Code}: {Core.Payments.FawryFinancialAuthorityPolicy.Message}");
            }

            var normalizedGateway = gateway.Trim().ToUpperInvariant();
            var clearingCode = normalizedGateway switch
            {
                "STRIPE" => AccountCodes.StripeClearing,
                "PAYMOB" or "CARD" or "VISA" => AccountCodes.VisaClearing,
                _ => throw new InvalidOperationException(
                    $"Gateway settlement provider '{gateway}' has no configured clearing-account authority.")
            };
            
            var accounts = await _accountResolver.GetByCodesAsync(
                tenantId,
                new[] { AccountCodes.BankAccount, AccountCodes.GatewayFees, clearingCode },
                ct);
            
            var netAmount = grossAmount - fees;
            var lines = new List<PostingLine>
            {
                PostingLine.Debit(accounts[AccountCodes.BankAccount].Id, netAmount, $"Settlement from {gateway}"),
                PostingLine.Credit(accounts[clearingCode].Id, grossAmount, $"Clear {gateway} balance")
            };
            
            // Record fees if any
            if (fees > 0)
            {
                lines.Add(PostingLine.Debit(accounts[AccountCodes.GatewayFees].Id, fees, $"{gateway} processing fees"));
            }
            
            var request = new PostingRequest
            {
                TenantId = tenantId,
                JournalDate = settledAtUtc,
                SourceType = "GatewaySettlement",
                SourceId = settlementBatchId,
                SourceReference = settlementId,
                Description = $"{gateway} settlement {settlementId}",
                IdempotencyKey = $"GATEWAY_SETTLEMENT:{tenantId}:{settlementId}",
                Lines = lines
            };
            
            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post gateway settlement {SettlementId}", settlementId);
            return PostingResult.Failed(ex.Message);
        }
    }
    
    public async Task<PostingResult> PostCodSettlementAsync(
        Guid tenantId, Guid settlementBatchId, decimal grossAmount, decimal fees,
        string settlementId, DateTime settledAtUtc, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation(
                "Posting COD settlement, Gross: {Gross}, Fees: {Fees}",
                grossAmount, fees);
            
            var accounts = await _accountResolver.GetByCodesAsync(
                tenantId,
                new[] { AccountCodes.BankAccount, AccountCodes.ShippingFees, AccountCodes.CodClearing },
                ct);
            
            var netAmount = grossAmount - fees;
            var lines = new List<PostingLine>
            {
                PostingLine.Debit(accounts[AccountCodes.BankAccount].Id, netAmount, "COD settlement"),
                PostingLine.Credit(accounts[AccountCodes.CodClearing].Id, grossAmount, "Clear COD balance")
            };
            
            if (fees > 0)
            {
                lines.Add(PostingLine.Debit(accounts[AccountCodes.ShippingFees].Id, fees, "COD collection fees"));
            }
            
            var request = new PostingRequest
            {
                TenantId = tenantId,
                JournalDate = settledAtUtc,
                SourceType = "CODSettlement",
                SourceId = settlementBatchId,
                SourceReference = settlementId,
                Description = $"COD settlement {settlementId}",
                IdempotencyKey = $"COD_SETTLEMENT:{tenantId}:{settlementId}",
                Lines = lines
            };
            
            var journal = await _postingService.PostJournalAsync(request, ct);
            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post COD settlement {SettlementId}", settlementId);
            return PostingResult.Failed(ex.Message);
        }
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // INVENTORY OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    public async Task<PostingResult> PostInventoryAdjustmentAsync(
        Guid tenantId, decimal costValue, bool isIncrease, string reason, Guid adjustmentId, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        _logger.LogWarning(
            "Rejected inventory adjustment accounting without SQL source authority | TenantId: {TenantId} | AdjustmentId: {AdjustmentId}",
            tenantId,
            adjustmentId);
        return PostingResult.Failed(
            "Inventory adjustment accounting requires an authoritative SQL adjustment workflow.");
    }

    public async Task<PostingResult> PostManualCodSettlementAsync(Guid tenantId, Guid settlementBatchId, CancellationToken ct = default)
    {
        if (_db == null) throw new InvalidOperationException("Manual COD settlement requires SQL authority.");
        var request = await ManualCodSettlementAuthority.BuildPostingAsync(_db, tenantId, settlementBatchId, ct);
        var journal = await _postingService.PostJournalAsync(request, ct);
        return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
    }

    public async Task<PostingResult> PostCarrierRemittanceAsync(Guid tenantId, Guid remittanceId, CancellationToken ct = default)
    {
        if (_db == null) throw new InvalidOperationException("Carrier remittance requires SQL authority.");
        var request = await CarrierRemittanceAuthority.BuildPostingAsync(_db, tenantId, remittanceId, ct);
        var journal = await _postingService.PostJournalAsync(request, ct);
        return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
    }

    private static List<PostingLine> BuildPurchaseReturnLines(
        Return returnObj,
        IReadOnlyDictionary<string, Account> accounts,
        string? cashAccountCode)
    {
        var lines = new List<PostingLine>
        {
            PostingLine.Debit(
                accounts[AccountCodes.SupplierPayables].Id,
                returnObj.TotalAmount,
                $"Reduce supplier payables for {returnObj.ReturnNumber}")
        };
        var inventoryAmount = returnObj.TotalAmount - returnObj.TaxAmount;
        if (inventoryAmount > 0m)
        {
            lines.Add(PostingLine.Credit(
                accounts[AccountCodes.Inventory].Id,
                inventoryAmount,
                $"Inventory reduction for {returnObj.ReturnNumber}"));
        }
        if (returnObj.TaxAmount > 0m)
        {
            lines.Add(PostingLine.Credit(
                accounts[AccountCodes.InputTaxReceivable].Id,
                returnObj.TaxAmount,
                $"Input tax reversal {returnObj.ReturnNumber}"));
        }

        if (returnObj.CashRefundAmount > 0m)
        {
            lines.Add(PostingLine.Debit(
                accounts[cashAccountCode!].Id,
                returnObj.CashRefundAmount,
                $"Cash refund received for {returnObj.ReturnNumber}"));
            lines.Add(PostingLine.Credit(
                accounts[AccountCodes.SupplierPayables].Id,
                returnObj.CashRefundAmount,
                $"Settle supplier refund for {returnObj.ReturnNumber}"));
        }

        return lines;
    }

    private static void ValidatePurchaseReturnSettlement(Return returnObj)
    {
        if (returnObj.RefundMethod is null)
        {
            if (returnObj.CashRefundAmount != 0m ||
                returnObj.CardRefundAmount != 0m ||
                returnObj.CustomerCreditAmount != 0m)
            {
                throw new InvalidOperationException(
                    $"Legacy purchase return {returnObj.ReturnNumber} cannot contain settlement amounts without a method.");
            }

            return;
        }

        if (returnObj.RefundMethod == RefundMethod.AccountCredit &&
            returnObj.CashRefundAmount == 0m &&
            returnObj.CardRefundAmount == 0m &&
            returnObj.CustomerCreditAmount == 0m)
        {
            return;
        }

        if (returnObj.RefundMethod == RefundMethod.Cash &&
            returnObj.CashRefundAmount == returnObj.TotalAmount &&
            returnObj.CardRefundAmount == 0m &&
            returnObj.CustomerCreditAmount == 0m)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Purchase return {returnObj.ReturnNumber} has an unsupported or inconsistent settlement snapshot.");
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════════════
    
    private decimal CalculateTotalCost(SalesInvoice invoice)
    {
        // New POS sale lines carry immutable cost snapshots. Legacy rows fall back to
        // historical behavior only when no snapshot exists.
        if (invoice.Items == null || !invoice.Items.Any())
            return 0;
        
        return invoice.Items.Sum(item =>
        {
            if (item.CostAmountSnapshot.HasValue)
            {
                return item.CostAmountSnapshot.Value;
            }

            var quantity = item.BaseQuantity > 0m ? item.BaseQuantity : item.Quantity;
            return (item.ProductVariant?.CostPrice ?? 0m) * quantity;
        });
    }
    
    private decimal CalculateReturnCost(Return returnObj)
    {
        // Sales returns must reverse the original immutable cost basis when present.
        if (returnObj.Items == null || !returnObj.Items.Any())
            return 0;
        
        return returnObj.Items.Sum(item =>
        {
            if (item.CostAmountSnapshot.HasValue)
            {
                return item.CostAmountSnapshot.Value;
            }

            var quantity = item.BaseQuantity > 0m ? item.BaseQuantity : item.Quantity;
            return (item.ProductVariant?.CostPrice ?? 0m) * quantity;
        });
    }

    private async Task<string> ResolveWarehouseCashboxAccountCodeAsync(
        Guid tenantId,
        Guid warehouseId,
        CancellationToken ct)
    {
        var cashboxAccount = await _cashboxLedgerAccountService.ResolveWarehouseCashboxAccountAsync(
            tenantId,
            warehouseId,
            ct);

        return cashboxAccount.AccountCode;
    }

    private async Task<string> ResolveDefaultCashboxAccountCodeAsync(Guid tenantId, CancellationToken ct)
    {
        var cashboxAccount = await _cashboxLedgerAccountService.ResolveDefaultCashboxAccountAsync(tenantId, ct);
        return cashboxAccount.AccountCode;
    }

    private async Task<string> ResolveExpenseCashboxAccountCodeAsync(Expense expense, CancellationToken ct)
    {
        if (expense.CashboxId.HasValue)
        {
            var cashboxAccount = await _cashboxLedgerAccountService.ResolveCashboxAccountAsync(
                expense.TenantId,
                expense.CashboxId.Value,
                ct);

            return cashboxAccount.AccountCode;
        }

        return await ResolveDefaultCashboxAccountCodeAsync(expense.TenantId, ct);
    }

    private static string BuildSaleJournalIdempotencyKey(
        string prefix,
        Guid tenantId,
        Guid invoiceId,
        string? operationKey)
    {
        return operationKey == null
            ? $"{prefix}:{tenantId}:{invoiceId}"
            : $"{prefix}:{tenantId}:{invoiceId}:{operationKey}";
    }

    private static string BuildExpenseJournalIdempotencyKey(
        Guid tenantId,
        Guid expenseId,
        string? operationKey)
    {
        return operationKey == null
            ? $"EXPENSE:{tenantId}:{expenseId}"
            : $"EXPENSE:{tenantId}:{expenseId}:{operationKey}";
    }

    private static bool IsDebitAdjustment(TransactionType transactionType) => transactionType switch
    {
        TransactionType.DebitNote => true,
        TransactionType.DebitAdjustment => true,
        TransactionType.CreditNote => false,
        TransactionType.CreditAdjustment => false,
        _ => throw new InvalidOperationException($"Unsupported adjustment transaction type '{transactionType}'.")
    };

    private static ArgumentException CreateInvalidPaymentMethodException(Expense expense)
    {
        return new ArgumentException(
            $"Invalid or unsupported payment method '{expense.PaymentMethod}' for expense. " +
            $"Supported methods: Cash, Visa, Card, BankTransfer. ExpenseId: {expense.Id}, TenantId: {expense.TenantId}",
            nameof(expense.PaymentMethod));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COMMERCEHUB MARKETPLACE OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<PostingResult> PostCommerceHubFinancialTransactionAsync(
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
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation(
                "Posting CommerceHub financial transaction to GL | TenantId: {TenantId} | TransactionId: {TransactionId} | Type: {Type} | Direction: {Direction} | EntityType: {EntityType} | Bucket: {Bucket} | Amount: {Amount} {Currency}",
                tenantId, transactionId, transactionType, direction, entityType, financialBucket, amount, currency);

            var transactionTypeValue = ParseCommerceHubEnum<CommerceHubFinancialTransactionType>(transactionType, nameof(transactionType));
            var directionValue = ParseCommerceHubEnum<CommerceHubFinancialTransactionDirection>(direction, nameof(direction));
            var entityTypeValue = ParseCommerceHubEnum<CommerceHubFinancialEntityType>(entityType, nameof(entityType));
            var bucketValue = ParseCommerceHubEnum<CommerceHubFinancialBucket>(financialBucket, nameof(financialBucket));

            var requiredAccountCodes = ResolveCommerceHubAccountCodes(
                entityTypeValue,
                transactionTypeValue,
                bucketValue);
            var accounts = await _accountResolver.GetByCodesAsync(tenantId, requiredAccountCodes, ct);
            var (debitAccountCode, creditAccountCode) = ResolveCommerceHubPostingAccounts(
                entityTypeValue,
                transactionTypeValue,
                directionValue,
                bucketValue);

            var debitLine = PostingLine.Debit(accounts[debitAccountCode].Id, amount, description);
            var creditLine = PostingLine.Credit(accounts[creditAccountCode].Id, amount, description);

            var request = new PostingRequest
            {
                TenantId        = tenantId,
                JournalDate     = occurredAtUtc,
                // SourceType MUST equal the literal checked by EnsureCommerceHubLedgerAccountingCoverageAsync
                SourceType      = "CommerceHubFinancialTransaction",
                SourceId        = transactionId,
                SourceReference = transactionId.ToString("D"),
                Description     = description,
                IdempotencyKey  = $"COMMERCEHUB_TX:{tenantId:N}:{transactionId:N}",
                Lines           = new List<PostingLine> { debitLine, creditLine }
            };

            var journal = await _postingService.PostJournalAsync(request, ct);

            _logger.LogInformation(
                "Posted CommerceHub financial transaction to GL | TenantId: {TenantId} | TransactionId: {TransactionId} | JournalNumber: {JournalNumber}",
                tenantId, transactionId, journal.JournalNumber);

            return PostingResult.Succeeded(journal.Id, journal.JournalNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to post CommerceHub financial transaction to GL | TenantId: {TenantId} | TransactionId: {TransactionId}",
                tenantId, transactionId);
            return PostingResult.Failed(ex.Message);
        }
    }

    private static TEnum ParseCommerceHubEnum<TEnum>(string value, string parameterName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ArgumentOutOfRangeException(parameterName, value, $"{typeof(TEnum).Name} value is invalid.");
    }

    private static IReadOnlyCollection<string> ResolveCommerceHubAccountCodes(
        CommerceHubFinancialEntityType entityType,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialBucket bucket)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal)
        {
            AccountCodes.TreasurySuspense
        };

        if (entityType == CommerceHubFinancialEntityType.Vendor ||
            bucket == CommerceHubFinancialBucket.Payable ||
            transactionType == CommerceHubFinancialTransactionType.Payout)
        {
            codes.Add(AccountCodes.MarketplaceVendorPayable);
        }

        // CRITICAL FIX: Include BankAccount for both Platform AND Vendor cash transactions
        // ResolveCommerceHubPrimaryAccount returns BankAccount for vendor cash rows,
        // so we must include it here to prevent missing-key lookups in the accounts dictionary
        if ((entityType == CommerceHubFinancialEntityType.Platform ||
             entityType == CommerceHubFinancialEntityType.Vendor) &&
            bucket == CommerceHubFinancialBucket.Cash)
        {
            codes.Add(AccountCodes.BankAccount);
        }

        // Include the dedicated vendor receivable account for CommerceHub debt recovery.
        if (entityType == CommerceHubFinancialEntityType.Vendor &&
            bucket == CommerceHubFinancialBucket.Receivable)
        {
            codes.Add(AccountCodes.MarketplaceVendorReceivable);
        }

        if (entityType == CommerceHubFinancialEntityType.Platform &&
            transactionType is CommerceHubFinancialTransactionType.Commission)
        {
            codes.Add(AccountCodes.EcommerceSales);
        }

        if (entityType == CommerceHubFinancialEntityType.Platform &&
            transactionType is CommerceHubFinancialTransactionType.Promotion)
        {
            codes.Add(AccountCodes.Expenses);
        }

        return codes;
    }

    private static (string DebitAccountCode, string CreditAccountCode) ResolveCommerceHubPostingAccounts(
        CommerceHubFinancialEntityType entityType,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        CommerceHubFinancialBucket bucket)
    {
        var primaryAccount = ResolveCommerceHubPrimaryAccount(entityType, transactionType, bucket);
        var offsetAccount = AccountCodes.TreasurySuspense;

        var debitPrimaryAccount = bucket == CommerceHubFinancialBucket.Cash
            ? direction == CommerceHubFinancialTransactionDirection.Credit
            : direction == CommerceHubFinancialTransactionDirection.Debit;

        return debitPrimaryAccount
            ? (primaryAccount, offsetAccount)
            : (offsetAccount, primaryAccount);
    }

    private static string ResolveCommerceHubPrimaryAccount(
        CommerceHubFinancialEntityType entityType,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialBucket bucket)
    {
        if (entityType == CommerceHubFinancialEntityType.Vendor)
        {
            return bucket switch
            {
                CommerceHubFinancialBucket.Payable => AccountCodes.MarketplaceVendorPayable,
                CommerceHubFinancialBucket.Receivable => AccountCodes.MarketplaceVendorReceivable,
                CommerceHubFinancialBucket.Cash => AccountCodes.BankAccount,
                _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, "CommerceHub financial bucket is invalid.")
            };
        }

        return transactionType switch
        {
            CommerceHubFinancialTransactionType.Commission => AccountCodes.EcommerceSales,
            CommerceHubFinancialTransactionType.Promotion => AccountCodes.Expenses,
            CommerceHubFinancialTransactionType.Payout => AccountCodes.MarketplaceVendorPayable,
            _ when bucket == CommerceHubFinancialBucket.Cash => AccountCodes.BankAccount,
            _ when bucket == CommerceHubFinancialBucket.Payable => AccountCodes.MarketplaceVendorPayable,
            _ => AccountCodes.TreasurySuspense
        };
    }
}
