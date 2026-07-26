using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>
/// Resolves ledger accounts by code for a tenant.
/// </summary>
public class AccountResolver : IAccountResolver
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AccountResolver> _logger;
    
    public AccountResolver(ApplicationDbContext db, ILogger<AccountResolver> logger)
    {
        _db = db;
        _logger = logger;
    }
    
    public async Task<Account> GetByCodeAsync(Guid tenantId, string accountCode, CancellationToken ct = default)
    {
        var account = await GetByCodeOrDefaultAsync(tenantId, accountCode, ct);
        
        if (account == null)
        {
            _logger.LogError("Account {AccountCode} not found for tenant {TenantId}", accountCode, tenantId);
            throw new InvalidOperationException($"Account {accountCode} not found. Has the Chart of Accounts been seeded?");
        }
        
        return account;
    }
    
    public async Task<Account?> GetByCodeOrDefaultAsync(Guid tenantId, string accountCode, CancellationToken ct = default)
    {
        return await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.AccountCode == accountCode, ct);
    }
    
    public async Task<Dictionary<string, Account>> GetByCodesAsync(Guid tenantId, IEnumerable<string> accountCodes, CancellationToken ct = default)
    {
        var codes = accountCodes.ToList();
        
        var accounts = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && codes.Contains(a.AccountCode))
            .ToDictionaryAsync(a => a.AccountCode, ct);
        
        var missing = codes.Except(accounts.Keys).ToList();
        if (missing.Any())
        {
            _logger.LogError("Accounts {AccountCodes} not found for tenant {TenantId}", string.Join(", ", missing), tenantId);
            throw new InvalidOperationException($"Accounts not found: {string.Join(", ", missing)}. Has the Chart of Accounts been seeded?");
        }
        
        return accounts;
    }
    
    public async Task<Guid> GetPaymentAccountIdAsync(Guid tenantId, string paymentMethodCode, CancellationToken ct = default)
    {
        var method = await _db.PaymentMethods
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(pm => pm.TenantId == tenantId && pm.Code == paymentMethodCode, ct);
        
        if (method == null)
        {
            _logger.LogError("PaymentMethod {Code} not found for tenant {TenantId}", paymentMethodCode, tenantId);
            throw new InvalidOperationException($"Payment method {paymentMethodCode} not found");
        }
        
        // Prefer target account (direct funds like cash), fall back to clearing (for cards/gateways)
        return method.TargetAccountId ?? method.ClearingAccountId 
            ?? throw new InvalidOperationException($"Payment method {paymentMethodCode} has no target or clearing account");
    }
    
    public async Task<Guid?> GetFeesAccountIdAsync(Guid tenantId, string paymentMethodCode, CancellationToken ct = default)
    {
        var method = await _db.PaymentMethods
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(pm => pm.TenantId == tenantId && pm.Code == paymentMethodCode, ct);
        
        return method?.FeesAccountId;
    }
}
