using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Identity;

public sealed record CanonicalIdentityReconciliationResult(int RepairedCount, int ConflictCount);

public sealed class CanonicalIdentityReconciliationService
{
    private const int BatchSize = 250;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CanonicalIdentityReconciliationService> _logger;

    public CanonicalIdentityReconciliationService(
        ApplicationDbContext dbContext,
        ILogger<CanonicalIdentityReconciliationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CanonicalIdentityReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var repaired = 0;
        var conflicts = 0;

        repaired += await ReconcileUsersAsync(cancellationToken);
        repaired += await ReconcileCustomersAsync(cancellationToken);
        repaired += await ReconcileTenantsAsync(cancellationToken);
        repaired += await ReconcileOnlineCustomersAsync(cancellationToken);
        repaired += await ReconcileCommerceHubCustomersAsync(cancellationToken);
        repaired += await ReconcileStoreTenantsFromCommerceHubAsync(cancellationToken);

        var affiliateResult = await ReconcileAffiliatesAsync(cancellationToken);
        repaired += affiliateResult.RepairedCount;
        conflicts += affiliateResult.ConflictCount;

        var commerceHubBuyerResult = await ReconcileCommerceHubBuyerLinksAsync(cancellationToken);
        repaired += commerceHubBuyerResult.RepairedCount;
        conflicts += commerceHubBuyerResult.ConflictCount;

        if (repaired > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (conflicts > 0)
        {
            _logger.LogCritical(
                "Canonical identity reconciliation found {ConflictCount} cross-system identity conflicts. Manual authority resolution is required.",
                conflicts);
        }

        return new CanonicalIdentityReconciliationResult(repaired, conflicts);
    }

    public async Task<CanonicalIdentityReconciliationResult> InspectAsync(CancellationToken cancellationToken = default)
    {
        var missingCount =
            await CountMissingUsersAsync(cancellationToken) +
            await CountMissingCustomersAsync(cancellationToken) +
            await CountMissingTenantsAsync(cancellationToken) +
            await CountMissingOnlineCustomersAsync(cancellationToken) +
            await CountMissingCommerceHubCustomersAsync(cancellationToken) +
            await CountMissingAffiliateAccountsAsync(cancellationToken) +
            await CountMissingAffiliateUsersAsync(cancellationToken);

        var conflictCount = await CountCommerceHubBuyerConflictsAsync(cancellationToken);

        return new CanonicalIdentityReconciliationResult(missingCount, conflictCount);
    }

    private async Task<int> ReconcileUsersAsync(CancellationToken cancellationToken)
    {
        var subjects = await _dbContext.Users
            .IgnoreQueryFilters()
            .Where(user => !user.IsDeleted)
            .Where(user => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.PosUser &&
                link.SubjectId == user.Id &&
                link.TenantId == user.TenantId))
            .OrderBy(user => user.Id)
            .Take(BatchSize)
            .Select(user => new { user.Id, TenantId = (Guid?)user.TenantId, user.Email })
            .ToListAsync(cancellationToken);

        foreach (var subject in subjects)
        {
            await EnsureLinkAsync(subject.Id, CanonicalIdentitySubjectType.PosUser, subject.Id, subject.TenantId, "POS", subject.Email, cancellationToken);
        }

        return subjects.Count;
    }

    private async Task<int> ReconcileCustomersAsync(CancellationToken cancellationToken)
    {
        var subjects = await _dbContext.Customers
            .IgnoreQueryFilters()
            .Where(customer => !customer.IsDeleted)
            .Where(customer => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.PosCustomer &&
                link.SubjectId == customer.Id &&
                link.TenantId == customer.TenantId))
            .OrderBy(customer => customer.Id)
            .Take(BatchSize)
            .Select(customer => new { customer.Id, TenantId = (Guid?)customer.TenantId, customer.Email })
            .ToListAsync(cancellationToken);

        foreach (var subject in subjects)
        {
            await EnsureLinkAsync(subject.Id, CanonicalIdentitySubjectType.PosCustomer, subject.Id, subject.TenantId, "POS", subject.Email, cancellationToken);
        }

        return subjects.Count;
    }

    private async Task<int> ReconcileTenantsAsync(CancellationToken cancellationToken)
    {
        var subjects = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .Where(tenant => tenant.DeletedAtUtc == null)
            .Where(tenant => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.StoreTenant &&
                link.SubjectId == tenant.Id &&
                link.TenantId == tenant.Id))
            .OrderBy(tenant => tenant.Id)
            .Take(BatchSize)
            .Select(tenant => new
            {
                tenant.Id,
                DisplayKey = tenant.Slug ?? tenant.ContactEmail ?? tenant.Name
            })
            .ToListAsync(cancellationToken);

        foreach (var subject in subjects)
        {
            await EnsureLinkAsync(subject.Id, CanonicalIdentitySubjectType.StoreTenant, subject.Id, subject.Id, "POS", subject.DisplayKey, cancellationToken);
        }

        return subjects.Count;
    }

    private async Task<int> ReconcileOnlineCustomersAsync(CancellationToken cancellationToken)
    {
        var subjects = await _dbContext.OnlineCustomers
            .IgnoreQueryFilters()
            .Where(customer => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.OnlineCustomer &&
                link.SubjectId == customer.Id &&
                link.TenantId == customer.TenantId))
            .OrderBy(customer => customer.Id)
            .Take(BatchSize)
            .Select(customer => new { customer.Id, TenantId = (Guid?)customer.TenantId, customer.Email })
            .ToListAsync(cancellationToken);

        foreach (var subject in subjects)
        {
            await EnsureLinkAsync(subject.Id, CanonicalIdentitySubjectType.OnlineCustomer, subject.Id, subject.TenantId, "Storefront", subject.Email, cancellationToken);
        }

        return subjects.Count;
    }

    private async Task<int> ReconcileCommerceHubCustomersAsync(CancellationToken cancellationToken)
    {
        var subjects = await _dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .Where(customer => !customer.IsDeleted)
            .Where(customer => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.CommerceHubCustomer &&
                link.SubjectId == customer.Id &&
                link.TenantId == null))
            .OrderBy(customer => customer.Id)
            .Take(BatchSize)
            .Select(customer => new { customer.Id, customer.Email })
            .ToListAsync(cancellationToken);

        foreach (var subject in subjects)
        {
            await EnsureLinkAsync(subject.Id, CanonicalIdentitySubjectType.CommerceHubCustomer, subject.Id, null, "CommerceHub", subject.Email, cancellationToken);
        }

        return subjects.Count;
    }

    private async Task<int> ReconcileStoreTenantsFromCommerceHubAsync(CancellationToken cancellationToken)
    {
        var storeSubjects = await _dbContext.CommerceHubSubOrders
            .IgnoreQueryFilters()
            .Where(subOrder => subOrder.StoreId != Guid.Empty)
            .Where(subOrder => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.StoreTenant &&
                link.SubjectId == subOrder.StoreId &&
                link.TenantId == subOrder.StoreId))
            .GroupBy(subOrder => new { subOrder.StoreId, subOrder.StoreName })
            .OrderBy(group => group.Key.StoreId)
            .Take(BatchSize)
            .Select(group => new { group.Key.StoreId, group.Key.StoreName })
            .ToListAsync(cancellationToken);

        foreach (var subject in storeSubjects)
        {
            await EnsureLinkAsync(subject.StoreId, CanonicalIdentitySubjectType.StoreTenant, subject.StoreId, subject.StoreId, "CommerceHub", subject.StoreName, cancellationToken);
        }

        return storeSubjects.Count;
    }

    private async Task<CanonicalIdentityReconciliationResult> ReconcileAffiliatesAsync(CancellationToken cancellationToken)
    {
        var repaired = 0;
        var conflicts = 0;

        var accounts = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(account => !account.IsDeleted)
            .Where(account => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.AffiliateAccount &&
                link.SubjectId == account.Id &&
                link.TenantId == (account.TenantId == Guid.Empty ? null : account.TenantId)))
            .OrderBy(account => account.Id)
            .Take(BatchSize)
            .Select(account => new { account.Id, TenantId = (Guid?)account.TenantId, account.UserId, account.ReferralCode, account.DisplayName })
            .ToListAsync(cancellationToken);

        foreach (var account in accounts)
        {
            var tenantId = NormalizeIdentityScope(account.TenantId);
            Guid? preferredCanonicalId = null;
            if (account.UserId != Guid.Empty)
            {
                preferredCanonicalId = await EnsureLinkAsync(
                    account.UserId,
                    CanonicalIdentitySubjectType.PosUser,
                    account.UserId,
                    tenantId,
                    "POS",
                    account.DisplayName,
                    cancellationToken);
            }

            var result = await EnsureLinkAsync(
                preferredCanonicalId ?? Guid.NewGuid(),
                CanonicalIdentitySubjectType.AffiliateAccount,
                account.Id,
                tenantId,
                "Affiliate",
                account.ReferralCode,
                cancellationToken,
                preferredCanonicalId);

            if (result == null)
            {
                conflicts++;
            }
            else
            {
                repaired++;
            }
        }

        var users = await _dbContext.AffiliateUsers
            .IgnoreQueryFilters()
            .Where(user => !user.IsDeleted)
            .Where(user => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.AffiliateUser &&
                link.SubjectId == user.Id &&
                link.TenantId == (user.TenantId == Guid.Empty ? null : user.TenantId)))
            .OrderBy(user => user.Id)
            .Take(BatchSize)
            .Select(user => new { user.Id, TenantId = (Guid?)user.TenantId, user.Email, user.AffiliateAccountId })
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            var tenantId = NormalizeIdentityScope(user.TenantId);
            var affiliateCanonicalId = await EnsureLinkAsync(
                Guid.NewGuid(),
                CanonicalIdentitySubjectType.AffiliateAccount,
                user.AffiliateAccountId,
                tenantId,
                "Affiliate",
                user.Email,
                cancellationToken);

            if (affiliateCanonicalId == null)
            {
                // Affiliate account link conflict - skip user link attempt
                conflicts++;
            }
            else
            {
                // Affiliate account linked successfully - now link the user
                var result = await EnsureLinkAsync(
                    affiliateCanonicalId.Value,
                    CanonicalIdentitySubjectType.AffiliateUser,
                    user.Id,
                    tenantId,
                    "Affiliate",
                    user.Email,
                    cancellationToken,
                    affiliateCanonicalId);

                if (result == null)
                {
                    conflicts++;
                }
                else
                {
                    repaired++;
                }
            }
        }

        return new CanonicalIdentityReconciliationResult(repaired, conflicts);
    }

    private async Task<CanonicalIdentityReconciliationResult> ReconcileCommerceHubBuyerLinksAsync(CancellationToken cancellationToken)
    {
        var repaired = 0;
        var conflicts = 0;

        var buyers = await _dbContext.CommerceHubMarketplaceOrders
            .IgnoreQueryFilters()
            .Where(order => !order.IsDeleted)
            .Where(order => order.CustomerId != Guid.Empty && order.OnlineCustomerId.HasValue && order.OnlineCustomerId.Value != Guid.Empty)
            .OrderBy(order => order.Id)
            .Take(BatchSize)
            .Select(order => new { order.CustomerId, OnlineCustomerId = order.OnlineCustomerId!.Value, order.FullName })
            .ToListAsync(cancellationToken);

        var onlineCustomerIds = buyers.Select(buyer => buyer.OnlineCustomerId).Distinct().ToList();
        var onlineTenants = await _dbContext.OnlineCustomers
            .IgnoreQueryFilters()
            .Where(customer => onlineCustomerIds.Contains(customer.Id))
            .Select(customer => new { customer.Id, TenantId = (Guid?)customer.TenantId })
            .ToDictionaryAsync(customer => customer.Id, customer => customer.TenantId, cancellationToken);

        foreach (var buyer in buyers)
        {
            onlineTenants.TryGetValue(buyer.OnlineCustomerId, out var onlineTenantId);
            var commerceHubCanonicalId = await TryResolveCanonicalIdentityAsync(
                CanonicalIdentitySubjectType.CommerceHubCustomer,
                buyer.CustomerId,
                null,
                cancellationToken);
            var onlineCanonicalId = await TryResolveCanonicalIdentityAsync(
                CanonicalIdentitySubjectType.OnlineCustomer,
                buyer.OnlineCustomerId,
                onlineTenantId,
                cancellationToken);

            if (commerceHubCanonicalId.HasValue &&
                onlineCanonicalId.HasValue &&
                commerceHubCanonicalId.Value != onlineCanonicalId.Value)
            {
                conflicts++;
                _logger.LogCritical(
                    "Canonical identity conflict for CommerceHub customer {CommerceHubCustomerId} and online customer {OnlineCustomerId}.",
                    buyer.CustomerId,
                    buyer.OnlineCustomerId);
                continue;
            }

            var canonicalId = commerceHubCanonicalId ?? onlineCanonicalId ?? Guid.NewGuid();
            var commerceHubResult = await EnsureLinkAsync(
                canonicalId,
                CanonicalIdentitySubjectType.CommerceHubCustomer,
                buyer.CustomerId,
                null,
                "CommerceHub",
                buyer.FullName,
                cancellationToken,
                canonicalId);
            var onlineResult = await EnsureLinkAsync(
                canonicalId,
                CanonicalIdentitySubjectType.OnlineCustomer,
                buyer.OnlineCustomerId,
                onlineTenantId,
                "Storefront",
                buyer.FullName,
                cancellationToken,
                canonicalId);

            if (commerceHubResult == null || onlineResult == null)
            {
                conflicts++;
                continue;
            }

            if (!commerceHubCanonicalId.HasValue)
            {
                repaired++;
            }

            if (!onlineCanonicalId.HasValue)
            {
                repaired++;
            }
        }

        return new CanonicalIdentityReconciliationResult(repaired, conflicts);
    }

    private async Task<Guid?> EnsureLinkAsync(
        Guid canonicalIdentityId,
        CanonicalIdentitySubjectType subjectType,
        Guid subjectId,
        Guid? tenantId,
        string sourceSystem,
        string? displayKey,
        CancellationToken cancellationToken,
        Guid? preferredCanonicalIdentityId = null)
    {
        var normalizedTenantId = NormalizeIdentityScope(tenantId);
        var trackedLink = _dbContext.ChangeTracker.Entries<CanonicalIdentityLink>()
            .Where(entry => entry.State != EntityState.Deleted)
            .Select(entry => entry.Entity)
            .FirstOrDefault(link =>
                link.IsActive &&
                link.SubjectType == subjectType &&
                link.SubjectId == subjectId &&
                link.TenantId == normalizedTenantId);

        if (trackedLink != null)
        {
            if (preferredCanonicalIdentityId.HasValue &&
                trackedLink.CanonicalIdentityId != preferredCanonicalIdentityId.Value)
            {
                _logger.LogCritical(
                    "Canonical identity conflict for {SubjectType} subject {SubjectId}. Planned canonical identity {PlannedCanonicalIdentityId} does not match required canonical identity {RequiredCanonicalIdentityId}.",
                    subjectType,
                    subjectId,
                    trackedLink.CanonicalIdentityId,
                    preferredCanonicalIdentityId.Value);
                return null;
            }

            return trackedLink.CanonicalIdentityId;
        }

        var existingCanonicalId = await TryResolveCanonicalIdentityAsync(subjectType, subjectId, normalizedTenantId, cancellationToken);

        if (existingCanonicalId.HasValue)
        {
            if (preferredCanonicalIdentityId.HasValue && existingCanonicalId.Value != preferredCanonicalIdentityId.Value)
            {
                _logger.LogCritical(
                    "Canonical identity conflict for {SubjectType} subject {SubjectId}. Existing canonical identity {ExistingCanonicalIdentityId} does not match required canonical identity {RequiredCanonicalIdentityId}.",
                    subjectType,
                    subjectId,
                    existingCanonicalId.Value,
                    preferredCanonicalIdentityId.Value);
                return null;
            }

            return existingCanonicalId.Value;
        }

        var canonicalId = preferredCanonicalIdentityId ?? canonicalIdentityId;
        _dbContext.CanonicalIdentityLinks.Add(CanonicalIdentityLink.Create(
            canonicalId,
            subjectType,
            subjectId,
            normalizedTenantId,
            sourceSystem,
            displayKey));

        return canonicalId;
    }

    private async Task<Guid?> TryResolveCanonicalIdentityAsync(
        CanonicalIdentitySubjectType subjectType,
        Guid subjectId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var normalizedTenantId = NormalizeIdentityScope(tenantId);
        return await _dbContext.CanonicalIdentityLinks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == subjectType &&
                link.SubjectId == subjectId &&
                link.TenantId == normalizedTenantId)
            .Select(link => (Guid?)link.CanonicalIdentityId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<int> CountMissingUsersAsync(CancellationToken cancellationToken) =>
        _dbContext.Users
            .IgnoreQueryFilters()
            .Where(user => !user.IsDeleted)
            .CountAsync(user => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.PosUser &&
                link.SubjectId == user.Id &&
                link.TenantId == user.TenantId), cancellationToken);

    private Task<int> CountMissingCustomersAsync(CancellationToken cancellationToken) =>
        _dbContext.Customers
            .IgnoreQueryFilters()
            .Where(customer => !customer.IsDeleted)
            .CountAsync(customer => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.PosCustomer &&
                link.SubjectId == customer.Id &&
                link.TenantId == customer.TenantId), cancellationToken);

    private Task<int> CountMissingTenantsAsync(CancellationToken cancellationToken) =>
        _dbContext.Tenants
            .IgnoreQueryFilters()
            .Where(tenant => tenant.DeletedAtUtc == null)
            .CountAsync(tenant => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.StoreTenant &&
                link.SubjectId == tenant.Id &&
                link.TenantId == tenant.Id), cancellationToken);

    private Task<int> CountMissingOnlineCustomersAsync(CancellationToken cancellationToken) =>
        _dbContext.OnlineCustomers
            .IgnoreQueryFilters()
            .CountAsync(customer => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.OnlineCustomer &&
                link.SubjectId == customer.Id &&
                link.TenantId == customer.TenantId), cancellationToken);

    private Task<int> CountMissingCommerceHubCustomersAsync(CancellationToken cancellationToken) =>
        _dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .Where(customer => !customer.IsDeleted)
            .CountAsync(customer => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.CommerceHubCustomer &&
                link.SubjectId == customer.Id &&
                link.TenantId == null), cancellationToken);

    private Task<int> CountMissingAffiliateAccountsAsync(CancellationToken cancellationToken) =>
        _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(account => !account.IsDeleted)
            .CountAsync(account => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.AffiliateAccount &&
                link.SubjectId == account.Id &&
                link.TenantId == (account.TenantId == Guid.Empty ? null : account.TenantId)), cancellationToken);

    private Task<int> CountMissingAffiliateUsersAsync(CancellationToken cancellationToken) =>
        _dbContext.AffiliateUsers
            .IgnoreQueryFilters()
            .Where(user => !user.IsDeleted)
            .CountAsync(user => !_dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Any(link =>
                !link.IsDeleted &&
                link.RevokedAtUtc == null &&
                link.SubjectType == CanonicalIdentitySubjectType.AffiliateUser &&
                link.SubjectId == user.Id &&
                link.TenantId == (user.TenantId == Guid.Empty ? null : user.TenantId)), cancellationToken);

    private Task<int> CountCommerceHubBuyerConflictsAsync(CancellationToken cancellationToken) =>
        _dbContext.CommerceHubMarketplaceOrders
            .IgnoreQueryFilters()
            .Where(order => !order.IsDeleted)
            .Where(order => order.CustomerId != Guid.Empty && order.OnlineCustomerId.HasValue && order.OnlineCustomerId.Value != Guid.Empty)
            .Join(
                _dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Where(link =>
                    !link.IsDeleted &&
                    link.RevokedAtUtc == null &&
                    link.SubjectType == CanonicalIdentitySubjectType.CommerceHubCustomer &&
                    link.TenantId == null),
                order => order.CustomerId,
                link => link.SubjectId,
                (order, commerceHubLink) => new { order.OnlineCustomerId, commerceHubLink.CanonicalIdentityId })
            .Join(
                _dbContext.CanonicalIdentityLinks.IgnoreQueryFilters().Where(link =>
                    !link.IsDeleted &&
                    link.RevokedAtUtc == null &&
                    link.SubjectType == CanonicalIdentitySubjectType.OnlineCustomer),
                left => left.OnlineCustomerId!.Value,
                onlineLink => onlineLink.SubjectId,
                (left, onlineLink) => new { left.CanonicalIdentityId, OnlineCanonicalIdentityId = onlineLink.CanonicalIdentityId })
            .CountAsync(pair => pair.CanonicalIdentityId != pair.OnlineCanonicalIdentityId, cancellationToken);

    private static Guid? NormalizeIdentityScope(Guid? tenantId) =>
        tenantId.HasValue && tenantId.Value != Guid.Empty ? tenantId : null;
}

public sealed class CanonicalIdentityReconciliationHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CanonicalIdentityReconciliationHostedService> _logger;

    public CanonicalIdentityReconciliationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<CanonicalIdentityReconciliationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay startup to avoid hitting the database during cold start
        try
        {
            _logger.LogInformation(
                "Canonical identity reconciliation service delaying startup for {StartupDelay}.",
                StartupDelay);
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Canonical identity reconciliation service startup cancelled.");
            return;
        }

        _logger.LogInformation("Canonical identity reconciliation service starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<CanonicalIdentityReconciliationService>();
                var result = await reconciler.ReconcileAsync(stoppingToken);

                if (result.RepairedCount > 0 || result.ConflictCount > 0)
                {
                    _logger.LogWarning(
                        "Canonical identity reconciliation completed with {RepairedCount} repaired links and {ConflictCount} conflicts.",
                        result.RepairedCount,
                        result.ConflictCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Canonical identity reconciliation failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
