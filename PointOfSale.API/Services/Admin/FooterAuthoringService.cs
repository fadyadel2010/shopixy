using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Admin;

public enum FooterAuthoringScope
{
    Tenant,
    Global,
    Marketplace
}

public interface IFooterAuthoringService
{
    Task<AdminStorefrontFooterConfigurationResponse> GetConfigurationAsync(
        FooterAuthoringScope scope,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default);

    Task<AdminStorefrontFooterConfigurationResponse> SaveConfigurationAsync(
        FooterAuthoringScope scope,
        Guid? tenantId,
        AdminStorefrontFooterConfigurationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class FooterAuthoringService : IFooterAuthoringService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly FooterAuthoringValidator _validator;
    private readonly FooterAuthoringMapper _mapper;

    public FooterAuthoringService(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        FooterAuthoringValidator validator,
        FooterAuthoringMapper mapper)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _validator = validator;
        _mapper = mapper;
    }

    public async Task<AdminStorefrontFooterConfigurationResponse> GetConfigurationAsync(
        FooterAuthoringScope scope,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope, tenantId);
        return scope == FooterAuthoringScope.Marketplace
            ? await LoadMarketplaceResponseAsync(asTracking: false, cancellationToken)
            : await LoadStorefrontResponseAsync(scope, tenantId, asTracking: false, cancellationToken);
    }

    public async Task<AdminStorefrontFooterConfigurationResponse> SaveConfigurationAsync(
        FooterAuthoringScope scope,
        Guid? tenantId,
        AdminStorefrontFooterConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateScope(scope, tenantId);

        if (string.IsNullOrWhiteSpace(request.ExpectedRevision))
        {
            throw new FooterAuthoringException(
                StatusCodes.Status428PreconditionRequired,
                "FOOTER_REVISION_REQUIRED",
                "The current footer revision is required before saving.",
                "expectedRevision");
        }

        var ownsTransaction = _dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction == null;
        await using var transaction = ownsTransaction
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        try
        {
            var current = scope == FooterAuthoringScope.Marketplace
                ? await LoadMarketplaceResponseAsync(asTracking: true, cancellationToken)
                : await LoadStorefrontResponseAsync(scope, tenantId, asTracking: true, cancellationToken);

            if (!string.Equals(request.ExpectedRevision.Trim(), current.Revision, StringComparison.Ordinal))
            {
                throw CreateConflict();
            }

            var publishedCmsTargets = await LoadPublishedCmsTargetsAsync(scope, tenantId, cancellationToken);
            _validator.Validate(request, scope, publishedCmsTargets.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));

            var now = DateTime.UtcNow;
            if (scope == FooterAuthoringScope.Marketplace)
            {
                ReplaceMarketplace(request, now);
            }
            else
            {
                ReplaceStorefront(scope, tenantId, request, now, publishedCmsTargets);
            }

            await SaveChangesAsync(cancellationToken);
            await PublishUpdatedEventAsync(scope, tenantId, now, cancellationToken);
            await SaveChangesAsync(cancellationToken);

            var accepted = scope == FooterAuthoringScope.Marketplace
                ? await LoadMarketplaceResponseAsync(asTracking: false, cancellationToken)
                : await LoadStorefrontResponseAsync(scope, tenantId, asTracking: false, cancellationToken);

            await CommitAsync(transaction, cancellationToken);
            return accepted;
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _dbContext.ChangeTracker.Clear();
            }

            throw;
        }
    }

    private async Task<AdminStorefrontFooterConfigurationResponse> LoadStorefrontResponseAsync(
        FooterAuthoringScope scope,
        Guid? tenantId,
        bool asTracking,
        CancellationToken cancellationToken)
    {
        var sectionsQuery = _dbContext.StorefrontFooterSections
            .IgnoreQueryFilters()
            .Where(section => section.TenantId == tenantId)
            .Include(section => section.Links)
            .AsQueryable();
        var socialQuery = _dbContext.StorefrontSocialLinks
            .IgnoreQueryFilters()
            .Where(link => link.TenantId == tenantId)
            .AsQueryable();

        if (!asTracking)
        {
            sectionsQuery = sectionsQuery.AsNoTracking();
            socialQuery = socialQuery.AsNoTracking();
        }

        var sections = await sectionsQuery.ToListAsync(cancellationToken);
        var socialLinks = await socialQuery.ToListAsync(cancellationToken);
        var canonicalCmsSlugs = tenantId.HasValue
            ? (await LoadPublishedCmsTargetsAsync(scope, tenantId, cancellationToken))
                .ToDictionary(target => target.Value, target => target.Key)
            : null;
        return _mapper.MapStorefrontResponse(scope, sections, socialLinks, canonicalCmsSlugs);
    }

    private async Task<AdminStorefrontFooterConfigurationResponse> LoadMarketplaceResponseAsync(
        bool asTracking,
        CancellationToken cancellationToken)
    {
        var sectionsQuery = _dbContext.MarketplaceFooterSections
            .IgnoreQueryFilters()
            .Include(section => section.Links)
            .AsQueryable();
        var socialQuery = _dbContext.MarketplaceSocialLinks
            .IgnoreQueryFilters()
            .AsQueryable();

        if (!asTracking)
        {
            sectionsQuery = sectionsQuery.AsNoTracking();
            socialQuery = socialQuery.AsNoTracking();
        }

        var sections = await sectionsQuery.ToListAsync(cancellationToken);
        var socialLinks = await socialQuery.ToListAsync(cancellationToken);
        return _mapper.MapMarketplaceResponse(sections, socialLinks);
    }

    private async Task<IReadOnlyDictionary<string, Guid>> LoadPublishedCmsTargetsAsync(
        FooterAuthoringScope scope,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (scope == FooterAuthoringScope.Global)
        {
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        }

        if (scope == FooterAuthoringScope.Marketplace)
        {
            var pages = await _dbContext.MarketplaceCmsPages
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(page => page.IsPublished && !page.IsDraft && page.PublishedAt != null)
                .Select(page => new { page.PageGroupId, page.Slug, page.Version, page.PublishedAt })
                .ToListAsync(cancellationToken);

            return pages
                .GroupBy(page => page.PageGroupId)
                .Select(group => group.OrderByDescending(page => page.Version).ThenByDescending(page => page.PublishedAt).First())
                .ToDictionary(page => page.Slug, page => page.PageGroupId, StringComparer.OrdinalIgnoreCase);
        }

        var storefrontPages = await _dbContext.CmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => page.TenantId == tenantId)
            .Where(page => !page.IsDeleted && page.IsPublished && !page.IsDraft && page.PublishedAt != null)
            .Select(page => new { page.PageGroupId, page.Slug, page.Version, page.PublishedAt })
            .ToListAsync(cancellationToken);

        return storefrontPages
            .GroupBy(page => page.PageGroupId)
            .Select(group => group.OrderByDescending(page => page.Version).ThenByDescending(page => page.PublishedAt).First())
            .ToDictionary(page => page.Slug, page => page.PageGroupId, StringComparer.OrdinalIgnoreCase);
    }

    private void ReplaceStorefront(
        FooterAuthoringScope scope,
        Guid? tenantId,
        AdminStorefrontFooterConfigurationRequest request,
        DateTime now,
        IReadOnlyDictionary<string, Guid> publishedCmsTargets)
    {
        var existingSections = _dbContext.ChangeTracker.Entries<StorefrontFooterSection>()
            .Where(entry => entry.Entity.TenantId == tenantId)
            .Select(entry => entry.Entity)
            .ToList();
        var existingSocialLinks = _dbContext.ChangeTracker.Entries<StorefrontSocialLink>()
            .Where(entry => entry.Entity.TenantId == tenantId)
            .Select(entry => entry.Entity)
            .ToList();

        _dbContext.StorefrontFooterSections.RemoveRange(existingSections);
        _dbContext.StorefrontSocialLinks.RemoveRange(existingSocialLinks);
        _dbContext.StorefrontFooterSections.AddRange(
            _mapper.MapStorefrontSections(request, tenantId, now, publishedCmsTargets));
        _dbContext.StorefrontSocialLinks.AddRange(_mapper.MapStorefrontSocialLinks(request, tenantId, now));
    }

    private void ReplaceMarketplace(AdminStorefrontFooterConfigurationRequest request, DateTime now)
    {
        var existingSections = _dbContext.ChangeTracker.Entries<MarketplaceFooterSection>()
            .Select(entry => entry.Entity)
            .ToList();
        var existingSocialLinks = _dbContext.ChangeTracker.Entries<MarketplaceSocialLink>()
            .Select(entry => entry.Entity)
            .ToList();

        _dbContext.MarketplaceFooterSections.RemoveRange(existingSections);
        _dbContext.MarketplaceSocialLinks.RemoveRange(existingSocialLinks);
        _dbContext.MarketplaceFooterSections.AddRange(_mapper.MapMarketplaceSections(request, now));
        _dbContext.MarketplaceSocialLinks.AddRange(_mapper.MapMarketplaceSocialLinks(request, now));
    }

    private Task PublishUpdatedEventAsync(
        FooterAuthoringScope scope,
        Guid? tenantId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (scope == FooterAuthoringScope.Marketplace)
        {
            return _eventPublisher.PublishAsync(
                new MarketplaceFooterUpdatedEvent { UpdatedAtUtc = now },
                cancellationToken);
        }

        return _eventPublisher.PublishAsync(
            new StorefrontFooterUpdatedEvent
            {
                TenantId = tenantId,
                UpdatedAtUtc = now
            },
            cancellationToken);
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new FooterAuthoringException(
                StatusCodes.Status409Conflict,
                "FOOTER_CONCURRENCY_CONFLICT",
                "Footer configuration was updated by another author. Refresh or retry against the latest revision.",
                innerException: exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres &&
                                                   (postgres.SqlState == PostgresErrorCodes.SerializationFailure ||
                                                    postgres.SqlState == PostgresErrorCodes.UniqueViolation))
        {
            throw CreateConflict();
        }
    }

    private static async Task CommitAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken)
    {
        if (transaction == null)
        {
            return;
        }

        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw CreateConflict();
        }
    }

    private static FooterAuthoringException CreateConflict()
    {
        return new FooterAuthoringException(
            StatusCodes.Status409Conflict,
            "FOOTER_CONCURRENCY_CONFLICT",
            "Footer configuration was updated by another author. Refresh or retry against the latest revision.");
    }

    private static void ValidateScope(FooterAuthoringScope scope, Guid? tenantId)
    {
        if (scope == FooterAuthoringScope.Tenant && (!tenantId.HasValue || tenantId == Guid.Empty))
        {
            throw new FooterAuthoringException(
                StatusCodes.Status400BadRequest,
                "FOOTER_TENANT_REQUIRED",
                "A tenant is required for tenant footer authoring.");
        }

        if (scope != FooterAuthoringScope.Tenant && tenantId.HasValue)
        {
            throw new FooterAuthoringException(
                StatusCodes.Status400BadRequest,
                "FOOTER_SCOPE_INVALID",
                "Global and Marketplace footer authoring cannot use tenant scope.");
        }
    }
}
