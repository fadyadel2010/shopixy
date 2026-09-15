using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Accounting;

public sealed record SupplierCardAuthoritySnapshot(
    Guid FundingInstrumentId,
    Guid FundingLedgerAccountId,
    int FundingInstrumentVersion,
    decimal AvailableBalance,
    string DisplayName);

public static class SupplierCardAuthority
{
    public const string LegacyPosVisaAccountCode = "1020";
    public const string CustomerVisaClearingAccountCode = "1110";

    public static async Task<SupplierCardAuthoritySnapshot> ResolveForOutflowAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid? fundingInstrumentId,
        int? expectedVersion,
        decimal requiredAmount,
        decimal pendingReversalCredit = 0m,
        CancellationToken cancellationToken = default)
    {
        if (!fundingInstrumentId.HasValue || fundingInstrumentId == Guid.Empty)
            throw new InvalidOperationException("FundingInstrumentId is required for supplier Card payments.");
        if (!expectedVersion.HasValue || expectedVersion <= 0)
            throw new InvalidOperationException("FundingInstrumentVersion is required for supplier Card payments.");
        if (requiredAmount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(requiredAmount));

        var instrument = await db.SupplierCardFundingInstruments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == fundingInstrumentId.Value, cancellationToken);

        if (instrument == null || instrument.TenantId != tenantId || instrument.IsDeleted || !instrument.IsActive)
            throw new InvalidOperationException("The selected supplier card funding instrument is unavailable for this tenant.");
        if (instrument.InstrumentType != SupplierCardInstrumentType.FundedCompanyCard)
            throw new InvalidOperationException("Only funded company cards are supported for supplier Card payments.");
        if (instrument.AuthorityVersion != expectedVersion.Value)
            throw new InvalidOperationException("The selected supplier card funding authority changed. Reload and retry.");

        var account = await LockLedgerAccountAsync(db, tenantId, instrument.LedgerAccountId, cancellationToken);
        ValidateLedgerAccount(account);
        await ValidateFundingSourceAsync(db, tenantId, instrument.FundingSourceTreasuryAccountId, cancellationToken);

        var availableBalance = await GetLedgerBalanceAsync(db, tenantId, account.Id, cancellationToken);
        if (availableBalance + pendingReversalCredit < requiredAmount)
            throw new InvalidOperationException(
                $"Insufficient funded card balance. Available={availableBalance:F2}, Required={requiredAmount:F2}.");

        return new SupplierCardAuthoritySnapshot(
            instrument.Id,
            account.Id,
            instrument.AuthorityVersion,
            availableBalance,
            instrument.DisplayName);
    }

    public static async Task ValidateFrozenAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid? fundingInstrumentId,
        Guid? fundingLedgerAccountId,
        int? fundingInstrumentVersion,
        decimal requiredAmount,
        CancellationToken cancellationToken = default)
    {
        if (!fundingInstrumentId.HasValue || fundingInstrumentId == Guid.Empty ||
            !fundingLedgerAccountId.HasValue || fundingLedgerAccountId == Guid.Empty ||
            !fundingInstrumentVersion.HasValue || fundingInstrumentVersion <= 0 ||
            requiredAmount <= 0m)
        {
            throw new InvalidOperationException("Complete frozen supplier card funding authority is required.");
        }

        var instrument = await db.SupplierCardFundingInstruments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == fundingInstrumentId.Value, cancellationToken);
        if (instrument == null || instrument.TenantId != tenantId ||
            instrument.InstrumentType != SupplierCardInstrumentType.FundedCompanyCard ||
            instrument.LedgerAccountId != fundingLedgerAccountId.Value ||
            instrument.AuthorityVersion != fundingInstrumentVersion.Value)
        {
            throw new InvalidOperationException("The persisted supplier card ledger authority does not match the frozen instrument snapshot.");
        }

        var account = await db.LedgerAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == fundingLedgerAccountId.Value && item.TenantId == tenantId, cancellationToken);
        if (account == null || account.AccountType != AccountType.Asset || account.IsClearing ||
            !account.IsSupplierCardFunding || !account.PreventNegative ||
            account.AccountCode is LegacyPosVisaAccountCode or CustomerVisaClearingAccountCode)
        {
            throw new InvalidOperationException("The frozen supplier card ledger account identity is invalid.");
        }
    }

    public static async Task<TreasuryAccount> ValidateFundingSourceAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid fundingSourceTreasuryAccountId,
        CancellationToken cancellationToken = default)
    {
        var source = await db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(item => item.Account)
            .SingleOrDefaultAsync(item => item.Id == fundingSourceTreasuryAccountId, cancellationToken);

        if (source == null || source.TenantId != tenantId || source.IsDeleted || !source.IsActive ||
            source.Type is not (TreasuryAccountType.Cashbox or TreasuryAccountType.Bank) ||
            source.Account == null || source.Account.TenantId != tenantId || source.Account.IsDeleted ||
            !source.Account.IsActive || source.Account.AccountType != AccountType.Asset)
        {
            throw new InvalidOperationException("Funding source must be an active same-tenant Cashbox or Bank authority.");
        }

        return source;
    }

    public static async Task<decimal> GetLedgerBalanceAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid ledgerAccountId,
        CancellationToken cancellationToken = default) =>
        await db.LedgerEntries
            .IgnoreQueryFilters()
            .Where(entry => entry.TenantId == tenantId && entry.AccountId == ledgerAccountId)
            .SumAsync(entry => (decimal?)(entry.DebitAmount - entry.CreditAmount), cancellationToken) ?? 0m;

    public static void ValidateLedgerAccount(Account? account)
    {
        if (account == null || account.IsDeleted || !account.IsActive ||
            account.AccountType != AccountType.Asset || account.IsClearing ||
            !account.IsSupplierCardFunding || !account.PreventNegative ||
            account.AccountCode is LegacyPosVisaAccountCode or CustomerVisaClearingAccountCode)
        {
            throw new InvalidOperationException("The instrument is not linked to an active dedicated supplier card funding Asset account.");
        }
    }

    private static async Task<Account> LockLedgerAccountAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        IQueryable<Account> query;
        var providerName = db.Database.ProviderName ?? string.Empty;
        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            query = db.LedgerAccounts.FromSqlInterpolated($@"
                SELECT xmin, * FROM ""LedgerAccounts""
                WHERE ""TenantId"" = {tenantId} AND ""Id"" = {accountId}
                FOR UPDATE").IgnoreQueryFilters();
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            query = db.LedgerAccounts.FromSqlInterpolated($@"
                SELECT * FROM [LedgerAccounts] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [TenantId] = {tenantId} AND [Id] = {accountId}").IgnoreQueryFilters();
        }
        else
        {
            query = db.LedgerAccounts.IgnoreQueryFilters()
                .Where(account => account.TenantId == tenantId && account.Id == accountId);
        }

        return await query.SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Supplier card funding ledger account was not found in tenant scope.");
    }
}
