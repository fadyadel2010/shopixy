using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Treasury;

/// <summary>
/// Creates and resolves dedicated ledger accounts for cashboxes.
/// </summary>
public sealed class CashboxLedgerAccountService : ICashboxLedgerAccountService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<CashboxLedgerAccountService> _logger;

    public CashboxLedgerAccountService(
        ApplicationDbContext db,
        ILogger<CashboxLedgerAccountService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<CashboxLedgerAccountInfo> EnsureCashboxAccountAsync(
        Cashbox cashbox,
        string? warehouseName = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cashbox);

        if (cashbox.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException($"Cashbox {cashbox.Id} is missing TenantId.");
        }

        var referenceCode = cashbox.Id.ToString("N");
        
        // First check without transaction for the common case (account already exists)
        var existingTreasury = await LoadDedicatedTreasuryAccountAsync(cashbox.TenantId, referenceCode, ct);
        if (existingTreasury != null)
        {
            var updated = SyncCashboxMetadata(existingTreasury, cashbox, warehouseName);
            if (updated)
            {
                await _db.SaveChangesAsync(ct);
            }

            return Map(existingTreasury, cashbox);
        }

        // Account doesn't exist - use transaction with retry to handle race conditions
        const int maxRetries = 2;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var ownsTransaction = _db.Database.CurrentTransaction == null;
            await using var transaction = ownsTransaction
                ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct)
                : null;

            Account? dedicatedAccount = null;
            TreasuryAccount? treasuryAccount = null;
            TreasuryAccount? genericCashboxTreasury = null;

            try
            {
                // Re-check inside transaction to handle race condition
                existingTreasury = await LoadDedicatedTreasuryAccountAsync(cashbox.TenantId, referenceCode, ct);
                if (existingTreasury != null)
                {
                    var updated = SyncCashboxMetadata(existingTreasury, cashbox, warehouseName);
                    if (updated)
                    {
                        await _db.SaveChangesAsync(ct);
                    }

                    if (ownsTransaction && transaction != null)
                    {
                        await transaction.CommitAsync(ct);
                    }

                    return Map(existingTreasury, cashbox);
                }

                // Still doesn't exist - create it
                var baseCashAccount = await _db.LedgerAccounts
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        account => account.TenantId == cashbox.TenantId && account.AccountCode == AccountCodes.CashboxMain,
                        ct)
                    ?? throw new InvalidOperationException(
                        $"Base cashbox ledger account '{AccountCodes.CashboxMain}' was not found for tenant {cashbox.TenantId}.");

                var dedicatedCode = BuildCashboxAccountCode(cashbox.Id);
                var dedicatedName = BuildAccountName(cashbox.Name);

                dedicatedAccount = await _db.LedgerAccounts
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        account => account.TenantId == cashbox.TenantId && account.AccountCode == dedicatedCode,
                        ct);

                if (dedicatedAccount == null)
                {
                    dedicatedAccount = Account.Create(
                        tenantId: cashbox.TenantId,
                        accountCode: dedicatedCode,
                        name: dedicatedName,
                        accountType: baseCashAccount.AccountType,
                        parentAccountId: baseCashAccount.Id,
                        isSystem: true,
                        preventNegative: true);

                    dedicatedAccount.Description = BuildAccountDescription(cashbox.Name);
                    dedicatedAccount.NameAr = baseCashAccount.NameAr;
                    _db.LedgerAccounts.Add(dedicatedAccount);
                }
                else
                {
                    dedicatedAccount.Name = dedicatedName;
                    dedicatedAccount.NameAr = baseCashAccount.NameAr;
                    dedicatedAccount.Description = BuildAccountDescription(cashbox.Name);
                    dedicatedAccount.ParentAccountId = baseCashAccount.Id;
                    dedicatedAccount.IsSystem = true;
                    dedicatedAccount.PreventNegative = true;
                    dedicatedAccount.IsActive = true;
                }

                var hasOtherDedicatedCashboxes = await _db.TreasuryAccounts
                    .IgnoreQueryFilters()
                    .AnyAsync(
                        account => account.TenantId == cashbox.TenantId &&
                                   account.Type == TreasuryAccountType.Cashbox &&
                                   account.IsActive &&
                                   !string.IsNullOrWhiteSpace(account.ReferenceCode) &&
                                   account.ReferenceCode != referenceCode,
                        ct);

                var displayOrder = await _db.TreasuryAccounts
                    .IgnoreQueryFilters()
                    .Where(account => account.TenantId == cashbox.TenantId && account.Type == TreasuryAccountType.Cashbox)
                    .Select(account => (int?)account.DisplayOrder)
                    .MaxAsync(ct) ?? 0;

                treasuryAccount = TreasuryAccount.CreateCashbox(
                    cashbox.TenantId,
                    dedicatedAccount.Id,
                    Truncate(dedicatedName, 100),
                    location: BuildLocation(cashbox, warehouseName),
                    isDefault: !hasOtherDedicatedCashboxes);

                treasuryAccount.Account = dedicatedAccount;
                treasuryAccount.ReferenceCode = referenceCode;
                treasuryAccount.NameAr = baseCashAccount.NameAr;
                treasuryAccount.DisplayOrder = displayOrder + 1;
                _db.TreasuryAccounts.Add(treasuryAccount);

                if (!hasOtherDedicatedCashboxes)
                {
                    genericCashboxTreasury = await _db.TreasuryAccounts
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(
                            account => account.TenantId == cashbox.TenantId &&
                                       account.Type == TreasuryAccountType.Cashbox &&
                                       string.IsNullOrWhiteSpace(account.ReferenceCode),
                            ct);

                    if (genericCashboxTreasury != null)
                    {
                        genericCashboxTreasury.IsDefault = false;
                    }
                }

                await _db.SaveChangesAsync(ct);

                if (ownsTransaction && transaction != null)
                {
                    await transaction.CommitAsync(ct);
                }

                _logger.LogInformation(
                    "Provisioned dedicated cashbox ledger account {AccountCode} for cashbox {CashboxId} in tenant {TenantId}",
                    dedicatedCode,
                    cashbox.Id,
                    cashbox.TenantId);

                return Map(treasuryAccount, cashbox);
            }
            catch (DbUpdateException ex) when (attempt < maxRetries - 1)
            {
                // If we're in an ambient transaction, don't retry - let the outer transaction handle it
                if (!ownsTransaction)
                {
                    _logger.LogWarning(
                        ex,
                        "DbUpdateException in ambient transaction for cashbox {CashboxId}, tenant {TenantId}. Re-throwing to outer transaction handler.",
                        cashbox.Id,
                        cashbox.TenantId);
                    throw;
                }

                // We own the transaction - rollback and retry
                if (transaction != null)
                {
                    await transaction.RollbackAsync(ct);
                }

                // Detach only the entities created in this attempt to avoid dropping unrelated pending writes
                if (dedicatedAccount != null)
                {
                    _db.Entry(dedicatedAccount).State = EntityState.Detached;
                }
                if (treasuryAccount != null)
                {
                    _db.Entry(treasuryAccount).State = EntityState.Detached;
                }
                if (genericCashboxTreasury != null && _db.Entry(genericCashboxTreasury).State == EntityState.Modified)
                {
                    _db.Entry(genericCashboxTreasury).State = EntityState.Detached;
                }

                _logger.LogWarning(
                    ex,
                    "Concurrent cashbox account creation detected for cashbox {CashboxId}, tenant {TenantId}. Retrying (attempt {Attempt}/{MaxRetries})...",
                    cashbox.Id,
                    cashbox.TenantId,
                    attempt + 1,
                    maxRetries);

                // Brief delay before retry to reduce contention
                await Task.Delay(50 * (attempt + 1), ct);
                continue;
            }
            catch
            {
                if (ownsTransaction && transaction != null)
                {
                    await transaction.RollbackAsync(ct);
                }
                throw;
            }
        }

        // Final attempt after retries - load the account that was created by concurrent request
        existingTreasury = await LoadDedicatedTreasuryAccountAsync(cashbox.TenantId, referenceCode, ct);
        if (existingTreasury != null)
        {
            _logger.LogInformation(
                "Loaded existing cashbox account after retry for cashbox {CashboxId}, tenant {TenantId}",
                cashbox.Id,
                cashbox.TenantId);

            return Map(existingTreasury, cashbox);
        }

        throw new InvalidOperationException(
            $"Failed to create or load dedicated cashbox account for cashbox {cashbox.Id} after {maxRetries} attempts.");
    }

    public async Task<CashboxLedgerAccountInfo> ResolveCashboxAccountAsync(
        Guid tenantId,
        Guid cashboxId,
        CancellationToken ct = default)
    {
        var referenceCode = cashboxId.ToString("N");
        var existingTreasury = await LoadDedicatedTreasuryAccountAsync(tenantId, referenceCode, ct);
        if (existingTreasury != null)
        {
            var cashbox = await LoadCashboxAsync(tenantId, cashboxId, ct);
            return Map(existingTreasury, cashbox);
        }

        var cashboxEntity = await LoadCashboxAsync(tenantId, cashboxId, ct)
            ?? throw new InvalidOperationException($"Cashbox {cashboxId} was not found for tenant {tenantId}.");

        var warehouseName = await ResolveWarehouseNameAsync(cashboxEntity.WarehouseId, ct);
        return await EnsureCashboxAccountAsync(cashboxEntity, warehouseName, ct);
    }

    public async Task<CashboxLedgerAccountInfo?> GetCashboxAccountIfExistsAsync(
        Guid tenantId,
        Guid cashboxId,
        CancellationToken ct = default)
    {
        var referenceCode = cashboxId.ToString("N");
        var existingTreasury = await LoadDedicatedTreasuryAccountAsync(tenantId, referenceCode, ct);
        if (existingTreasury == null)
        {
            return null;
        }

        var cashbox = await LoadCashboxAsync(tenantId, cashboxId, ct);
        return Map(existingTreasury, cashbox);
    }

    public async Task<CashboxLedgerAccountInfo> ResolveWarehouseCashboxAccountAsync(
        Guid tenantId,
        Guid warehouseId,
        CancellationToken ct = default)
    {
        var cashbox = await _db.Cashboxes
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.WarehouseId == warehouseId && !c.IsDeleted)
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (cashbox == null)
        {
            return await ResolveDefaultCashboxAccountAsync(tenantId, ct);
        }

        var warehouseName = await ResolveWarehouseNameAsync(warehouseId, ct);
        return await EnsureCashboxAccountAsync(cashbox, warehouseName, ct);
    }

    public async Task<CashboxLedgerAccountInfo> ResolveDefaultCashboxAccountAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        var dedicatedDefault = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .Include(account => account.Account)
            .Where(account =>
                account.TenantId == tenantId &&
                account.Type == TreasuryAccountType.Cashbox &&
                account.IsActive &&
                !string.IsNullOrWhiteSpace(account.ReferenceCode))
            .OrderByDescending(account => account.IsDefault)
            .ThenBy(account => account.DisplayOrder)
            .FirstOrDefaultAsync(ct);

        if (dedicatedDefault != null)
        {
            Cashbox? cashbox = null;
            if (Guid.TryParseExact(dedicatedDefault.ReferenceCode, "N", out var cashboxId))
            {
                cashbox = await LoadCashboxAsync(tenantId, cashboxId, ct);
            }

            return Map(dedicatedDefault, cashbox);
        }

        var preferredCashbox = await _db.Cashboxes
            .IgnoreQueryFilters()
            .Where(cashbox => cashbox.TenantId == tenantId && !cashbox.IsDeleted && cashbox.IsActive)
            .OrderBy(cashbox => cashbox.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (preferredCashbox != null)
        {
            var warehouseName = await ResolveWarehouseNameAsync(preferredCashbox.WarehouseId, ct);
            return await EnsureCashboxAccountAsync(preferredCashbox, warehouseName, ct);
        }

        var genericCashbox = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .Include(account => account.Account)
            .Where(account =>
                account.TenantId == tenantId &&
                account.Type == TreasuryAccountType.Cashbox &&
                account.IsActive)
            .OrderByDescending(account => account.IsDefault)
            .ThenBy(account => account.DisplayOrder)
            .FirstOrDefaultAsync(ct);

        if (genericCashbox != null)
        {
            return Map(genericCashbox, cashbox: null);
        }

        throw new NoCashboxAccountException(tenantId);
    }

    public async Task<IReadOnlyList<CashboxLedgerAccountInfo>> ListCashboxAccountsAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        var cashboxes = await _db.Cashboxes
            .IgnoreQueryFilters()
            .Where(cashbox => cashbox.TenantId == tenantId && !cashbox.IsDeleted)
            .OrderByDescending(cashbox => cashbox.IsActive)
            .ThenBy(cashbox => cashbox.CreatedAt)
            .ToListAsync(ct);

        if (cashboxes.Count > 0)
        {
            var warehouseIds = cashboxes
                .Where(cashbox => cashbox.WarehouseId.HasValue)
                .Select(cashbox => cashbox.WarehouseId!.Value)
                .Distinct()
                .ToList();

            var warehouseNames = warehouseIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await _db.Warehouses
                    .IgnoreQueryFilters()
                    .Where(warehouse => warehouseIds.Contains(warehouse.Id))
                    .ToDictionaryAsync(warehouse => warehouse.Id, warehouse => warehouse.Name, ct);

            // Query existing treasury account mappings for these cashboxes
            var resolvedCashboxIds = cashboxes.Select(c => c.Id).ToList();
            var cashboxReferenceCodes = resolvedCashboxIds.Select(id => id.ToString("N")).ToList();
            
            var existingMappings = await _db.TreasuryAccounts
                .IgnoreQueryFilters()
                .Include(account => account.Account)
                .Where(account =>
                    account.TenantId == tenantId &&
                    account.Type == TreasuryAccountType.Cashbox &&
                    account.ReferenceCode != null &&
                    cashboxReferenceCodes.Contains(account.ReferenceCode))
                .ToDictionaryAsync(
                    account => account.ReferenceCode!,
                    ct);

            var resolvedAccounts = new List<CashboxLedgerAccountInfo>(cashboxes.Count);
            foreach (var cashbox in cashboxes)
            {
                var cashboxRefCode = cashbox.Id.ToString("N");
                if (existingMappings.TryGetValue(cashboxRefCode, out var treasuryAccount))
                {
                    // Use existing mapping
                    resolvedAccounts.Add(Map(treasuryAccount, cashbox));
                }
                else
                {
                    // Cashbox has no treasury account mapping - add placeholder so callers can detect and provision
                    _logger.LogWarning(
                        "Cashbox {CashboxId} (ReferenceCode: {ReferenceCode}) in tenant {TenantId} has no TreasuryAccount mapping. Needs provisioning.",
                        cashbox.Id,
                        cashboxRefCode,
                        tenantId);

                    var warehouseName = cashbox.WarehouseId.HasValue && warehouseNames.TryGetValue(cashbox.WarehouseId.Value, out var name)
                        ? name
                        : null;

                    // Add placeholder with Guid.Empty AccountId to indicate unmapped state
                    resolvedAccounts.Add(new CashboxLedgerAccountInfo(
                        CashboxId: cashbox.Id,
                        AccountId: Guid.Empty, // Sentinel value indicating needs provisioning
                        AccountCode: BuildCashboxAccountCode(cashbox.Id),
                        Name: cashbox.Name,
                        IsDefault: false,
                        WarehouseId: cashbox.WarehouseId,
                        Location: BuildLocation(cashbox, warehouseName)));
                }
            }

            return resolvedAccounts
                .OrderByDescending(account => account.IsDefault)
                .ThenBy(account => account.Location)
                .ThenBy(account => account.Name)
                .ToList();
        }

        var treasuryAccounts = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .Include(account => account.Account)
            .Where(account =>
                account.TenantId == tenantId &&
                account.Type == TreasuryAccountType.Cashbox &&
                account.IsActive)
            .OrderByDescending(account => account.IsDefault)
            .ThenBy(account => account.DisplayOrder)
            .ToListAsync(ct);

        if (treasuryAccounts.Count == 0)
        {
            return Array.Empty<CashboxLedgerAccountInfo>();
        }

        var cashboxIds = treasuryAccounts
            .Where(account => !string.IsNullOrWhiteSpace(account.ReferenceCode))
            .Select(account => Guid.TryParseExact(account.ReferenceCode, "N", out var cashboxId) ? cashboxId : Guid.Empty)
            .Where(cashboxId => cashboxId != Guid.Empty)
            .ToList();

        var cashboxesById = await _db.Cashboxes
            .IgnoreQueryFilters()
            .Where(cashbox => cashbox.TenantId == tenantId && cashboxIds.Contains(cashbox.Id))
            .ToDictionaryAsync(cashbox => cashbox.Id, ct);

        return treasuryAccounts
            .Select(account =>
            {
                Cashbox? cashbox = null;
                if (!string.IsNullOrWhiteSpace(account.ReferenceCode) &&
                    Guid.TryParseExact(account.ReferenceCode, "N", out var cashboxId) &&
                    cashboxesById.TryGetValue(cashboxId, out var foundCashbox))
                {
                    cashbox = foundCashbox;
                }

                return Map(account, cashbox);
            })
            .ToList();
    }

    private async Task<TreasuryAccount?> LoadDedicatedTreasuryAccountAsync(
        Guid tenantId,
        string referenceCode,
        CancellationToken ct)
    {
        return await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .Include(account => account.Account)
            .FirstOrDefaultAsync(
                account => account.TenantId == tenantId &&
                           account.Type == TreasuryAccountType.Cashbox &&
                           account.ReferenceCode == referenceCode &&
                           account.IsActive,
                ct);
    }

    private async Task<Cashbox?> LoadCashboxAsync(Guid tenantId, Guid cashboxId, CancellationToken ct)
    {
        return await _db.Cashboxes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                cashbox => cashbox.TenantId == tenantId &&
                           cashbox.Id == cashboxId &&
                           !cashbox.IsDeleted,
                ct);
    }

    private async Task<string?> ResolveWarehouseNameAsync(Guid? warehouseId, CancellationToken ct)
    {
        if (!warehouseId.HasValue)
        {
            return null;
        }

        return await _db.Warehouses
            .IgnoreQueryFilters()
            .Where(warehouse => warehouse.Id == warehouseId.Value)
            .Select(warehouse => warehouse.Name)
            .FirstOrDefaultAsync(ct);
    }

    private static bool SyncCashboxMetadata(TreasuryAccount treasuryAccount, Cashbox cashbox, string? warehouseName)
    {
        var updated = false;
        var name = BuildAccountName(cashbox.Name);
        var location = BuildLocation(cashbox, warehouseName);

        if (treasuryAccount.Name != Truncate(name, 100))
        {
            treasuryAccount.Name = Truncate(name, 100);
            updated = true;
        }

        if (treasuryAccount.Location != location)
        {
            treasuryAccount.Location = location;
            updated = true;
        }

        if (treasuryAccount.Account != null)
        {
            if (treasuryAccount.Account.Name != name)
            {
                treasuryAccount.Account.Name = name;
                updated = true;
            }

            var description = BuildAccountDescription(cashbox.Name);
            if (treasuryAccount.Account.Description != description)
            {
                treasuryAccount.Account.Description = description;
                updated = true;
            }
        }

        return updated;
    }

    private static CashboxLedgerAccountInfo Map(TreasuryAccount treasuryAccount, Cashbox? cashbox)
    {
        var cashboxId = cashbox?.Id;
        if (!cashboxId.HasValue &&
            !string.IsNullOrWhiteSpace(treasuryAccount.ReferenceCode) &&
            Guid.TryParseExact(treasuryAccount.ReferenceCode, "N", out var parsedCashboxId))
        {
            cashboxId = parsedCashboxId;
        }

        var accountCode = treasuryAccount.Account?.AccountCode;
        if (string.IsNullOrWhiteSpace(accountCode))
        {
            accountCode = cashboxId.HasValue
                ? BuildCashboxAccountCode(cashboxId.Value)
                : throw new InvalidOperationException(
                    $"Treasury account {treasuryAccount.Id} is missing its ledger account mapping.");
        }

        return new CashboxLedgerAccountInfo(
            CashboxId: cashboxId,
            AccountId: treasuryAccount.AccountId,
            AccountCode: accountCode,
            Name: treasuryAccount.Name,
            IsDefault: treasuryAccount.IsDefault,
            WarehouseId: cashbox?.WarehouseId,
            Location: treasuryAccount.Location);
    }

    private static string BuildCashboxAccountCode(Guid cashboxId) =>
        $"{AccountCodes.CashboxMain}-{cashboxId.ToString("N")[24..].ToUpperInvariant()}";

    private static string BuildAccountName(string cashboxName) =>
        Truncate($"Cashbox {cashboxName}".Trim(), 200);

    private static string BuildAccountDescription(string cashboxName) =>
        Truncate($"Dedicated ledger account for cashbox '{cashboxName}'.", 500);

    private static string? BuildLocation(Cashbox cashbox, string? warehouseName)
    {
        if (!string.IsNullOrWhiteSpace(warehouseName))
        {
            return Truncate(warehouseName.Trim(), 100);
        }

        return cashbox.WarehouseId.HasValue ? "Warehouse cashbox" : "System-wide";
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
