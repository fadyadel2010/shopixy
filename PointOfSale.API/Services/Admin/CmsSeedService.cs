using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.Homepage;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Admin;

public interface ICmsSeedService
{
    Task<AdminCmsSeedResponse> SeedAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default);
}

public sealed class CmsSeedException : Exception
{
    public CmsSeedException(int statusCode, string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}

public sealed class CmsSeedService : ICmsSeedService
{
    private static readonly string[] DefaultSlugs = ["about", "contact", "privacy", "terms"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _dbContext;
    private readonly ICmsPageContentValidator _cmsContentValidator;
    private readonly IHomepageSectionSchemaRegistry _homepageSchemaRegistry;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<CmsSeedService> _logger;

    public CmsSeedService(
        ApplicationDbContext dbContext,
        ICmsPageContentValidator cmsContentValidator,
        IHomepageSectionSchemaRegistry homepageSchemaRegistry,
        IEventPublisher eventPublisher,
        ILogger<CmsSeedService> logger)
    {
        _dbContext = dbContext;
        _cmsContentValidator = cmsContentValidator;
        _homepageSchemaRegistry = homepageSchemaRegistry;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<AdminCmsSeedResponse> SeedAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = null;
        var ownsTransaction = false;

        try
        {
            if (!_dbContext.Database.IsRelational())
            {
                throw new InvalidOperationException("CMS seed requires a relational transaction provider.");
            }

            transaction = _dbContext.Database.CurrentTransaction;
            if (transaction == null)
            {
                transaction = await _dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                ownsTransaction = true;
            }

            var tenantExists = await _dbContext.Tenants
                .AsNoTracking()
                .AnyAsync(
                    tenant => tenant.Id == tenantId && tenant.Status != TenantStatus.Deleted,
                    cancellationToken);
            if (!tenantExists)
            {
                throw SeedError(
                    StatusCodes.Status404NotFound,
                    "CMS_SEED_TENANT_NOT_FOUND",
                    "The tenant does not exist or is no longer available.");
            }

            var storeConfig = await ResolveStoreConfigAsync(tenantId, cancellationToken);
            if (storeConfig == null)
            {
                throw SeedError(
                    StatusCodes.Status409Conflict,
                    "CMS_SEED_STORE_CONFIG_REQUIRED",
                    "CMS seed requires an existing StoreConfig for the tenant.");
            }

            var activeDefaultPages = await _dbContext.CmsPages
                .IgnoreQueryFilters()
                .Where(page => page.TenantId == tenantId)
                .Where(page => !page.IsDeleted && (page.IsDraft || page.IsPublished))
                .Where(page => DefaultSlugs.Contains(page.Slug))
                .ToListAsync(cancellationToken);
            var existingPageGroups = ResolveCanonicalPageGroups(activeDefaultPages);

            var customizationRoot = ParseObject(
                storeConfig.CustomizationJson,
                "StoreConfig customization JSON is invalid.");
            var navigationExists = ResolveNavigationExists(customizationRoot);
            var homepageExists = await _dbContext.HomepageSections
                .AsNoTracking()
                .AnyAsync(
                    section => section.TenantId == tenantId &&
                               !section.IsDeleted &&
                               (section.IsDraft || section.IsPublished),
                    cancellationToken);
            var footerExists = await _dbContext.StorefrontFooterSections
                .AsNoTracking()
                .IgnoreQueryFilters()
                .AnyAsync(section => section.TenantId == tenantId, cancellationToken) ||
                await _dbContext.StorefrontSocialLinks
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .AnyAsync(link => link.TenantId == tenantId, cancellationToken);

            var missingPageRequests = BuildDefaultPages()
                .Where(request => !existingPageGroups.ContainsKey(request.Slug))
                .ToList();
            if (missingPageRequests.Count == 0 && navigationExists && homepageExists && footerExists)
            {
                throw SeedError(
                    StatusCodes.Status409Conflict,
                    "CMS_SEED_ALREADY_COMPLETED",
                    "The tenant CMS is already seeded. Existing authored content was not changed.");
            }

            var now = DateTime.UtcNow;
            var events = new List<IntegrationEvent>();
            var seededPages = new List<string>();

            foreach (var request in missingPageRequests)
            {
                var page = CreatePublishedPage(tenantId, request, now);
                _dbContext.CmsPages.Add(page);
                existingPageGroups[request.Slug] = page.PageGroupId;
                seededPages.Add(request.Slug);
                events.Add(new StorefrontCmsPagePublishedEvent
                {
                    TenantId = tenantId,
                    PageGroupId = page.PageGroupId,
                    Slug = page.Slug,
                    PageVersion = page.Version,
                    EventVersion = page.Version,
                    PublishedAtUtc = now,
                    CorrelationId = traceId,
                    IdempotencyKey = $"STOREFRONT_CMS_PUBLISHED:{tenantId:N}:{page.PageGroupId:N}:{page.Version}"
                });
            }

            var navigationCreated = false;
            if (!navigationExists)
            {
                AddDefaultNavigation(customizationRoot, existingPageGroups, now);
                storeConfig.CustomizationJson = customizationRoot.ToJsonString(JsonOptions);
                storeConfig.UpdatedAtUtc = now;
                var navigationVersion = AppendNavigationVersion(storeConfig, now);
                navigationCreated = true;
                events.Add(new NavigationPublishedEvent
                {
                    TenantId = tenantId,
                    StoreConfigId = storeConfig.Id,
                    EventVersion = navigationVersion,
                    PublishedAtUtc = now,
                    CorrelationId = traceId,
                    IdempotencyKey = $"NAVIGATION_PUBLISHED:{tenantId:N}:{storeConfig.Id:N}:{navigationVersion}"
                });
            }

            var homepageCreated = false;
            if (!homepageExists)
            {
                var homepageSections = CreateHomepageSections(tenantId, now);
                _dbContext.HomepageSections.AddRange(homepageSections);
                homepageCreated = true;
                events.Add(new HomepageSectionsPublishedEvent
                {
                    TenantId = tenantId,
                    EventVersion = 1,
                    PublishedAtUtc = now,
                    SectionCount = homepageSections.Count,
                    CorrelationId = traceId,
                    IdempotencyKey = $"HOMEPAGE_SEEDED:{tenantId:N}:1"
                });
            }

            var footerCreated = false;
            if (!footerExists)
            {
                _dbContext.StorefrontFooterSections.Add(CreateFooter(tenantId, now));
                footerCreated = true;
                events.Add(new StorefrontFooterUpdatedEvent
                {
                    TenantId = tenantId,
                    EventVersion = 1,
                    UpdatedAtUtc = now,
                    CorrelationId = traceId,
                    IdempotencyKey = $"STOREFRONT_FOOTER_SEEDED:{tenantId:N}:1"
                });
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            foreach (var @event in events)
            {
                await _eventPublisher.PublishAsync((object)@event, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (ownsTransaction)
            {
                await transaction!.CommitAsync(cancellationToken);
            }

            _logger.LogInformation(
                "CMS seed committed | TenantId: {TenantId} | Pages: {PageCount} | Navigation: {NavigationCreated} | Homepage: {HomepageCreated} | Footer: {FooterCreated} | TraceId: {TraceId}",
                tenantId,
                seededPages.Count,
                navigationCreated,
                homepageCreated,
                footerCreated,
                traceId);

            return new AdminCmsSeedResponse
            {
                Success = true,
                Status = "Seeded",
                SeededPages = seededPages,
                NavigationCreated = navigationCreated,
                HomepageCreated = homepageCreated,
                FooterCreated = footerCreated,
                Message = $"CMS seed completed with {seededPages.Count} new page(s)."
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollbackAsync(transaction, ownsTransaction);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch (CmsSeedException)
        {
            await RollbackAsync(transaction, ownsTransaction);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await RollbackAsync(transaction, ownsTransaction);
            _dbContext.ChangeTracker.Clear();
            throw SeedError(
                StatusCodes.Status409Conflict,
                "CMS_SEED_CONFLICT",
                "CMS seed conflicted with another authoring operation. Retry from the latest state.",
                ex);
        }
        catch (Exception ex) when (IsDatabaseConflict(ex))
        {
            await RollbackAsync(transaction, ownsTransaction);
            _dbContext.ChangeTracker.Clear();
            throw SeedError(
                StatusCodes.Status409Conflict,
                "CMS_SEED_CONFLICT",
                "CMS seed conflicted with another seed or duplicate content. Retry from the latest state.",
                ex);
        }
        catch
        {
            await RollbackAsync(transaction, ownsTransaction);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            if (ownsTransaction && transaction != null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<StoreConfig?> ResolveStoreConfigAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await _dbContext.StoreConfigs
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "StoreConfigs"
                    WHERE "TenantId" = {tenantId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.StoreConfigs
            .SingleOrDefaultAsync(config => config.TenantId == tenantId, cancellationToken);
    }

    private static Dictionary<string, Guid> ResolveCanonicalPageGroups(IReadOnlyCollection<CmsPage> pages)
    {
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var slug in DefaultSlugs)
        {
            var matching = pages
                .Where(page => string.Equals(page.Slug, slug, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matching.Count == 0)
            {
                continue;
            }

            var groupIds = matching.Select(page => page.PageGroupId).Distinct().ToList();
            if (groupIds.Count != 1)
            {
                throw SeedError(
                    StatusCodes.Status409Conflict,
                    "CMS_SEED_DUPLICATE_CONTENT",
                    $"Multiple active CMS page groups use the canonical slug '{slug}'.");
            }

            if (!matching.Any(page => page.IsPublished && page.PublishedAt.HasValue))
            {
                throw SeedError(
                    StatusCodes.Status409Conflict,
                    "CMS_SEED_CONTENT_CONFLICT",
                    $"CMS page '{slug}' exists only as a draft. Publish or remove it before seeding.");
            }

            result[slug] = groupIds[0];
        }

        return result;
    }

    private CmsPage CreatePublishedPage(
        Guid tenantId,
        AdminCmsPageRequest request,
        DateTime now)
    {
        var requestError = request.Validate();
        if (requestError != null)
        {
            throw SeedError(StatusCodes.Status422UnprocessableEntity, "CMS_SEED_VALIDATION_FAILED", requestError);
        }

        var validation = _cmsContentValidator.ValidateAndNormalize(
            request.ContentType,
            request.Content,
            request.SchemaVersion);
        var pageGroupId = CreateDeterministicGuid(tenantId, $"cms-page-group:{request.Slug}");

        return new CmsPage
        {
            Id = CreateDeterministicGuid(tenantId, $"cms-page-record:{request.Slug}:1"),
            PageGroupId = pageGroupId,
            TenantId = tenantId,
            Slug = request.Slug,
            Title = request.Title,
            Content = validation.NormalizedContent,
            ContentType = validation.ContentType,
            Version = 1,
            SchemaVersion = validation.SchemaVersion,
            IsDeleted = false,
            IsDraft = false,
            IsPublished = true,
            PublishedAt = now,
            MetaTitle = request.Seo.MetaTitle,
            MetaDescription = request.Seo.MetaDescription,
            MetaKeywords = request.Seo.MetaKeywords,
            OpenGraphImageUrl = request.Seo.OpenGraphImageUrl,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static bool ResolveNavigationExists(JsonObject customizationRoot)
    {
        if (!customizationRoot.TryGetPropertyValue("navigation", out var navigation))
        {
            return false;
        }

        if (navigation is not JsonObject)
        {
            throw SeedError(
                StatusCodes.Status422UnprocessableEntity,
                "CMS_SEED_VALIDATION_FAILED",
                "StoreConfig navigation JSON is invalid.");
        }

        return true;
    }

    private static void AddDefaultNavigation(
        JsonObject customizationRoot,
        IReadOnlyDictionary<string, Guid> pageGroups,
        DateTime now)
    {
        var header = new List<AdminNavigationLinkRequest>
        {
            new() { Label = "Home", Type = "custom", Path = "/", Order = 0, Enabled = true },
            new() { Label = "Shop", Type = "collection", CollectionKey = NavigationAdmissionService.AllProductsCollectionKey, Path = "/products", Order = 1, Enabled = true }
        };
        var footer = new List<AdminNavigationLinkRequest>
        {
            new() { Label = "About Us", Type = "cms", TargetId = pageGroups["about"], Slug = "about", Order = 0, Enabled = true },
            new() { Label = "Contact", Type = "cms", TargetId = pageGroups["contact"], Slug = "contact", Order = 1, Enabled = true },
            new() { Label = "Privacy Policy", Type = "cms", TargetId = pageGroups["privacy"], Slug = "privacy", Order = 2, Enabled = true },
            new() { Label = "Terms & Conditions", Type = "cms", TargetId = pageGroups["terms"], Slug = "terms", Order = 3, Enabled = true }
        };
        var snapshot = new JsonObject
        {
            ["header"] = new JsonObject
            {
                ["links"] = JsonSerializer.SerializeToNode(
                    header.Select(NavigationStorageAdapter.ToStorage).ToList(),
                    JsonOptions)
            },
            ["footer"] = new JsonObject
            {
                ["links"] = JsonSerializer.SerializeToNode(
                    footer.Select(NavigationStorageAdapter.ToStorage).ToList(),
                    JsonOptions)
            }
        };

        customizationRoot["navigation"] = new JsonObject
        {
            ["draft"] = snapshot.DeepClone(),
            ["live"] = snapshot,
            ["lastPublishedAt"] = now.ToString("O")
        };
    }

    private static int AppendNavigationVersion(StoreConfig config, DateTime now)
    {
        var versions = ParseArray(config.VersionsJson, "StoreConfig version history JSON is invalid.");
        var maximumVersion = versions
            .OfType<JsonObject>()
            .Select(GetSnapshotVersion)
            .DefaultIfEmpty(0)
            .Max();
        var nextVersion = Math.Max(config.LiveVersion, maximumVersion) + 1;

        while (versions.Count >= 50)
        {
            versions.RemoveAt(0);
        }

        versions.Add(new JsonObject
        {
            ["Version"] = nextVersion,
            ["PublishedAt"] = now,
            ["PublishedBy"] = "CMS Seed",
            ["ChangeType"] = "Navigation Seeded",
            ["ThemePresetKey"] = config.ThemeLivePresetKey,
            ["ThemeLiveOverridesJson"] = config.ThemeLiveOverridesJson,
            ["CustomizationJson"] = config.CustomizationJson
        });
        config.VersionsJson = versions.ToJsonString();
        config.LiveVersion = nextVersion;
        return nextVersion;
    }

    private List<HomepageSection> CreateHomepageSections(Guid tenantId, DateTime now)
    {
        var definitions = new (string Type, JsonElement Data)[]
        {
            (HomepageSectionTypes.Hero, JsonSerializer.SerializeToElement(new
            {
                title = "Welcome to Shopixy",
                subtitle = "Discover amazing products",
                imageUrl = CreatePlaceholderImageDataUrl("Welcome to Shopixy", "#0f172a", "#2563eb"),
                ctaText = "Shop Now",
                ctaLink = "/products"
            })),
            (HomepageSectionTypes.Categories, JsonSerializer.SerializeToElement(new
            {
                title = "Shop by Category",
                subtitle = "Browse the top collections for this storefront.",
                source = "all",
                maxItems = 6,
                categoryIds = Array.Empty<string>()
            })),
            (HomepageSectionTypes.Products, JsonSerializer.SerializeToElement(new
            {
                title = "Featured Products",
                subtitle = "Explore products selected from the storefront catalog.",
                source = "all",
                maxItems = 8,
                productIds = Array.Empty<string>(),
                layout = "grid"
            })),
            (HomepageSectionTypes.Banner, JsonSerializer.SerializeToElement(new
            {
                title = "Discover your next favorite",
                subtitle = "Browse the latest products and collections.",
                imageUrl = CreatePlaceholderImageDataUrl("Discover More", "#111827", "#f59e0b"),
                ctaText = "Browse Collections",
                ctaLink = "/products"
            }))
        };

        return definitions.Select((definition, index) =>
        {
            var validation = _homepageSchemaRegistry.ValidateAndNormalize(
                definition.Type,
                definition.Data,
                schemaVersion: 1);
            return new HomepageSection
            {
                Id = CreateDeterministicGuid(tenantId, $"homepage-record:{index}"),
                SectionGroupId = CreateDeterministicGuid(tenantId, $"homepage-group:{index}"),
                Type = validation.Type,
                Order = index,
                Data = validation.NormalizedData,
                Version = 1,
                SchemaVersion = validation.SchemaVersion,
                IsActive = true,
                IsDeleted = false,
                IsDraft = false,
                IsPublished = true,
                PublishedAt = now,
                TenantId = tenantId,
                CreatedAt = now,
                UpdatedAt = now
            };
        }).ToList();
    }

    private static StorefrontFooterSection CreateFooter(Guid tenantId, DateTime now)
    {
        var sectionId = CreateDeterministicGuid(tenantId, "footer-section:information");
        var links = new (string Label, string Slug)[]
        {
            ("About Us", "about"),
            ("Contact", "contact"),
            ("Privacy Policy", "privacy"),
            ("Terms & Conditions", "terms")
        };

        return new StorefrontFooterSection
        {
            Id = sectionId,
            TenantId = tenantId,
            Title = "Information",
            SortOrder = 0,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Links = links.Select((link, index) => new StorefrontFooterLink
            {
                Id = CreateDeterministicGuid(tenantId, $"footer-link:{link.Slug}"),
                SectionId = sectionId,
                Label = link.Label,
                LinkType = FooterLinkType.Cms,
                CmsPageSlug = link.Slug,
                SortOrder = index,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            }).ToList()
        };
    }

    private static IReadOnlyList<AdminCmsPageRequest> BuildDefaultPages() =>
    [
        new AdminCmsPageRequest
        {
            Slug = "about",
            Title = "About Us",
            ContentType = CmsPageContentType.RichText,
            Content = JsonSerializer.SerializeToElement("""
                <h2>About Shopixy</h2>
                <p>We build modern storefront experiences for ambitious marketplace brands.</p>
                <p>Our team focuses on curated products, dependable fulfillment, and customer-first support.</p>
                """),
            Seo = new AdminCmsPageSeoRequest
            {
                MetaTitle = "About Us",
                MetaDescription = "Learn more about our company, values, and what makes Shopixy different."
            }
        },
        new AdminCmsPageRequest
        {
            Slug = "contact",
            Title = "Contact Us",
            ContentType = CmsPageContentType.Blocks,
            Content = JsonSerializer.SerializeToElement(new object[]
            {
                new
                {
                    type = "text",
                    data = new
                    {
                        text = "Need help with an order, wholesale inquiry, or partnership request? Reach our team and we will get back to you quickly.",
                        alignment = "left"
                    }
                },
                new
                {
                    type = "faq",
                    data = new
                    {
                        items = new[]
                        {
                            new { question = "Support email", answer = "support@shopixy.com" },
                            new { question = "Business hours", answer = "Sunday to Thursday, 9 AM to 6 PM" }
                        }
                    }
                }
            }),
            Seo = new AdminCmsPageSeoRequest
            {
                MetaTitle = "Contact Us",
                MetaDescription = "Contact Shopixy support, sales, and partnership teams."
            }
        },
        new AdminCmsPageRequest
        {
            Slug = "privacy",
            Title = "Privacy Policy",
            ContentType = CmsPageContentType.RichText,
            Content = JsonSerializer.SerializeToElement("""
                <h2>Privacy Policy</h2>
                <p>We collect only the information needed to operate the marketplace, fulfill orders, and improve our services.</p>
                <p>Personal data is never sold, and access is limited to authorized operational workflows.</p>
                <p>If you need a copy of your stored information or want us to delete it, contact our privacy team.</p>
                """),
            Seo = new AdminCmsPageSeoRequest
            {
                MetaTitle = "Privacy Policy",
                MetaDescription = "Understand how Shopixy collects, uses, and protects your information."
            }
        },
        new AdminCmsPageRequest
        {
            Slug = "terms",
            Title = "Terms & Conditions",
            ContentType = CmsPageContentType.RichText,
            Content = JsonSerializer.SerializeToElement("""
                <h2>Terms &amp; Conditions</h2>
                <p>By using this storefront, you agree to our ordering, payment, fulfillment, and return terms.</p>
                <p>All orders are subject to availability, fraud screening, and final operational approval.</p>
                <p>Delivery estimates are best-effort timelines and may shift during exceptional demand or carrier disruption.</p>
                """),
            Seo = new AdminCmsPageSeoRequest
            {
                MetaTitle = "Terms & Conditions",
                MetaDescription = "Read the terms that govern purchases and use of the Shopixy storefront."
            }
        }
    ];

    private static JsonObject ParseObject(string? json, string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new JsonObject();
            }

            return JsonNode.Parse(json) as JsonObject
                ?? throw SeedError(StatusCodes.Status422UnprocessableEntity, "CMS_SEED_VALIDATION_FAILED", message);
        }
        catch (JsonException ex)
        {
            throw SeedError(StatusCodes.Status422UnprocessableEntity, "CMS_SEED_VALIDATION_FAILED", message, ex);
        }
    }

    private static JsonArray ParseArray(string? json, string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new JsonArray();
            }

            return JsonNode.Parse(json) as JsonArray
                ?? throw SeedError(StatusCodes.Status422UnprocessableEntity, "CMS_SEED_VALIDATION_FAILED", message);
        }
        catch (JsonException ex)
        {
            throw SeedError(StatusCodes.Status422UnprocessableEntity, "CMS_SEED_VALIDATION_FAILED", message, ex);
        }
    }

    private static int GetSnapshotVersion(JsonObject snapshot)
    {
        var node = snapshot["Version"] ?? snapshot["version"];
        return node is JsonValue value && value.TryGetValue<int>(out var version) ? version : 0;
    }

    private static Guid CreateDeterministicGuid(Guid tenantId, string key)
    {
        var input = Encoding.UTF8.GetBytes($"{tenantId:N}:{key}");
        var hash = SHA256.HashData(input);
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static string CreatePlaceholderImageDataUrl(string title, string startColor, string endColor)
    {
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='1600' height='900'><defs><linearGradient id='g'><stop stop-color='{startColor}'/><stop offset='1' stop-color='{endColor}'/></linearGradient></defs><rect width='1600' height='900' fill='url(#g)'/><text x='100' y='480' fill='white' font-size='80'>{title}</text></svg>";
        return $"data:image/svg+xml;charset=UTF-8,{Uri.EscapeDataString(svg)}";
    }

    private static bool IsDatabaseConflict(Exception exception)
    {
        var postgres = exception as PostgresException
            ?? (exception as DbUpdateException)?.InnerException as PostgresException;
        if (postgres != null)
        {
            return postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation;
        }

        return exception is DbUpdateException updateException &&
               updateException.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static async Task RollbackAsync(IDbContextTransaction? transaction, bool ownsTransaction)
    {
        if (!ownsTransaction || transaction == null)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the original seed failure.
        }
    }

    private static CmsSeedException SeedError(
        int statusCode,
        string code,
        string message,
        Exception? innerException = null) =>
        new(statusCode, code, message, innerException);
}
