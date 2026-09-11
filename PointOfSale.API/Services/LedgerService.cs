using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using System.Data;
using PointOfSale.API.Services.Audit;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services;

public class LedgerService : ILedgerService, IPartyBalanceMaintenanceService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<LedgerService> _logger;
    private readonly IAuditService _auditService;

    public LedgerService(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<LedgerService> logger,
        IAuditService auditService)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
        _auditService = auditService;
    }

    public async Task CreateLedgerEntry(
        PartyType partyType,
        Guid partyId,
        DateTime transactionDate,
        TransactionType transactionType,
        string documentNumber,
        decimal debitAmount,
        decimal creditAmount,
        Guid? referenceId,
        string? notes,
        Guid? userId = null)
    {
        await ApplyPartyTransactionAsync(
            partyType,
            partyId,
            transactionDate,
            transactionType,
            documentNumber,
            debitAmount,
            creditAmount,
            referenceId,
            notes,
            userId);
    }

    public Task<decimal> ApplySupplierTransaction(
        Guid supplierId,
        DateTime transactionDate,
        TransactionType transactionType,
        string documentNumber,
        decimal debitAmount,
        decimal creditAmount,
        Guid? referenceId,
        string? notes,
        Guid? userId = null)
    {
        return ApplyPartyTransactionAsync(
            PartyType.Supplier,
            supplierId,
            transactionDate,
            transactionType,
            documentNumber,
            debitAmount,
            creditAmount,
            referenceId,
            notes,
            userId);
    }

    public Task<decimal> ApplyCustomerTransaction(
        Guid customerId,
        DateTime transactionDate,
        TransactionType transactionType,
        string documentNumber,
        decimal debitAmount,
        decimal creditAmount,
        Guid? referenceId,
        string? notes,
        Guid? userId = null)
    {
        return ApplyPartyTransactionAsync(
            PartyType.Customer,
            customerId,
            transactionDate,
            transactionType,
            documentNumber,
            debitAmount,
            creditAmount,
            referenceId,
            notes,
            userId);
    }

    public async Task<decimal> RebuildPartyTransactionBalancesAsync(
        PartyType partyType,
        Guid partyId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        if (partyType == PartyType.Customer)
        {
            var transactionsById = (await _context.CustomerTransactions
                    .Where(transaction =>
                        transaction.TenantId == tenantId &&
                        transaction.CustomerId == partyId)
                    .ToListAsync(cancellationToken))
                .ToDictionary(transaction => transaction.Id);

            foreach (var trackedEntry in _context.ChangeTracker.Entries<CustomerTransaction>()
                         .Where(entry =>
                             entry.State != EntityState.Detached &&
                             entry.State != EntityState.Deleted &&
                             !entry.Entity.IsDeleted &&
                             entry.Entity.TenantId == tenantId &&
                             entry.Entity.CustomerId == partyId))
            {
                transactionsById[trackedEntry.Entity.Id] = trackedEntry.Entity;
            }

            return RebuildCustomerTransactionBalances(transactionsById.Values);
        }

        var supplierTransactionsById = (await _context.SupplierTransactions
                .Where(transaction =>
                    transaction.TenantId == tenantId &&
                    transaction.SupplierId == partyId)
                .ToListAsync(cancellationToken))
            .ToDictionary(transaction => transaction.Id);

        foreach (var trackedEntry in _context.ChangeTracker.Entries<SupplierTransaction>()
                     .Where(entry =>
                         entry.State != EntityState.Detached &&
                         entry.State != EntityState.Deleted &&
                         !entry.Entity.IsDeleted &&
                         entry.Entity.TenantId == tenantId &&
                         entry.Entity.SupplierId == partyId))
        {
            supplierTransactionsById[trackedEntry.Entity.Id] = trackedEntry.Entity;
        }

        return RebuildSupplierTransactionBalances(supplierTransactionsById.Values);
    }

    public async Task<decimal> RebuildPartyBalanceAsync(
        PartyType partyType,
        Guid partyId,
        string reason,
        bool writeAuditTrail = true,
        [CallerMemberName] string? requestedBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var ownsTransaction = _context.Database.CurrentTransaction == null && _context.Database.IsRelational();
        await using var transaction = ownsTransaction
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        try
        {
            var lockedParty = await LockPartyForUpdateAsync(partyType, partyId);
            var balanceBefore = partyType == PartyType.Customer
                ? lockedParty.Customer!.Balance
                : lockedParty.Supplier!.Balance;
            var rebuiltBalance = await RebuildTrackedBalancesAsync(partyType, partyId, lockedParty);

            if (ownsTransaction)
            {
                await _context.SaveChangesAsync(cancellationToken);
                await transaction!.CommitAsync(cancellationToken);
            }

            // Audit and logging after commit - failures here should not trigger rollback
            try
            {
                _logger.LogWarning(
                    "Ledger balance rebuild executed | PartyType: {PartyType} | PartyId: {PartyId} | RequestedBy: {RequestedBy} | PreviousBalance: {PreviousBalance} | RebuiltBalance: {RebuiltBalance} | Reason: {Reason}",
                    partyType,
                    partyId,
                    requestedBy ?? "Unknown",
                    balanceBefore,
                    rebuiltBalance,
                    reason);

                if (writeAuditTrail)
                {
                    await _auditService.LogAction(
                        actionType: "RebuildBalance",
                        entityType: partyType.ToString(),
                        entityId: partyId.ToString(),
                        description: $"Party balance rebuilt from ledger. Reason: {reason}",
                        beforeData: new
                        {
                            PartyType = partyType,
                            PartyId = partyId,
                            Balance = balanceBefore,
                        },
                        afterData: new
                        {
                            PartyType = partyType,
                            PartyId = partyId,
                            Balance = rebuiltBalance,
                            RequestedBy = requestedBy ?? "Unknown",
                            Reason = reason,
                        },
                        reference: requestedBy ?? (reason.Length <= 100 ? reason : reason[..100]),
                        success: true,
                        source: "Internal");
                }
            }
            catch (Exception auditEx)
            {
                _logger.LogError(auditEx, "Failed to log audit trail for balance rebuild of {PartyType} {PartyId}", partyType, partyId);
            }

            return rebuiltBalance;
        }
        catch (Exception ex)
        {
            if (ownsTransaction && transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _logger.LogError(
                ex,
                "Error rebuilding ledger balances for {PartyType} {PartyId}. Reason: {Reason}",
                partyType,
                partyId,
                reason);
            throw;
        }
    }

    private async Task<decimal> ApplyPartyTransactionAsync(
        PartyType partyType,
        Guid partyId,
        DateTime transactionDate,
        TransactionType transactionType,
        string documentNumber,
        decimal debitAmount,
        decimal creditAmount,
        Guid? referenceId,
        string? notes,
        Guid? userId)
    {
        var ownsTransaction = _context.Database.CurrentTransaction == null && _context.Database.IsRelational();
        await using var transaction = ownsTransaction
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
            : null;

        try
        {
            var lockedParty = await LockPartyForUpdateAsync(partyType, partyId);
            await EnsureOpeningBalanceNotAlreadyAppliedAsync(partyType, partyId, transactionType);

            var ledger = new AccountLedger
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,
                PartyType = partyType,
                PartyId = partyId,
                TransactionDate = transactionDate,
                TransactionType = transactionType,
                DocumentNumber = documentNumber,
                DebitAmount = debitAmount,
                CreditAmount = creditAmount,
                ReferenceId = referenceId,
                Notes = notes,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.AccountLedgers.Add(ledger);
            var newBalance = await RecalculateLedgerBalancesAsync(partyType, partyId);
            SetTrackedPartyBalance(lockedParty, partyType, newBalance);

            if (ownsTransaction)
            {
                await _context.SaveChangesAsync();
                await transaction!.CommitAsync();
            }

            _logger.LogInformation(
                "Ledger entry created: {PartyType} {PartyId}, Type: {TransactionType}, Debit: {Debit}, Credit: {Credit}, Balance: {Balance}",
                partyType,
                partyId,
                transactionType,
                debitAmount,
                creditAmount,
                newBalance);

            return newBalance;
        }
        catch (DuplicateOpeningBalanceException)
        {
            if (ownsTransaction && transaction != null)
            {
                await transaction.RollbackAsync();
            }

            // This is an expected 409 conflict - rethrow without logging as error
            throw;
        }
        catch (Exception ex)
        {
            if (ownsTransaction && transaction != null)
            {
                await transaction.RollbackAsync();
            }

            _logger.LogError(ex, "Error creating ledger entry for {PartyType} {PartyId}", partyType, partyId);
            throw;
        }
    }

    private async Task<decimal> RecalculateLedgerBalancesAsync(PartyType partyType, Guid partyId)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var ledgersById = (await _context.AccountLedgers
            .Where(a =>
                a.TenantId == tenantId &&
                a.PartyType == partyType &&
                a.PartyId == partyId)
            .ToListAsync())
            .ToDictionary(entry => entry.Id);

        foreach (var trackedEntry in _context.ChangeTracker.Entries<AccountLedger>()
                     .Where(entry =>
                         entry.State != EntityState.Detached &&
                         entry.State != EntityState.Deleted &&
                         !entry.Entity.IsDeleted &&
                         entry.Entity.TenantId == tenantId &&
                         entry.Entity.PartyType == partyType &&
                         entry.Entity.PartyId == partyId))
        {
            ledgersById[trackedEntry.Entity.Id] = trackedEntry.Entity;
        }

        decimal runningBalance = 0m;
        foreach (var entry in ledgersById.Values
                     .OrderBy(entry => entry.TransactionDate)
                     .ThenBy(entry => entry.CreatedAt)
                     .ThenBy(entry => entry.Id))
        {
            runningBalance += entry.DebitAmount - entry.CreditAmount;
            entry.RunningBalance = runningBalance;
        }

        return runningBalance;
    }

    private async Task EnsureOpeningBalanceNotAlreadyAppliedAsync(
        PartyType partyType,
        Guid partyId,
        TransactionType transactionType)
    {
        if (transactionType != TransactionType.OpeningBalance)
        {
            return;
        }

        var tenantId = _tenantContext.RequiredTenantId;
        var pendingOpeningBalanceExists = _context.ChangeTracker.Entries<AccountLedger>()
            .Any(entry =>
                entry.State != EntityState.Detached &&
                entry.State != EntityState.Deleted &&
                !entry.Entity.IsDeleted &&
                entry.Entity.TenantId == tenantId &&
                entry.Entity.PartyType == partyType &&
                entry.Entity.PartyId == partyId &&
                entry.Entity.TransactionType == TransactionType.OpeningBalance);

        if (pendingOpeningBalanceExists)
        {
            throw new DuplicateOpeningBalanceException(partyType, partyId);
        }

        var existingOpeningBalanceExists = await _context.AccountLedgers
            .IgnoreQueryFilters()
            .AnyAsync(entry =>
                entry.TenantId == tenantId &&
                !entry.IsDeleted &&
                entry.PartyType == partyType &&
                entry.PartyId == partyId &&
                entry.TransactionType == TransactionType.OpeningBalance);

        if (existingOpeningBalanceExists)
        {
            throw new DuplicateOpeningBalanceException(partyType, partyId);
        }
    }

    private bool IsPostgresProvider() =>
        _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<decimal> RebuildTrackedBalancesAsync(
        PartyType partyType,
        Guid partyId,
        LockedParty lockedParty)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var runningBalance = await RecalculateLedgerBalancesAsync(partyType, partyId);

        await RebuildPartyTransactionBalancesAsync(partyType, partyId);

        SetTrackedPartyBalance(lockedParty, partyType, runningBalance);
        return runningBalance;
    }

    private static decimal RebuildCustomerTransactionBalances(IEnumerable<CustomerTransaction> transactions)
    {
        decimal balance = 0m;
        foreach (var transaction in transactions
                     .OrderBy(transaction => transaction.TransactionDate)
                     .ThenBy(transaction => transaction.CreatedAt)
                     .ThenBy(transaction => transaction.Id))
        {
            balance += transaction.DebitAmount - transaction.CreditAmount;
            transaction.BalanceAfter = balance;
        }
        return balance;
    }

    private static decimal RebuildSupplierTransactionBalances(IEnumerable<SupplierTransaction> transactions)
    {
        decimal balance = 0m;
        foreach (var transaction in transactions
                     .OrderBy(transaction => transaction.TransactionDate)
                     .ThenBy(transaction => transaction.CreatedAt)
                     .ThenBy(transaction => transaction.Id))
        {
            balance += transaction.DebitAmount - transaction.CreditAmount;
            transaction.BalanceAfter = balance;
        }
        return balance;
    }

    private static void SetTrackedPartyBalance(LockedParty lockedParty, PartyType partyType, decimal balance)
    {
        if (partyType == PartyType.Customer)
        {
            if (lockedParty.Customer != null)
            {
                lockedParty.Customer.Balance = balance;
            }

            return;
        }

        if (lockedParty.Supplier != null)
        {
            lockedParty.Supplier.Balance = balance;
        }
    }

    private async Task<LockedParty> LockPartyForUpdateAsync(PartyType partyType, Guid partyId)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        if (partyType == PartyType.Customer)
        {
            var customer = await LoadCustomerForUpdateAsync(tenantId, partyId);
            if (customer == null)
            {
                throw new InvalidOperationException($"Customer {partyId} was not found for tenant {tenantId}.");
            }

            return new LockedParty(customer, null);
        }

        var supplier = await LoadSupplierForUpdateAsync(tenantId, partyId);
        if (supplier == null)
        {
            throw new InvalidOperationException($"Supplier {partyId} was not found for tenant {tenantId}.");
        }

        return new LockedParty(null, supplier);
    }

    private async Task<Customer?> LoadCustomerForUpdateAsync(Guid tenantId, Guid customerId)
    {
        if (IsPostgresProvider())
        {
            return await _context.Customers
                .FromSqlInterpolated($@"
                    SELECT * FROM ""Customers""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {customerId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync();
        }

        if (IsSqlServerProvider())
        {
            return await _context.Customers
                .FromSqlInterpolated($@"
                    SELECT * FROM [Customers] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {customerId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync();
        }

        return await _context.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(customer =>
                customer.TenantId == tenantId &&
                customer.Id == customerId &&
                !customer.IsDeleted);
    }

    private async Task<Supplier?> LoadSupplierForUpdateAsync(Guid tenantId, Guid supplierId)
    {
        if (IsPostgresProvider())
        {
            return await _context.Suppliers
                .FromSqlInterpolated($@"
                    SELECT * FROM ""Suppliers""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {supplierId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync();
        }

        if (IsSqlServerProvider())
        {
            return await _context.Suppliers
                .FromSqlInterpolated($@"
                    SELECT * FROM [Suppliers] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {supplierId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync();
        }

        return await _context.Suppliers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(supplier =>
                supplier.TenantId == tenantId &&
                supplier.Id == supplierId &&
                !supplier.IsDeleted);
    }

    private sealed record LockedParty(Customer? Customer, Supplier? Supplier);
}
