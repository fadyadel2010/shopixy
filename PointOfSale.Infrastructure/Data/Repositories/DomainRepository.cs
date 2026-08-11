using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDomainRepository"/> for Domain
/// persistence (DOMAIN-2C + DOMAIN-3.1-C write path).
///
/// This repository is the PERSISTENCE boundary for the Host Resolution
/// Middleware pipeline. It returns raw persistence models (Domain entities)
/// — it NEVER resolves Tenant, StoreConfig, Theme, or CMS.
///
/// Resolution pipeline (DOMAIN-2C):
///   NormalizedHost → IDomainRepository → Domain Entity → STOP
/// </summary>
public sealed class DomainRepository : IDomainRepository
{
    private readonly ApplicationDbContext _dbContext;

    public DomainRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    /// Uses UQ_Domains_NormalizedHostName — single B-tree index seek.
    /// No joins, no scans. < 5ms target (DOMAIN-1.5 Section 11.1).
    public async Task<Domain?> FindByNormalizedHostAsync(
        string normalizedHostName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedHostName))
        {
            return null;
        }

        return await _dbContext.Domains
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                d => !d.IsDeleted &&
                     d.NormalizedHostName == normalizedHostName,
                cancellationToken);
    }

    /// <inheritdoc />
    /// Uses IX_Domains_TenantId + filtered unique index for Primary state.
    /// Returns the single Primary domain for the tenant, or null if none.
    public async Task<Domain?> FindPrimaryDomainAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return null;
        }

        return await _dbContext.Domains
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                d => !d.IsDeleted &&
                     d.TenantId == tenantId &&
                     d.DomainType == DomainType.Primary &&
                     d.LifecycleState == DomainLifecycleState.Primary,
                cancellationToken);
    }

    /// <inheritdoc />
    /// Uses IX_Domains_TenantId_LifecycleState composite index.
    /// Returns all alias domains for the tenant (DomainType = Alias).
    public async Task<IReadOnlyList<Domain>> FindAliasDomainsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return Array.Empty<Domain>();
        }

        return await _dbContext.Domains
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => !d.IsDeleted &&
                        d.TenantId == tenantId &&
                        d.DomainType == DomainType.Alias)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// Uses IX_Domains_RedirectTargetDomainId — FK reverse lookup.
    /// Two-step: lookup source domain by ID, then resolve redirect target.
    public async Task<Domain?> FindRedirectTargetAsync(
        Guid domainId,
        CancellationToken cancellationToken = default)
    {
        if (domainId == Guid.Empty)
        {
            return null;
        }

        // Step 1: Find the source domain and its redirect target ID
        var redirectTargetId = await _dbContext.Domains
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => !d.IsDeleted && d.Id == domainId)
            .Select(d => d.RedirectTargetDomainId)
            .SingleOrDefaultAsync(cancellationToken);

        // No redirect configured
        if (redirectTargetId == null)
        {
            return null;
        }

        // Step 2: Resolve the target domain
        return await _dbContext.Domains
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(d => !d.IsDeleted && d.Id == redirectTargetId, cancellationToken);
    }

    // =========================================================================
    // DOMAIN-3.1-C: Write-path repository methods
    // =========================================================================

    /// <inheritdoc />
    public async Task AddAsync(Domain domain, CancellationToken cancellationToken = default)
    {
        await _dbContext.Domains.AddAsync(domain, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Domain?> FindByIdAsync(
        Guid tenantId,
        Guid domainId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Domains
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                d => !d.IsDeleted &&
                     d.TenantId == tenantId &&
                     d.Id == domainId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Domain?> FindByIdIncludingDeletedAsync(
        Guid tenantId,
        Guid domainId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Domains
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                d => d.TenantId == tenantId &&
                     d.Id == domainId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByNormalizedHostAsync(
        string normalizedHostName,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Domains
            .IgnoreQueryFilters()
            .AnyAsync(
                d => d.NormalizedHostName == normalizedHostName,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByTenantAndNormalizedHostAsync(
        Guid tenantId,
        string normalizedHostName,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Domains
            .IgnoreQueryFilters()
            .AnyAsync(
                d => d.TenantId == tenantId &&
                     d.NormalizedHostName == normalizedHostName,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> HasRedirectDependentsAsync(
        Guid domainId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Domains
            .IgnoreQueryFilters()
            .AnyAsync(
                d => !d.IsDeleted &&
                     d.RedirectTargetDomainId == domainId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAuditEntryAsync(
        DomainAuditEntry auditEntry,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.DomainAuditEntries.AddAsync(auditEntry, cancellationToken);
    }
}
