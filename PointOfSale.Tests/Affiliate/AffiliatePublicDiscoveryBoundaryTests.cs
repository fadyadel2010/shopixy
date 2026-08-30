using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using Xunit;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-39: Sanitized anonymous public discovery boundary certification.
///
/// Freezes:
/// 1. Categories and public category products share the same SQL-admission helper/boundary.
/// 2. Anonymous public list/detail actions are explicitly allowed while member discovery/detail stay protected.
/// 3. Public DTOs expose no TenantId, commission, max commission, policy/category IDs, variants, or stock internals.
/// 4. Category filtering happens before totalCount/pagination; totals derive from admitted filtered results.
/// 5. Main category includes eligible directly-assigned and child-assigned products; ineligible docs never surface.
/// 6. The public sort allowlist excludes every commission/ranking ordering.
/// </summary>
public sealed class AffiliatePublicDiscoveryBoundaryTests
{
    private const string ControllerRelativePath = "PointOfSale.API|Controllers|AffiliatePortalController.cs";

    private const string CategoriesSignature =
        "public async Task<ActionResult<List<AffiliateCategoryDiscoveryItem>>> DiscoverCategories(";

    private const string MemberListSignature =
        "public async Task<ActionResult<AffiliateProductDiscoveryResponse>> DiscoverProducts(";

    private const string MemberDetailSignature =
        "public async Task<ActionResult<AffiliateProductDetailResponse>> GetProductDetail(";

    private const string PublicListSignature =
        "public async Task<ActionResult<AffiliatePublicProductDiscoveryResponse>> DiscoverPublicProducts(";

    private const string PublicDetailSignature =
        "public async Task<ActionResult<AffiliatePublicProductItem>> DiscoverPublicProductDetail(";

    private const string AdmissionHelperSignature =
        "private async Task<List<AffiliateProductIntelligenceDocument>> LoadSqlAdmittedAffiliateIntelligenceDocumentsAsync(";

    private const string PublicOrderSignature =
        "internal static IOrderedEnumerable<AffiliateProductIntelligenceDocument> OrderAffiliateDocumentsForPublicDiscovery(";

    // ═══════════════════════════════════════════════════════════════════
    // 1. SHARED SQL-ADMISSION BOUNDARY
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Categories_And_Public_List_And_Public_Detail_Share_One_SqlAdmission_Helper()
    {
        var source = ReadControllerSource();

        Assert.Equal(
            1,
            CountOccurrences(source, AdmissionHelperSignature));

        foreach (var signature in new[] { CategoriesSignature, PublicListSignature, PublicDetailSignature })
        {
            var body = ExtractMethodBody(source, signature);
            Assert.Contains("await LoadSqlAdmittedAffiliateIntelligenceDocumentsAsync(ct)", body);
        }
    }

    [Fact]
    public void SqlAdmission_Helper_Keeps_Hierarchy_Policy_Product_Tenant_Pricing_And_Stock_Gates()
    {
        var source = ReadControllerSource();
        var helperBody = ExtractMethodBody(source, AdmissionHelperSignature);
        var recomposeBody = ExtractMethodBody(
            source,
            "private async Task<AffiliateProductIntelligenceDocument?> RecomposeAffiliateIntelligenceDocumentBySqlAuthorityAsync(");

        // SQL identity admission gates (policy/category/product/tenant)
        Assert.Contains("policy.IsEnabled &&", helperBody);
        Assert.Contains("!policy.IsDeleted &&", helperBody);
        Assert.Contains("category.IsActive &&", helperBody);
        Assert.Contains("!category.IsDeleted &&", helperBody);
        Assert.Contains("!product.IsDeleted &&", helperBody);
        Assert.Contains("product.IsActive &&", helperBody);
        Assert.Contains("product.IsOnlineVisible &&", helperBody);
        Assert.Contains("product.IsSearchable &&", helperBody);
        Assert.Contains("product.PublishStatus == PublishStatus.Published &&", helperBody);
        Assert.Contains("tenant.IsActive &&", helperBody);
        Assert.Contains("tenant.DeletedAtUtc == null", helperBody);

        // Pricing/stock recomposition gates shared by public detail deliverability
        Assert.Contains("TryResolveAffiliatePublicPricing", recomposeBody);
        Assert.Contains("!pricing.InStock", recomposeBody);
    }

    [Fact]
    public void SqlAdmitted_Product_Remains_Deliverable_When_Mongo_Hydration_Is_Missing()
    {
        var helperBody = ExtractMethodBody(ReadControllerSource(), AdmissionHelperSignature);

        Assert.Contains("var projectedKeys = candidates", helperBody);
        Assert.Contains("authorityKeys", helperBody);
        Assert.Contains("!projectedKeys.Contains((key.TenantId, key.ProductId))", helperBody);
        Assert.Contains("new AffiliateProductIntelligenceDocument", helperBody);
        Assert.Contains("return await RecomposeAffiliateIntelligenceDocumentsBySqlAuthorityAsync(candidates, ct)", helperBody);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 2. AUTHORIZATION CONTRACT
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Public_List_And_Detail_Are_Explicitly_Anonymous_On_The_Public_Products_Routes()
    {
        var list = ControllerType.GetMethod("DiscoverPublicProducts");
        Assert.NotNull(list);
        Assert.NotNull(list!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("discover/public-products", list.GetCustomAttribute<HttpGetAttribute>()!.Template);

        var detail = ControllerType.GetMethod("DiscoverPublicProductDetail");
        Assert.NotNull(detail);
        Assert.NotNull(detail!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("discover/public-products/{productId:guid}", detail.GetCustomAttribute<HttpGetAttribute>()!.Template);

        Assert.NotNull(list.GetCustomAttribute<ResponseCacheAttribute>());
        Assert.True(list.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        Assert.NotNull(detail.GetCustomAttribute<ResponseCacheAttribute>());
        Assert.True(detail.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
    }

    [Fact]
    public void Member_Discovery_And_Detail_Remain_Protected_Under_ClassLevel_AffiliateOnly()
    {
        var classAuthorize = ControllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(classAuthorize);
        Assert.Equal("AffiliateOnly", classAuthorize!.Policy);

        foreach (var methodName in new[] { "DiscoverProducts", "GetProductDetail" })
        {
            var method = ControllerType.GetMethod(methodName);
            Assert.NotNull(method);
            Assert.Null(method!.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.NotNull(method.GetCustomAttribute<HttpGetAttribute>());
        }
    }

    [Fact]
    public void Public_List_Defaults_To_Newest_And_Accepts_Search_CategorySlug_Paging()
    {
        var method = ControllerType.GetMethod("DiscoverPublicProducts");
        Assert.NotNull(method);

        var parameterNames = method!.GetParameters().Select(p => p.Name).ToList();
        Assert.Contains("page", parameterNames);
        Assert.Contains("pageSize", parameterNames);
        Assert.Contains("search", parameterNames);
        Assert.Contains("categorySlug", parameterNames);

        var sortParameter = method.GetParameters().Single(p => p.Name == "sort");
        Assert.True(sortParameter.HasDefaultValue);
        Assert.Equal("newest", sortParameter.DefaultValue);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 3. PUBLIC DTO SANITIZATION
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(typeof(AffiliatePublicProductDiscoveryResponse))]
    [InlineData(typeof(AffiliatePublicProductItem))]
    public void Public_Dtos_Never_Expose_Internal_Intelligence(Type dtoType)
    {
        var propertyNames = dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        foreach (var forbidden in new[]
                 {
                     "TenantId",
                     "CommissionRate",
                     "EstimatedCommission",
                     "MaxCommission",
                     "MaxCommissionPerItem",
                     "AffiliateCategoryId",
                     "AffiliateParentCategoryId",
                     "PolicyId",
                     "CategoryId",
                     "Variants",
                     "TotalAvailableStock",
                     "LowStock",
                     "IsOutOfStock",
                     "RankingScore",
                     "TotalClicks",
                     "TotalEarnings"
                 })
        {
            Assert.DoesNotContain(forbidden, propertyNames);
        }
    }

    [Fact]
    public void Public_Item_Carries_The_Presentation_Fields_The_Hub_Needs()
    {
        var propertyNames = typeof(AffiliatePublicProductItem).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        foreach (var required in new[]
                 {
                     "ProductId", "Slug", "Name", "Description", "Images",
                     "Price", "Currency", "StoreName", "StoreSlug",
                     "CategoryName", "CategorySlug"
                 })
        {
            Assert.Contains(required, propertyNames);
        }
    }

    [Fact]
    public void Public_Mapper_Projects_Only_Presentation_Fields_From_Admitted_Documents()
    {
        var source = ReadControllerSource();
        var mapperBody = ExtractMethodBody(
            source,
            "private static AffiliatePublicProductItem MapAffiliatePublicProductItem(");

        Assert.DoesNotContain("TenantId", mapperBody);
        Assert.DoesNotContain("CommissionRate", mapperBody);
        Assert.DoesNotContain("EstimatedCommission", mapperBody);
        Assert.DoesNotContain("Variants", mapperBody);
        Assert.DoesNotContain("TotalAvailableStock", mapperBody);
        Assert.Contains("ProductId =", mapperBody);
        Assert.Contains("Price =", mapperBody);
        Assert.Contains("CategorySlug = ResolveAffiliateEffectiveCategorySlug(d)", mapperBody);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 4. CATEGORY FILTER BEFORE TOTAL AND PAGINATION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Public_List_Applies_Admission_Then_Filter_Then_Total_Then_Pagination_In_Order()
    {
        var source = ReadControllerSource();
        var body = ExtractMethodBody(source, PublicListSignature);

        var admissionIndex = body.IndexOf("LoadSqlAdmittedAffiliateIntelligenceDocumentsAsync(ct)", StringComparison.Ordinal);
        var filterIndex = body.IndexOf("FilterPublicAffiliateDocuments(admittedDocs, search, categorySlug)", StringComparison.Ordinal);
        var totalIndex = body.IndexOf("var totalCount = filteredDocs.Count;", StringComparison.Ordinal);
        var skipIndex = body.IndexOf(".Skip(PaginationGuard.GetSkip(page, pageSize))", StringComparison.Ordinal);
        var takeIndex = body.IndexOf(".Take(pageSize)", StringComparison.Ordinal);

        Assert.True(admissionIndex >= 0, "public list must admit through the shared SQL boundary");
        Assert.True(filterIndex > admissionIndex, "category filter must run after admission");
        Assert.True(totalIndex > filterIndex, "totalCount must be derived after category filtering");
        Assert.True(skipIndex > totalIndex, "pagination Skip must come after totalCount");
        Assert.True(takeIndex > skipIndex, "pagination Take must follow Skip");
    }

    [Fact]
    public void Totals_And_Pages_Are_Derived_From_Admitted_Filtered_Results()
    {
        var docs = new List<AffiliateProductIntelligenceDocument>
        {
            Doc(slug: "direct-a", name: "Direct A", categorySlug: "electronics", updatedAt: new DateTime(2026, 1, 3)),
            Doc(slug: "child-a", name: "Child A", categorySlug: "phones", parentSlug: "electronics", updatedAt: new DateTime(2026, 1, 2)),
            Doc(slug: "other-a", name: "Other A", categorySlug: "fashion", updatedAt: new DateTime(2026, 1, 1)),
        };

        var filtered = AffiliatePortalController.FilterPublicAffiliateDocuments(docs, search: null, categorySlug: "electronics")
            .ToList();

        Assert.Equal(2, filtered.Count);

        var totalCount = filtered.Count;
        var totalPages = (int)Math.Ceiling(totalCount / 1d);
        var pageTwo = AffiliatePortalController.OrderAffiliateDocumentsForPublicDiscovery(filtered, "newest")
            .Skip(PaginationGuard.GetSkip(2, 1))
            .Take(1)
            .ToList();

        Assert.Equal(2, totalPages);
        Assert.Single(pageTwo);
        // Newest-first slice of the admitted+filtered set only — the excluded doc never shifts totals or pages.
        Assert.Equal("child-a", pageTwo[0].ProductSlug);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 5. HIERARCHY BEHAVIOR OVER THE SHARED BOUNDARY
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Main_Category_Slug_Includes_Eligible_Direct_And_Child_Products_And_Excludes_Everything_Else()
    {
        var docs = new List<AffiliateProductIntelligenceDocument>
        {
            Doc(slug: "electronics-direct", name: "Nokia Phone", categorySlug: "electronics"),
            Doc(slug: "phones-child", name: "Smartphone X", categorySlug: "phones", parentSlug: "electronics"),
            Doc(slug: "fashion-item", name: "T-Shirt", categorySlug: "fashion"),
            Doc(slug: "orphan-child", name: "Orphan Item", categorySlug: "gadgets", parentSlug: "dead-parent"),
            Doc(slug: "no-category", name: "Uncategorized"),
        };

        var filtered = AffiliatePortalController
            .FilterPublicAffiliateDocuments(docs, search: null, categorySlug: "electronics")
            .Select(d => d.ProductSlug)
            .ToList();

        Assert.Contains("electronics-direct", filtered);
        Assert.Contains("phones-child", filtered);
        Assert.DoesNotContain("fashion-item", filtered);
        Assert.DoesNotContain("orphan-child", filtered);
        Assert.DoesNotContain("no-category", filtered);
        Assert.Equal(2, filtered.Count);
    }

    [Fact]
    public void Ineligible_Products_Are_Excluded_By_The_Shared_Gates_Regardless_Of_Category()
    {
        var docs = new List<AffiliateProductIntelligenceDocument>
        {
            Doc(slug: "valid", name: "Valid", categorySlug: "electronics"),
            Doc(slug: "out-of-stock", name: "Out Of Stock", categorySlug: "electronics", outOfStock: true),
            Doc(slug: "zero-price", name: "Zero Price", categorySlug: "electronics", price: 0m),
            Doc(slug: "negative-price", name: "Negative Price", categorySlug: "electronics", price: -5m),
        };

        var filtered = AffiliatePortalController
            .FilterPublicAffiliateDocuments(docs, search: null, categorySlug: "electronics")
            .Select(d => d.ProductSlug)
            .ToList();

        var withoutCategory = AffiliatePortalController
            .FilterPublicAffiliateDocuments(docs, search: null, categorySlug: null)
            .Select(d => d.ProductSlug)
            .ToList();

        Assert.Equal(new[] { "valid" }, filtered);
        Assert.Equal(new[] { "valid" }, withoutCategory);
    }

    [Fact]
    public void Search_Gate_Matches_Member_Discovery_Behavior()
    {
        var docs = new List<AffiliateProductIntelligenceDocument>
        {
            Doc(slug: "nokia", name: "Nokia G22", categorySlug: "electronics"),
            Doc(slug: "lamp", name: "Desk Lamp", categorySlug: "electronics"),
        };

        var nokiaHits = AffiliatePortalController
            .FilterPublicAffiliateDocuments(docs, search: "nokia", categorySlug: null)
            .Select(d => d.ProductSlug)
            .ToList();

        var memberGateHits = AffiliatePortalController.FilterAffiliateDocuments(
                docs, "nokia", minCommissionRate: null, storeTenantId: null, categoryProductKeys: null)
            .Select(d => d.ProductSlug)
            .ToList();

        Assert.Equal(new[] { "nokia" }, nokiaHits);
        Assert.Equal(nokiaHits, memberGateHits);
    }

    [Fact]
    public void Public_Ordering_Is_Deterministic_And_Authority_Keys_Are_Unique()
    {
        var tenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var productA = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var productB = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var docs = new List<AffiliateProductIntelligenceDocument>
        {
            Doc(tenantB, productB, slug: "same-price-b", name: "Beta", price: 50m, updatedAt: new DateTime(2026, 1, 2)),
            Doc(tenantA, productB, slug: "same-price-a", name: "Alpha", price: 50m, updatedAt: new DateTime(2026, 1, 2)),
            Doc(tenantA, productA, slug: "cheap", name: "Gamma", price: 10m, updatedAt: new DateTime(2026, 3, 1)),
            Doc(tenantB, productA, slug: "expensive", name: "Delta", price: 90m, updatedAt: new DateTime(2026, 2, 1)),
        };

        var runOne = AffiliatePortalController.OrderAffiliateDocumentsForPublicDiscovery(docs, "price_asc").ToList();
        var runTwo = AffiliatePortalController.OrderAffiliateDocumentsForPublicDiscovery(docs, "price_asc").ToList();

        // Deterministic across repeated runs; price ascending with (TenantId, ProductId) tie-breaking.
        Assert.Equal(runOne.Select(Key), runTwo.Select(Key));
        Assert.Equal(
            new[] { "cheap", "same-price-a", "same-price-b", "expensive" },
            runOne.Select(d => d.ProductSlug).ToArray());
        Assert.Equal(4, runOne.Select(Key).Distinct().Count());

        var newestFirst = AffiliatePortalController.OrderAffiliateDocumentsForPublicDiscovery(docs, "newest").ToList();
        Assert.Equal(new DateTime(2026, 3, 1), newestFirst[0].LastUpdatedUtc);
    }

    [Fact]
    public void Public_Detail_Resolves_Through_Admitted_Docs_With_Deliverability_Gates()
    {
        var source = ReadControllerSource();
        var body = ExtractMethodBody(source, PublicDetailSignature);

        Assert.Contains("LoadSqlAdmittedAffiliateIntelligenceDocumentsAsync(ct)", body);
        Assert.Contains("item.BasePrice > 0m && !item.IsOutOfStock", body);
        Assert.Contains("item.ProductId == productId", body);
        Assert.Contains("OrderBy(item => item.TenantId)", body);
        Assert.Contains("MapAffiliatePublicProductItem(doc)", body);
        Assert.Contains("return NotFound(new { message = \"Product not found\" });", body);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 6. PUBLIC SORT ALLOWLIST
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Public_Sort_Allowlist_Excludes_Commission_And_Ranking_Sorts()
    {
        var allowlist = AffiliatePortalController.AffiliatePublicDiscoverySortAllowlist;

        Assert.DoesNotContain("commission_desc", allowlist);
        Assert.DoesNotContain("commission_asc", allowlist);
        Assert.DoesNotContain("ranking", allowlist);

        Assert.Contains("newest", allowlist);
        Assert.Contains("price_desc", allowlist);
        Assert.Contains("price_asc", allowlist);
        Assert.Contains("name_asc", allowlist);
    }

    [Fact]
    public void Public_Ordering_Never_Reads_Commission_Or_Ranking_Signals()
    {
        var source = ReadControllerSource();
        var body = ExtractMethodBody(source, PublicOrderSignature);

        Assert.DoesNotContain("CommissionRate", body);
        Assert.DoesNotContain("RankingScore", body);
        Assert.DoesNotContain("EstimatedCommission", body);
        Assert.Contains("ThenBy(document => document.TenantId)", body);
        Assert.Contains("ThenBy(document => document.ProductId)", body);
    }

    [Fact]
    public async Task Public_List_Rejects_Commission_Sorts_With_A_Clean_400_Contract()
    {
        var controller = new AffiliatePortalController(
            dbContext: null!,
            mongoDbContext: null!,
            affiliateService: null!,
            payoutService: null!,
            tenantContext: null!,
            logger: null!);

        foreach (var rejectedSort in new[] { "commission_desc", "commission_asc", "ranking", "DROP TABLE" })
        {
            var result = await controller.DiscoverPublicProducts(sort: rejectedSort);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.NotNull(badRequest.Value);
            var payloadProperty = badRequest.Value!.GetType().GetProperty("error");
            Assert.NotNull(payloadProperty);
        }
    }

    [Fact]
    public void Missing_Sql_Policy_Clears_Stale_Mongo_Affiliate_Metadata_With_Event_Fencing()
    {
        var source = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Consumers",
            "Affiliate",
            "AffiliateProductIntelligenceProjectionConsumer.cs");
        var missingPolicyIndex = source.IndexOf("if (policy == null)", StringComparison.Ordinal);
        Assert.True(missingPolicyIndex >= 0);

        var clearBranch = source.Substring(
            missingPolicyIndex,
            source.IndexOf("AffiliateCategory? category", missingPolicyIndex, StringComparison.Ordinal) - missingPolicyIndex);

        Assert.Contains(".Set(d => d.CommissionRate, 0m)", clearBranch);
        Assert.Contains(".Set(d => d.EstimatedCommission, 0m)", clearBranch);
        Assert.Contains(".Set(d => d.AffiliateCategoryId, null)", clearBranch);
        Assert.Contains("BuildFreshPolicyEventFilter(", clearBranch);
        Assert.Contains("new UpdateOptions { IsUpsert = false }", clearBranch);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static Type ControllerType { get; } = typeof(AffiliatePortalController);

    private static string Key(AffiliateProductIntelligenceDocument document) =>
        $"{document.TenantId}:{document.ProductId}";

    private static AffiliateProductIntelligenceDocument Doc(
        Guid? tenantId = null,
        Guid? productId = null,
        string slug = "",
        string name = "",
        decimal price = 100m,
        bool outOfStock = false,
        string? categorySlug = null,
        string? parentSlug = null,
        DateTime? updatedAt = null)
    {
        return new AffiliateProductIntelligenceDocument
        {
            TenantId = tenantId ?? Guid.NewGuid(),
            ProductId = productId ?? Guid.NewGuid(),
            ProductSlug = slug,
            ProductName = name,
            BasePrice = price,
            IsOutOfStock = outOfStock,
            AffiliateCategorySlug = categorySlug,
            AffiliateParentCategorySlug = parentSlug,
            LastUpdatedUtc = updatedAt ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Images = [],
            Variants = []
        };
    }

    private static string ReadControllerSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var segments = ControllerRelativePath.Split('|');
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(segments).ToArray()));
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(segments).ToArray()));
    }

    private static int CountOccurrences(string source, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string ExtractMethodBody(string source, string signature)
    {
        var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Signature not found in controller source: {signature}");

        var bodyStart = source.IndexOf('{', signatureIndex);
        Assert.True(bodyStart > signatureIndex, $"No body found for: {signature}");

        var depth = 0;
        for (var i = bodyStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(bodyStart, i - bodyStart + 1);
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces while extracting body of: {signature}");
    }
}
