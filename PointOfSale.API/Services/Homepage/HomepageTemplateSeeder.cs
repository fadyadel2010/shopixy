using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Homepage;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Homepage;

public interface IHomepageTemplateSeeder
{
    Task<bool> EnsurePublishedTemplateAsync(
        Guid? tenantId,
        Guid? actorId = null,
        CancellationToken cancellationToken = default);

    Task<HomepageAuthoringResult<IReadOnlyList<HomepageSectionDto>>> GenerateDraftTemplateAsync(
        Guid? tenantId,
        bool overwriteExisting,
        string? expectedWorkspaceVersion,
        Guid? actorId = null,
        CancellationToken cancellationToken = default);
}

public sealed class HomepageTemplateSeeder : IHomepageTemplateSeeder
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ISuperAdminHomepageSectionManagementService _managementService;
    private readonly ILogger<HomepageTemplateSeeder> _logger;

    public HomepageTemplateSeeder(
        ApplicationDbContext dbContext,
        ISuperAdminHomepageSectionManagementService managementService,
        ILogger<HomepageTemplateSeeder> logger)
    {
        _dbContext = dbContext;
        _managementService = managementService;
        _logger = logger;
    }

    public async Task<bool> EnsurePublishedTemplateAsync(
        Guid? tenantId,
        Guid? actorId = null,
        CancellationToken cancellationToken = default)
    {
        if (await HasVisibleHomepageSectionsAsync(tenantId, cancellationToken))
        {
            return false;
        }

        var currentSnapshot = await _managementService.GetSectionsAsync(
            tenantId,
            HomepageSectionReadMode.Preview,
            cancellationToken);
        var templateDraft = await _managementService.SaveDraftAsync(
            BuildTemplateRequest(tenantId),
            currentSnapshot.WorkspaceVersion,
            actorId,
            cancellationToken);

        await _managementService.PublishSectionsAsync(
            new PublishHomepageSectionsRequest
            {
                TenantId = tenantId
            },
            templateDraft.WorkspaceVersion,
            actorId,
            cancellationToken);

        _logger.LogInformation(
            "Homepage starter template seeded | TenantId: {TenantId} | SectionCount: {SectionCount}",
            tenantId,
            templateDraft.Data.Count);

        return true;
    }

    public async Task<HomepageAuthoringResult<IReadOnlyList<HomepageSectionDto>>> GenerateDraftTemplateAsync(
        Guid? tenantId,
        bool overwriteExisting,
        string? expectedWorkspaceVersion,
        Guid? actorId = null,
        CancellationToken cancellationToken = default)
    {
        if (!overwriteExisting && await HasVisibleHomepageSectionsAsync(tenantId, cancellationToken))
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status409Conflict,
                "HOMEPAGE_TEMPLATE_EXISTS",
                "A homepage already exists for this scope. Use reset if you want to replace the draft with the starter template.");
        }

        var sections = await _managementService.SaveDraftAsync(
            BuildTemplateRequest(tenantId),
            expectedWorkspaceVersion,
            actorId,
            cancellationToken);

        _logger.LogInformation(
            "Homepage starter template generated for draft workspace | TenantId: {TenantId} | OverwriteExisting: {OverwriteExisting} | SectionCount: {SectionCount}",
            tenantId,
            overwriteExisting,
            sections.Data.Count);

        return sections;
    }

    private Task<bool> HasVisibleHomepageSectionsAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        return ScopeSections(tenantId)
            .AsNoTracking()
            .AnyAsync(section => !section.IsDeleted && (section.IsDraft || section.IsPublished), cancellationToken);
    }

    private IQueryable<PointOfSale.Core.Entities.HomepageSection> ScopeSections(Guid? tenantId)
    {
        return tenantId.HasValue
            ? _dbContext.HomepageSections.Where(section => section.TenantId == tenantId.Value)
            : _dbContext.HomepageSections.Where(section => section.TenantId == null);
    }

    private static SaveHomepageDraftRequest BuildTemplateRequest(Guid? tenantId)
    {
        return new SaveHomepageDraftRequest
        {
            TenantId = tenantId,
            Sections =
            [
                new SaveHomepageDraftSectionRequest
                {
                    Type = HomepageSectionTypes.Hero,
                    Order = 0,
                    SchemaVersion = 1,
                    IsActive = true,
                    Data = JsonSerializer.SerializeToElement(new
                    {
                        title = "Welcome to Shopixy",
                        subtitle = "Discover amazing products",
                        imageUrl = CreatePlaceholderImageDataUrl(
                            "Welcome to Shopixy",
                            "#0f172a",
                            "#2563eb"),
                        ctaText = "Shop Now",
                        ctaLink = "/products"
                    })
                },
                new SaveHomepageDraftSectionRequest
                {
                    Type = HomepageSectionTypes.Categories,
                    Order = 1,
                    SchemaVersion = 1,
                    IsActive = true,
                    Data = JsonSerializer.SerializeToElement(new
                    {
                        title = "Shop by Category",
                        subtitle = "Browse the top collections for this storefront.",
                        source = "all",
                        maxItems = 6,
                        categoryIds = Array.Empty<string>()
                    })
                },
                new SaveHomepageDraftSectionRequest
                {
                    Type = HomepageSectionTypes.Products,
                    Order = 2,
                    SchemaVersion = 1,
                    IsActive = true,
                    Data = JsonSerializer.SerializeToElement(new
                    {
                        title = "Featured Products",
                        subtitle = "A ready-to-publish shelf that automatically pulls from your storefront catalog.",
                        source = "all",
                        maxItems = 8,
                        productIds = Array.Empty<string>(),
                        layout = "grid"
                    })
                },
                new SaveHomepageDraftSectionRequest
                {
                    Type = HomepageSectionTypes.Banner,
                    Order = 3,
                    SchemaVersion = 1,
                    IsActive = true,
                    Data = JsonSerializer.SerializeToElement(new
                    {
                        title = "Launch your next best seller",
                        subtitle = "Use this starter banner to spotlight promotions, collections, or seasonal campaigns.",
                        imageUrl = CreatePlaceholderImageDataUrl(
                            "Launch Campaigns Fast",
                            "#111827",
                            "#f59e0b"),
                        ctaText = "Browse Collections",
                        ctaLink = "/products"
                    })
                }
            ]
        };
    }

    private static string CreatePlaceholderImageDataUrl(string title, string startColor, string endColor)
    {
        var svg = $"""
                   <svg xmlns="http://www.w3.org/2000/svg" width="1600" height="900" viewBox="0 0 1600 900" role="img" aria-label="{EscapeSvg(title)}">
                     <defs>
                       <linearGradient id="g" x1="0%" x2="100%" y1="0%" y2="100%">
                         <stop offset="0%" stop-color="{EscapeSvg(startColor)}" />
                         <stop offset="100%" stop-color="{EscapeSvg(endColor)}" />
                       </linearGradient>
                     </defs>
                     <rect width="1600" height="900" fill="url(#g)" />
                     <circle cx="1260" cy="180" r="180" fill="rgba(255,255,255,0.12)" />
                     <circle cx="260" cy="720" r="220" fill="rgba(255,255,255,0.08)" />
                     <g fill="white" font-family="Segoe UI, Arial, sans-serif">
                       <text x="120" y="360" font-size="42" opacity="0.72" letter-spacing="8">SHOPIXY MARKETPLACE</text>
                       <text x="120" y="470" font-size="92" font-weight="700">{EscapeSvg(title)}</text>
                       <text x="120" y="560" font-size="34" opacity="0.84">Starter homepage artwork generated automatically</text>
                     </g>
                   </svg>
                   """;

        return $"data:image/svg+xml;charset=UTF-8,{Uri.EscapeDataString(svg)}";
    }

    private static string EscapeSvg(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }
}
