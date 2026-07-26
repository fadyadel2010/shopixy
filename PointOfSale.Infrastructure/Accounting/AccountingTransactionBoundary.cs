using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;
using System.Data;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>
/// Transaction boundary wrapper for atomic business + accounting operations.
/// 
/// PHASE 3.5: Guarantees that business writes and accounting writes are atomic.
/// 
/// Usage:
///   await _transactionBoundary.ExecuteAsync(async (db, outbox) => {
///       var sale = await _salesService.CreateAsync(...);
///       await outbox.EnqueuePosSaleAsync(tenantId, sale.Id);
///       return sale;
///   });
/// 
/// If any part fails, the entire transaction rolls back.
/// </summary>
public interface IAccountingTransactionBoundary
{
    /// <summary>
    /// Executes a business operation with accounting within a transaction.
    /// </summary>
    Task<T> ExecuteAsync<T>(
        Func<ApplicationDbContext, IAccountingOutboxService, Task<T>> operation,
        CancellationToken ct = default);
    
    /// <summary>
    /// Executes a business operation with accounting within a transaction (no return value).
    /// </summary>
    Task ExecuteAsync(
        Func<ApplicationDbContext, IAccountingOutboxService, Task> operation,
        CancellationToken ct = default);
}

/// <summary>
/// Implementation of atomic business + accounting transactions.
/// </summary>
public class AccountingTransactionBoundary : IAccountingTransactionBoundary
{
    private readonly ApplicationDbContext _db;
    private readonly IAccountingOutboxService _outboxService;
    private readonly ILogger<AccountingTransactionBoundary> _logger;
    
    public AccountingTransactionBoundary(
        ApplicationDbContext db,
        IAccountingOutboxService outboxService,
        ILogger<AccountingTransactionBoundary> logger)
    {
        _db = db;
        _outboxService = outboxService;
        _logger = logger;
    }
    
    public async Task<T> ExecuteAsync<T>(
        Func<ApplicationDbContext, IAccountingOutboxService, Task<T>> operation,
        CancellationToken ct = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            
            try
            {
                // Execute the business operation with outbox enqueue
                var result = await operation(_db, _outboxService);
                
                // Commit both business data AND outbox message
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                
                _logger.LogDebug("AccountingTransactionBoundary: Transaction committed successfully");
                
                return result;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                _logger.LogError(ex, "AccountingTransactionBoundary: Transaction rolled back");
                throw;
            }
        });
    }
    
    public async Task ExecuteAsync(
        Func<ApplicationDbContext, IAccountingOutboxService, Task> operation,
        CancellationToken ct = default)
    {
        await ExecuteAsync<object?>(async (db, outbox) =>
        {
            await operation(db, outbox);
            return null;
        }, ct);
    }
}

/// <summary>
/// Extension methods for using the transaction boundary pattern.
/// </summary>
public static class AccountingTransactionExtensions
{
    /// <summary>
    /// Creates a transaction scope that includes outbox message delivery.
    /// 
    /// Usage example in a service:
    /// 
    /// public async Task<SalesInvoice> CreateSaleAsync(CreateSaleRequest request)
    /// {
    ///     return await _transactionBoundary.ExecuteAsync(async (db, outbox) =>
    ///     {
    ///         // 1. Create the sale (business operation)
    ///         var invoice = new SalesInvoice { ... };
    ///         db.SalesInvoices.Add(invoice);
    ///         
    ///         // 2. Enqueue for accounting (same transaction)
    ///         await outbox.EnqueuePosSaleAsync(tenantId, invoice.Id);
    ///         
    ///         // Transaction commits both or rolls back both
    ///         return invoice;
    ///     });
    /// }
    /// </summary>
    public static async Task<T> WithAccountingAsync<T>(
        this ApplicationDbContext db,
        IAccountingOutboxService outboxService,
        ILogger logger,
        Func<ApplicationDbContext, IAccountingOutboxService, Task<T>> operation,
        CancellationToken ct = default)
    {
        var boundary = new AccountingTransactionBoundary(
            db, 
            outboxService, 
            (logger as ILogger<AccountingTransactionBoundary>) ?? 
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AccountingTransactionBoundary>.Instance);
        
        return await boundary.ExecuteAsync(operation, ct);
    }
}
