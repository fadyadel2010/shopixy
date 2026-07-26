using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>
/// Seeds the baseline Chart of Accounts for a tenant.
/// 
/// BEHAVIOR:
/// - Idempotent: Can be called multiple times safely
/// - Creates all system accounts if they don't exist
/// - Creates default payment methods
/// - Creates default treasury accounts
/// 
/// This seeder is called:
/// 1. When a new tenant is provisioned
/// 2. Via admin endpoint POST /api/admin/accounting/seed
/// 3. On system upgrade to add new required accounts
/// </summary>
public class TenantAccountSeeder : ITenantAccountSeeder
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<TenantAccountSeeder> _logger;
    
    public TenantAccountSeeder(
        ApplicationDbContext db,
        ILogger<TenantAccountSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }
    
    /// <inheritdoc />
    public async Task SeedAsync(Guid tenantId, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting full seed for tenant {TenantId}", tenantId);
        
        await SeedAccountsAsync(tenantId, ct);
        await SeedPaymentMethodsAsync(tenantId, ct);
        await SeedTreasuryAccountsAsync(tenantId, ct);
        
        _logger.LogInformation("Completed full seed for tenant {TenantId}", tenantId);
    }
    
    /// <inheritdoc />
    public async Task SeedAccountsAsync(Guid tenantId, CancellationToken ct = default)
    {
        _logger.LogInformation("Seeding accounts for tenant {TenantId}", tenantId);
        
        // Get existing account codes for this tenant
        var existingCodes = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .Select(a => a.AccountCode)
            .ToHashSetAsync(ct);
        
        _logger.LogDebug("Found {Count} existing accounts", existingCodes.Count);
        
        // Track created accounts by code for parent lookup
        var createdAccounts = new Dictionary<string, Account>();
        
        // Also load existing accounts for parent lookup
        var existingAccountsByCode = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .ToDictionaryAsync(a => a.AccountCode, ct);
        
        // Process templates in order (parents before children)
        foreach (var template in SystemAccountTemplates.All)
        {
            if (existingCodes.Contains(template.Code))
            {
                _logger.LogDebug("Skipping existing account {Code}", template.Code);
                continue;
            }
            
            // Resolve parent account ID
            Guid? parentAccountId = null;
            if (!string.IsNullOrEmpty(template.ParentCode))
            {
                // Check created accounts first, then existing
                if (createdAccounts.TryGetValue(template.ParentCode, out var createdParent))
                {
                    parentAccountId = createdParent.Id;
                }
                else if (existingAccountsByCode.TryGetValue(template.ParentCode, out var existingParent))
                {
                    parentAccountId = existingParent.Id;
                }
                else
                {
                    _logger.LogWarning(
                        "Parent account {ParentCode} not found for {Code}, creating without parent",
                        template.ParentCode, template.Code);
                }
            }
            
            // Create the account
            var account = Account.Create(
                tenantId: tenantId,
                accountCode: template.Code,
                name: template.Name,
                accountType: template.Type,
                parentAccountId: parentAccountId,
                isSystem: true, // All seeded accounts are system accounts
                isClearing: template.IsClearing,
                preventNegative: template.PreventNegative);
            
            // Set optional properties
            account.NameAr = template.NameAr;
            account.Description = template.Description;
            account.ClearingSourceType = template.ClearingSourceType;
            account.ClearingSourceCode = template.ClearingSourceCode;
            account.ExpectedSettlementDays = template.ExpectedSettlementDays;
            account.Level = parentAccountId.HasValue ? 2 : 1;
            
            _db.LedgerAccounts.Add(account);
            createdAccounts[template.Code] = account;
            
            _logger.LogDebug("Created account {Code}: {Name}", template.Code, template.Name);
        }
        
        if (createdAccounts.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Created {Count} accounts for tenant {TenantId}", 
                createdAccounts.Count, tenantId);
        }
        else
        {
            _logger.LogInformation("No new accounts to create for tenant {TenantId}", tenantId);
        }
    }
    
    /// <inheritdoc />
    public async Task SeedPaymentMethodsAsync(Guid tenantId, CancellationToken ct = default)
    {
        _logger.LogInformation("Seeding payment methods for tenant {TenantId}", tenantId);
        
        // Get existing payment method codes
        var existingCodes = await _db.PaymentMethods
            .IgnoreQueryFilters()
            .Where(pm => pm.TenantId == tenantId)
            .Select(pm => pm.Code)
            .ToHashSetAsync(ct);
        
        // Load accounts by code for lookup
        var accountsByCode = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .ToDictionaryAsync(a => a.AccountCode, ct);
        
        var created = new List<PaymentMethod>();
        
        // CASH → Cashbox Main (immediate settlement)
        if (!existingCodes.Contains(PaymentMethodCodes.Cash) && 
            accountsByCode.TryGetValue("1010", out var cashboxAccount))
        {
            created.Add(PaymentMethod.CreateImmediate(
                tenantId, PaymentMethodCodes.Cash, "Cash", cashboxAccount.Id, isSystem: true));
        }
        
        // CARD → Visa Clearing (deferred settlement)
        if (!existingCodes.Contains(PaymentMethodCodes.Card) &&
            accountsByCode.TryGetValue("1110", out var visaClearingAccount) &&
            accountsByCode.TryGetValue("6010", out var gatewayFeesAccount))
        {
            created.Add(PaymentMethod.CreateDeferred(
                tenantId, PaymentMethodCodes.Card, "Card/Visa", 
                visaClearingAccount.Id, gatewayFeesAccount.Id, isSystem: true));
        }
        
        // STRIPE → Stripe Clearing (deferred settlement with fees)
        if (!existingCodes.Contains(PaymentMethodCodes.Stripe) &&
            accountsByCode.TryGetValue("1120", out var stripeClearingAccount) &&
            accountsByCode.TryGetValue("6010", out var stripeFeesAccount))
        {
            created.Add(PaymentMethod.CreateDeferred(
                tenantId, PaymentMethodCodes.Stripe, "Stripe",
                stripeClearingAccount.Id, stripeFeesAccount.Id, isSystem: true));
        }
        
        // COD → COD Clearing (deferred settlement)
        if (!existingCodes.Contains(PaymentMethodCodes.Cod) &&
            accountsByCode.TryGetValue("1130", out var codClearingAccount))
        {
            created.Add(PaymentMethod.CreateDeferred(
                tenantId, PaymentMethodCodes.Cod, "Cash on Delivery",
                codClearingAccount.Id, null, isSystem: true));
        }
        
        // WALLET → Wallet Clearing (deferred settlement with fees)
        if (!existingCodes.Contains(PaymentMethodCodes.Wallet) &&
            accountsByCode.TryGetValue("1140", out var walletClearingAccount) &&
            accountsByCode.TryGetValue("6010", out var walletFeesAccount))
        {
            created.Add(PaymentMethod.CreateDeferred(
                tenantId, PaymentMethodCodes.Wallet, "Mobile Wallet",
                walletClearingAccount.Id, walletFeesAccount.Id, isSystem: true));
        }
        
        // BANK_TRANSFER → Bank Account (immediate settlement)
        if (!existingCodes.Contains(PaymentMethodCodes.BankTransfer) &&
            accountsByCode.TryGetValue("1030", out var bankAccount))
        {
            created.Add(PaymentMethod.CreateImmediate(
                tenantId, PaymentMethodCodes.BankTransfer, "Bank Transfer", 
                bankAccount.Id, isSystem: true));
        }
        
        if (created.Count > 0)
        {
            // Set display order
            for (int i = 0; i < created.Count; i++)
            {
                created[i].DisplayOrder = i;
            }
            
            _db.PaymentMethods.AddRange(created);
            await _db.SaveChangesAsync(ct);
            
            _logger.LogInformation("Created {Count} payment methods for tenant {TenantId}",
                created.Count, tenantId);
        }
        else
        {
            _logger.LogInformation("No new payment methods to create for tenant {TenantId}", tenantId);
        }
    }
    
    /// <inheritdoc />
    public async Task SeedTreasuryAccountsAsync(Guid tenantId, CancellationToken ct = default)
    {
        _logger.LogInformation("Seeding treasury accounts for tenant {TenantId}", tenantId);
        
        // Get existing treasury accounts
        var existingAccountIds = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .Where(ta => ta.TenantId == tenantId)
            .Select(ta => ta.AccountId)
            .ToHashSetAsync(ct);
        
        // Load accounts for lookup
        var accountsByCode = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .ToDictionaryAsync(a => a.AccountCode, ct);
        
        var created = new List<TreasuryAccount>();
        
        // Cashbox Main
        if (accountsByCode.TryGetValue("1010", out var cashboxAccount) &&
            !existingAccountIds.Contains(cashboxAccount.Id))
        {
            created.Add(TreasuryAccount.CreateCashbox(
                tenantId, cashboxAccount.Id, "Main Cashbox", 
                location: "Main Branch", isDefault: true));
        }
        
        // Bank Account
        if (accountsByCode.TryGetValue("1030", out var bankAccount) &&
            !existingAccountIds.Contains(bankAccount.Id))
        {
            created.Add(TreasuryAccount.CreateBank(
                tenantId, bankAccount.Id, "Primary Bank Account",
                isDefault: true));
        }
        
        // Visa Clearing
        if (accountsByCode.TryGetValue("1110", out var visaClearing) &&
            !existingAccountIds.Contains(visaClearing.Id))
        {
            created.Add(TreasuryAccount.CreateClearing(
                tenantId, visaClearing.Id, "Visa Clearing"));
        }
        
        // Stripe Clearing
        if (accountsByCode.TryGetValue("1120", out var stripeClearing) &&
            !existingAccountIds.Contains(stripeClearing.Id))
        {
            created.Add(TreasuryAccount.CreateClearing(
                tenantId, stripeClearing.Id, "Stripe Clearing"));
        }
        
        // COD Clearing
        if (accountsByCode.TryGetValue("1130", out var codClearing) &&
            !existingAccountIds.Contains(codClearing.Id))
        {
            created.Add(TreasuryAccount.CreateClearing(
                tenantId, codClearing.Id, "COD Clearing"));
        }
        
        // Wallet Clearing
        if (accountsByCode.TryGetValue("1140", out var walletClearing) &&
            !existingAccountIds.Contains(walletClearing.Id))
        {
            created.Add(TreasuryAccount.CreateClearing(
                tenantId, walletClearing.Id, "Wallet Clearing"));
        }
        
        if (created.Count > 0)
        {
            // Set display order
            for (int i = 0; i < created.Count; i++)
            {
                created[i].DisplayOrder = i;
            }
            
            _db.TreasuryAccounts.AddRange(created);
            await _db.SaveChangesAsync(ct);
            
            _logger.LogInformation("Created {Count} treasury accounts for tenant {TenantId}",
                created.Count, tenantId);
        }
        else
        {
            _logger.LogInformation("No new treasury accounts to create for tenant {TenantId}", tenantId);
        }
    }
}
