using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Exceptions.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Pagination;

namespace PointOfSale.API.Controllers;

/// <summary>
/// AF-4: Affiliate Portal read-only API.
///
/// RULES:
/// - Financial totals come from the SQL commission ledger authority
/// - MongoDB projections are display/search read models only
/// - Never computes new commission
/// - Affiliate can only access their own data
/// - Tenant isolation enforced server-side
///
/// Affiliate identity comes from JWT affiliate_account_id.
/// </summary>
[Authorize(Policy = "AffiliateOnly")]
[ApiController]
[Route("api/affiliate")]
public class AffiliatePortalController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MongoDbContext _mongoDbContext;
    private readonly IAffiliateService _affiliateService;
    private readonly IAffiliatePayoutService _payoutService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AffiliatePortalController> _logger;

    public AffiliatePortalController(
        ApplicationDbContext dbContext,
        MongoDbContext mongoDbContext,
        IAffiliateService affiliateService,
        IAffiliatePayoutService payoutService,
        ITenantContext tenantContext,
        ILogger<AffiliatePortalController> logger)
    {
        _dbContext = dbContext;
        _mongoDbContext = mongoDbContext;
        _affiliateService = affiliateService;
        _payoutService = payoutService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════
    // DIAGNOSTIC ENDPOINT (AF-10 - AUDIT FIX F-A1: Only available in DEBUG builds)
    // ═══════════════════════════════════════════════════════════════════

#if DEBUG
    /// <summary>
    /// GET /api/affiliate/debug-claims
    /// Returns all claims in the current JWT for debugging authorization issues.
    /// SECURITY: Only available in DEBUG builds (gated by preprocessor directive).
    /// </summary>
    [HttpGet("debug-claims")]
    public IActionResult DebugClaims()
    {
        var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();
        return Ok(new
        {
            IsAuthenticated = User.Identity?.IsAuthenticated ?? false,
            AuthenticationType = User.Identity?.AuthenticationType,
            Claims = claims,
            HasRoleClaim = User.HasClaim(System.Security.Claims.ClaimTypes.Role, "Affiliate"),
            RoleClaimValue = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
        });
    }
#endif

    // ═══════════════════════════════════════════════════════════════════
    // DASHBOARD
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/affiliate/dashboard
    /// Returns aggregate dashboard metrics from the SQL commission ledger.
    /// AF-19B: Global affiliates - queries by AffiliateAccountId only (no tenant filter).
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<AffiliateDashboardResponse>> GetDashboard(CancellationToken ct)
    {
        // AF-10 DIAGNOSTIC: Log incoming request claims
        var roleClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var affiliateAccountClaim = User.FindFirst("affiliate_account_id")?.Value;
        _logger.LogInformation("Dashboard request - Role: {Role}, AffiliateAccountId: {AccountId}, IsAuthenticated: {IsAuth}", 
            roleClaim ?? "MISSING", affiliateAccountClaim ?? "MISSING", User.Identity?.IsAuthenticated ?? false);
        
        var affiliateAccountId = await ResolveAffiliateAccountId(ct);
        if (affiliateAccountId == null)
            return NotFound(new { message = "No affiliate account found for this user" });

        var ledgerTotals = await BuildAffiliateLedgerTotalsAsync(affiliateAccountId.Value, storeTenantId: null, ct);

        // AF-19B FIX: Use per-currency earnings summary instead of collapsing currencies
        // This prevents adding USD + EUR + other currencies as if they were the same
        var earningsSummary = await BuildAffiliateLedgerEarningsSummaryAsync(affiliateAccountId.Value, storeTenantId: null, ct);

        if (earningsSummary.Count == 0)
        {
            // Return empty dashboard — affiliate exists but has no activity yet
            return Ok(new AffiliateDashboardResponse());
        }

        return Ok(new AffiliateDashboardResponse
        {
            EarningsByCurrency = earningsSummary,
            TotalOrders = ledgerTotals.TotalOrders,
            LastCommissionAtUtc = ledgerTotals.LastCommissionAtUtc
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // ORDERS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/affiliate/orders?page=1&pageSize=20
    /// Returns paginated commission orders from MongoDB.
    /// AF-19B: Global affiliates - queries by AffiliateAccountId only (no tenant filter).
    /// </summary>
    [HttpGet("orders")]
    public async Task<ActionResult<AffiliateOrdersResponse>> GetOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var affiliateAccountId = await ResolveAffiliateAccountId(ct);
        if (affiliateAccountId == null)
            return NotFound(new { message = "No affiliate account found for this user" });

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // AF-19B: Query by AffiliateAccountId only (global affiliates)
        var filters = new List<FilterDefinition<AffiliateOrderDocument>>
        {
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.AffiliateAccountId, affiliateAccountId.Value)
        };

        // Optional status filter — exact match on settlement status string
        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusOrderIds = await BuildAffiliateOrderIdsForLedgerStatusAsync(
                affiliateAccountId.Value,
                storeTenantId: null,
                status,
                ct);

            if (statusOrderIds.Count == 0)
            {
                return Ok(new AffiliateOrdersResponse
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = 0,
                    TotalPages = 0
                });
            }

            filters.Add(Builders<AffiliateOrderDocument>.Filter.In(d => d.OrderId, statusOrderIds));
        }

        var filter = Builders<AffiliateOrderDocument>.Filter.And(filters);

        var totalCount = await _mongoDbContext.AffiliateOrders.CountDocumentsAsync(filter, cancellationToken: ct);

        var docs = await _mongoDbContext.AffiliateOrders.Find(filter)
            .SortByDescending(d => d.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        var ledgerSnapshots = await BuildAffiliateOrderLedgerSnapshotsAsync(
            affiliateAccountId.Value,
            storeTenantId: null,
            docs.Select(d => d.OrderId).Distinct().ToList(),
            ct);

        return Ok(new AffiliateOrdersResponse
        {
            Orders = docs.Select(d =>
            {
                ledgerSnapshots.TryGetValue(d.OrderId, out var ledger);

                // FALLBACK BEHAVIOR: Missing ledger can occur when:
                // 1. Order has no commission entries yet (e.g., order placed but not yet processed)
                // 2. Order was excluded from affiliate commission (e.g., affiliate's own purchase)
                // 3. Ledger rebuild is in progress (eventual consistency)
                // Fallback strategy:
                // - Status/Currency: Use MongoDB document values (display-only, not authoritative)
                // - Numeric totals (PaidAmount/TotalCommission/TotalApproved/TotalPaid/TotalReversed): Default to 0m (no commission earned)
                // - Item-level fields: Default to 0m/false/"Pending" (no commission data)
                if (ledger == null)
                {
                    _logger.LogWarning(
                        "Missing ledger snapshot for OrderId={OrderId}, AffiliateAccountId={AffiliateAccountId}. " +
                        "Falling back to MongoDB document values for Status/Currency and 0m for commission totals.",
                        d.OrderId, affiliateAccountId.Value);
                }

                return new AffiliateOrderDto
                {
                    OrderId = d.OrderId,
                    OrderNumber = d.OrderNumber,
                    Status = ledger?.Status ?? d.Status, // Fallback: MongoDB document Status (display-only)
                    OrderStatus = d.OrderStatus,
                    FulfillmentStatus = d.FulfillmentStatus,
                    PaymentMethod = d.PaymentMethod,
                    PaidAmount = ledger?.PaidAmount ?? 0m, // Fallback: 0m (no commission base)
                    TotalCommission = ledger?.TotalCommission ?? 0m, // Fallback: 0m (no commission)
                    TotalApproved = ledger?.TotalApproved ?? 0m, // Fallback: 0m (no approved commission)
                    TotalPaid = ledger?.TotalPaid ?? 0m, // Fallback: 0m (no paid commission)
                    TotalReversed = ledger?.TotalReversed ?? 0m, // Fallback: 0m (no reversals)
                    Currency = ledger?.Currency ?? d.Currency, // Fallback: MongoDB document Currency
                    CreatedAtUtc = d.CreatedAtUtc,
                    Items = d.Items.Select(i =>
                    {
                        AffiliateOrderItemLedgerSnapshot? itemLedger = null;
                        ledger?.Items.TryGetValue(i.OrderItemId, out itemLedger);

                        return new AffiliateOrderItemDto
                        {
                            OrderItemId = i.OrderItemId,
                            ProductId = i.ProductId,
                            ProductName = i.ProductName,
                            VariantName = i.VariantName,
                            PaidAmount = itemLedger?.PaidAmount ?? 0m, // Fallback: 0m (no commission base)
                            CommissionAmount = itemLedger?.CommissionAmount ?? 0m, // Fallback: 0m (no commission)
                            IsReversed = itemLedger?.IsReversed ?? false, // Fallback: false (not reversed)
                            SettlementStatus = itemLedger?.SettlementStatus ?? "Pending" // Fallback: "Pending" (no settlement)
                        };
                    }).ToList()
                };
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = (int)totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // EARNINGS SUMMARY
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/affiliate/earnings-summary
    /// Returns per-currency earnings breakdown from the SQL commission ledger.
    /// AF-19B: Global affiliates - queries by AffiliateAccountId only (no tenant filter).
    /// </summary>
    [HttpGet("earnings-summary")]
    public async Task<ActionResult<List<AffiliateEarningsSummaryDto>>> GetEarningsSummary(CancellationToken ct)
    {
        var affiliateAccountId = await ResolveAffiliateAccountId(ct);
        if (affiliateAccountId == null)
            return NotFound(new { message = "No affiliate account found for this user" });

        // AF-19B: Query by AffiliateAccountId only (global affiliates)
        return Ok(await BuildAffiliateLedgerEarningsSummaryAsync(
            affiliateAccountId.Value,
            storeTenantId: null,
            ct));
    }

    // ═══════════════════════════════════════════════════════════════════
    // PROFILE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/affiliate/profile
    /// Returns affiliate profile info (ReferralCode, DisplayName, Status, PayoutWallet).
    /// AF-19B: Global affiliates - queries by AffiliateAccountId only (no tenant filter).
    /// </summary>
    [HttpGet("profile")]
    public async Task<ActionResult<AffiliateProfileResponse>> GetProfile(CancellationToken ct)
    {
        var affiliateAccountId = await ResolveAffiliateAccountId(ct);
        if (affiliateAccountId == null)
            return Unauthorized();

        // AF-19B: Query by AffiliateAccountId only (global affiliates)
        var affiliate = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(a => a.Id == affiliateAccountId.Value && !a.IsDeleted)
            .Select(a => new AffiliateProfileResponse
            {
                AffiliateAccountId = a.Id,
                ReferralCode = a.ReferralCode,
                DisplayName = a.DisplayName,
                Status = a.Status.ToString(),
                ActivatedAtUtc = a.ActivatedAtUtc,
                ClosedAtUtc = a.ClosedAtUtc,
                CreatedAtUtc = a.CreatedAt,
                PayoutWalletNumber = a.PayoutWalletNumber,
                PayoutWalletType = a.PayoutWalletType,
                PayoutWalletUpdatedAtUtc = a.PayoutWalletUpdatedAtUtc
            })
            .FirstOrDefaultAsync(ct);

        if (affiliate == null)
            return NotFound(new { message = "No affiliate account found for this user" });

        return Ok(affiliate);
    }

    // ═══════════════════════════════════════════════════════════════════
    // AF-16: PAYOUT WALLET MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// PUT /api/affiliate/profile/payout-wallet
    /// Updates affiliate's payout wallet information.
    /// SECURITY: Affiliate can only update their own wallet.
    /// AF-19B: Global affiliates - queries by AffiliateAccountId only (no tenant filter).
    /// </summary>
    [HttpPut("profile/payout-wallet")]
    public async Task<ActionResult> UpdatePayoutWallet(
        [FromBody] UpdatePayoutWalletRequest request,
        CancellationToken ct)
    {
        var affiliateAccountId = await ResolveAffiliateAccountId(ct);
        if (affiliateAccountId == null)
            return Unauthorized();

        // AF-19B: Fetch affiliate account by ID only (global affiliates)
        var account = await _dbContext.AffiliateAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a =>
                a.Id == affiliateAccountId.Value &&
                !a.IsDeleted, ct);

        if (account == null)
            return NotFound(new { message = "Affiliate account not found" });

        // Store old value for audit (masked)
        var oldWallet = account.PayoutWalletNumber != null
            ? PayoutWallet.MaskWalletNumber(account.PayoutWalletNumber)
            : null;

        try
        {
            // Domain method validates and updates
            account.UpdatePayoutWallet(request.WalletNumber, request.WalletType);
            await _dbContext.SaveChangesAsync(ct);

            // AF-16B: Audit log (masked values using centralized helper)
            var newWallet = PayoutWallet.MaskWalletNumber(request.WalletNumber);
            _logger.LogInformation(
                "Affiliate {AffiliateId} updated payout wallet: {OldWallet} → {NewWallet} (Type: {Type})",
                affiliateAccountId.Value, oldWallet ?? "none", newWallet, request.WalletType);

            // AF-19B: Insert audit record (global affiliates use Guid.Empty for TenantId)
            _dbContext.AuditLogs.Add(new AuditLog
            {
                TenantId = Guid.Empty, // AF-19B: Global affiliates have no tenant
                ActionType = "AffiliateWalletUpdated",
                EntityType = "AffiliateAccount",
                EntityId = account.Id.ToString(),
                Description = $"Payout wallet updated: {oldWallet ?? "none"} → {newWallet}",
                BeforeDataJson = oldWallet,
                AfterDataJson = newWallet,
                UserId = affiliateAccountId.Value,
                TimestampUtc = DateTime.UtcNow,
                SuccessFlag = true
            });
            await _dbContext.SaveChangesAsync(ct);

            return Ok(new { message = "Payout wallet updated successfully" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // AF-27B §3: MY LANDING PAGES
    // Makes landing pages first-class affiliate-controlled assets.
    // MongoDB supplies candidates; SQL authority admits and recomposes them.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/affiliate/landing-pages
    /// Returns all affiliate-eligible products with pre-built landing page URLs.
    /// AF-27B §3: Landing pages are affiliate-controlled assets, not just public routes.
    /// MongoDB candidates are filtered and recomposed from current SQL authority.
    /// </summary>
    [HttpGet("landing-pages")]
    public async Task<ActionResult<AffiliateLandingPagesResponse>> GetLandingPages(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var affiliateAccountId = await ResolveAffiliateAccountId(ct);
        if (affiliateAccountId == null)
            return NotFound(new { message = "No affiliate account found for this user" });

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        // Resolve affiliate's referral code for URL building
        var affiliate = await _dbContext.AffiliateAccounts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.Id == affiliateAccountId.Value && !a.IsDeleted)
            .Select(a => new { a.ReferralCode })
            .FirstOrDefaultAsync(ct);

        if (affiliate == null)
            return NotFound(new { message = "Affiliate account not found" });

        var admittedDocs = await LoadSqlAdmittedAffiliateIntelligenceDocumentsAsync(ct);
        var visibleDocs = OrderAffiliateDocuments(admittedDocs, "commission_desc")
            .Skip(PaginationGuard.GetSkip(page, pageSize))
            .Take(pageSize)
            .ToList();
        var totalCount = admittedDocs.Count;

        var landingPages = visibleDocs.Select(d =>
        {
            var slug = !string.IsNullOrWhiteSpace(d.ProductSlug)
                ? d.ProductSlug : d.ProductId.ToString();
                
            // AF-27C FIX: Extract just the tenant base URL (origin) from the full product StorefrontUrl
            var storefrontBaseUrl = "";
            if (!string.IsNullOrWhiteSpace(d.StorefrontUrl) && Uri.TryCreate(d.StorefrontUrl, UriKind.Absolute, out var uri))
            {
                storefrontBaseUrl = uri.GetLeftPart(UriPartial.Authority);
            }

            var absoluteUrl = !string.IsNullOrWhiteSpace(storefrontBaseUrl) 
                ? $"{storefrontBaseUrl}/p/{affiliate.ReferralCode}/{slug}"
                : $"/p/{affiliate.ReferralCode}/{slug}";

            var storefrontProductUrl = !string.IsNullOrWhiteSpace(d.StorefrontUrl)
                ? $"{d.StorefrontUrl}?ref={affiliate.ReferralCode}"
                : null;

            return new AffiliateLandingPageItem
            {
                ProductId = d.ProductId,
                ProductName = d.ProductName,
                ProductSlug = slug,
                MainImage = d.Images?.FirstOrDefault(),
                Price = d.BasePrice,
                Currency = d.Currency,
                // AF-27C §3: Accurate decimal & UI display representations mapping
                CommissionRateDecimal = d.CommissionRate,
                CommissionRatePercentage = Math.Round(d.CommissionRate * 100m, 2),
                EstimatedCommission = d.EstimatedCommission,
                IsOutOfStock = d.IsOutOfStock,
                StoreName = d.StoreName,
                StoreSlug = d.StoreSlug,
                AbsoluteLandingUrl = absoluteUrl,
                StorefrontBaseUrl = storefrontBaseUrl,
                StorefrontProductUrl = storefrontProductUrl,
                TemplateType = d.TemplateType ?? PointOfSale.Core.Constants.LandingTemplates.ClassicV1
            };
        }).ToList();

        _logger.LogInformation(
            "[AF-27B:LandingPages] Returned {Count} landing pages for affiliate {AffiliateAccountId}",
            landingPages.Count, affiliateAccountId.Value);

        return Ok(new AffiliateLandingPagesResponse
        {
            LandingPages = landingPages,
            AffiliateCode = affiliate.ReferralCode,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // AF-31: CATEGORY DISCOVERY (PUBLIC — Landing Page)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/affiliate/discover/categories
    /// AF-31: Returns active main categories (Level 1) with product counts.
    /// Public endpoint — consumed by the landing page categories section.
    /// Data source: PostgreSQL AffiliateCategories + MongoDB product count aggregation.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("discover/categories")]
    public async Task<ActionResult<List<AffiliateCategoryDiscoveryItem>>> DiscoverCategories(
        CancellationToken ct)
    {
        // Fetch active Level 1 (main) categories from PostgreSQL
        var mainCategories = await _dbContext.AffiliateCategories
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.IsActive && c.Level == 1)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Slug,
                c.DisplayOrder
            })
            .ToListAsync(ct);

        if (mainCategories.Count == 0)
        {
            return Ok(new List<AffiliateCategoryDiscoveryItem>());
        }

        var visibleDocs = await LoadSqlAdmittedAffiliateIntelligenceDocumentsAsync(ct);
        var productCounts = visibleDocs
            .Select(document => document.AffiliateParentCategorySlug ?? document.AffiliateCategorySlug)
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .GroupBy(slug => slug!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var result = mainCategories.Select(c => new AffiliateCategoryDiscoveryItem
        {
            Id = c.Id,
            Name = c.Name,
            Slug = c.Slug,
            ProductCount = productCounts.GetValueOrDefault(c.Slug, 0),
            IsActive = true
        }).ToList();

        _logger.LogInformation(
            "[AF-31] Discover categories returned {Count} categories",
            result.Count);

        return Ok(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // AF-22: PRODUCT INTELLIGENCE (DISCOVER & LANDING)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/affiliate/discover/products?page=1&pageSize=20&search=&minCommissionRate=&inStockOnly=&storeTenantId=&categorySlug=&sort=commission_desc
    /// Returns enriched product intelligence for affiliate discovery.
    /// SQL authority admits products before Mongo hydration and final pagination.
    /// AF-30: Category filtering support.
    /// Commission-bearing product intelligence is restricted to authenticated affiliates.
    /// AF-33: Deterministic backend sorting with URL-driven sort parameter.
    /// </summary>
    [HttpGet("discover/products")]
    public async Task<ActionResult<AffiliateProductDiscoveryResponse>> DiscoverProducts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] decimal? minCommissionRate = null,
        [FromQuery] bool inStockOnly = false,
        [FromQuery] Guid? storeTenantId = null,
        [FromQuery] string? categorySlug = null,
        [FromQuery] string sort = "commission_desc",
        CancellationToken ct = default)
    {
        // AF-33: Validate sort parameter against allowlist
        var allowedSorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "commission_desc", "commission_asc", "newest", "price_desc", "price_asc", "ranking"
        };
        if (!allowedSorts.Contains(sort))
        {
            return BadRequest(new { error = "Invalid sort parameter", allowed = allowedSorts });
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Retained for API compatibility; storefront-purchasable filtering is now mandatory.
        _ = inStockOnly;

        // AF-30: Category filter (matches both main category and subcategory)
        HashSet<AffiliateAuthorityKey>? categoryProductKeys = null;
        if (!string.IsNullOrWhiteSpace(categorySlug))
        {
            categoryProductKeys = (await ResolveLiveAffiliateCategoryProductKeysAsync(categorySlug, ct)).ToHashSet();
        }

        var admittedDocs = await LoadSqlAdmittedAffiliateIntelligenceDocumentsAsync(ct);
        var filteredDocs = FilterAffiliateDocuments(
                admittedDocs,
                search,
                minCommissionRate,
                storeTenantId,
                categoryProductKeys)
            .ToList();
        var totalCount = filteredDocs.Count;
        var visibleDocs = OrderAffiliateDocuments(filteredDocs, sort)
            .Skip(PaginationGuard.GetSkip(page, pageSize))
            .Take(pageSize)
            .ToList();

        // AF-22 HARDENING: Defensive API contract (never return null, always return valid structure)
        return Ok(new AffiliateProductDiscoveryResponse
        {
            Items = visibleDocs.Select(d => new AffiliateProductDiscoveryItem
            {
                ProductId = d.ProductId,
                Name = d.ProductName,
                Description = d.Description,
                Images = d.Images ?? new List<string>(),
                Price = d.BasePrice,
                Currency = d.Currency,
                CommissionRate = d.CommissionRate,
                EstimatedCommission = d.EstimatedCommission,
                IsOutOfStock = d.IsOutOfStock,
                LowStock = d.LowStock,
                TotalAvailableStock = d.TotalAvailableStock,
                StoreName = d.StoreName,
                StoreSlug = d.StoreSlug,
                StorefrontUrl = d.StorefrontUrl,
                CategoryName = d.AffiliateCategoryName,
                CategorySlug = d.AffiliateCategorySlug
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>
    /// GET /api/affiliate/discover/products/{productId}
    /// Returns full product intelligence including variants and stock details.
    /// SQL authority is recomposed over the Mongo display projection.
    /// </summary>
    [HttpGet("discover/products/{productId:guid}")]
    public async Task<ActionResult<AffiliateProductDetailResponse>> GetProductDetail(
        Guid productId,
        CancellationToken ct)
    {
        var doc = await _mongoDbContext.AffiliateProductIntelligence
            .Find(d => d.ProductId == productId)
            .FirstOrDefaultAsync(ct);

        // AF-22 HARDENING: Defensive API contract (return 404 for missing products)
        doc = doc == null ? null : await RecomposeAffiliateIntelligenceDocumentBySqlAuthorityAsync(doc, ct);
        if (doc == null || doc.BasePrice <= 0m || doc.IsOutOfStock)
            return NotFound(new { message = "Product not found" });

        // AF-22 HARDENING: Immutable affiliate link contract (never store affiliate code in MongoDB)
        // Affiliate link = StorefrontUrl + ?ref={AffiliateCode} (appended in frontend)
        return Ok(new AffiliateProductDetailResponse
        {
            ProductId = doc.ProductId,
            TenantId = doc.TenantId,
            Name = doc.ProductName,
            Description = doc.Description,
            Images = doc.Images ?? new List<string>(),
            Price = doc.BasePrice,
            Currency = doc.Currency,
            CommissionRate = doc.CommissionRate,
            EstimatedCommission = doc.EstimatedCommission,
            IsOutOfStock = doc.IsOutOfStock,
            LowStock = doc.LowStock,
            TotalAvailableStock = doc.TotalAvailableStock,
            StoreName = doc.StoreName,
            StoreSlug = doc.StoreSlug,
            StoreLogo = doc.StoreLogo,
            StorefrontUrl = doc.StorefrontUrl,
            Variants = doc.Variants.Select(v => new AffiliateProductVariantDto
            {
                VariantId = v.VariantId,
                Name = v.Name,
                Price = v.Price,
                Stock = v.Stock
            }).ToList()
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // AF-38: PAYOUTS / WITHDRAWALS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/affiliate/payouts?page=1&amp;pageSize=20
    /// Returns available balance and payout request history.
    /// AF-38: Hybrid Withdrawal Engine — affiliate self-service.
    /// </summary>
    [HttpGet("payouts")]
    public async Task<ActionResult<AffiliatePayoutBalanceResponse>> GetPayouts(
        [FromQuery] Guid? storeTenantId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var affiliateAccountId = await ResolveAffiliateAccountId(ct);
        if (affiliateAccountId == null)
            return NotFound(new { message = "No affiliate account found for this user" });

        AffiliateAvailableBalanceResult balance;
        try
        {
            balance = storeTenantId.HasValue && storeTenantId.Value != Guid.Empty
                ? await _payoutService.GetAvailableBalanceAsync(affiliateAccountId.Value, storeTenantId.Value, ct)
                : await _payoutService.GetAvailableBalanceAsync(affiliateAccountId.Value, ct);
        }
        catch (AffiliatePayoutAuthorityException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        
        var history = storeTenantId.HasValue && storeTenantId.Value != Guid.Empty
            ? await _payoutService.GetPayoutHistoryAsync(affiliateAccountId.Value, storeTenantId.Value, page, pageSize, ct)
            : await _payoutService.GetPayoutHistoryAsync(affiliateAccountId.Value, page, pageSize, ct);

        return Ok(new AffiliatePayoutBalanceResponse
        {
            TotalApproved = balance.TotalApproved,
            TotalPaid = balance.TotalPaid,
            TotalPendingRequests = balance.TotalPendingRequests,
            AvailableForWithdrawal = balance.AvailableForWithdrawal,
            MinimumThreshold = balance.MinimumThreshold,
            CanWithdraw = balance.CanWithdraw,
            Currency = balance.Currency,
            History = history.Select(r => new AffiliatePayoutRequestDto
            {
                Id = r.Id,
                StoreTenantId = r.StoreTenantId,
                RequestedAmount = r.RequestedAmount,
                RemainingAmount = r.RemainingAmount,
                ApprovedAmount = r.ApprovedAmount,
                Status = r.Status.ToString(),
                RequestedAtUtc = r.RequestedAtUtc,
                ApprovedAtUtc = r.ApprovedAtUtc,
                PaidAtUtc = r.PaidAtUtc,
                PaymentReference = r.PaymentReference,
                Currency = r.Currency
            }).ToList()
        });
    }

    /// <summary>
    /// POST /api/affiliate/payouts/request
    /// Request a withdrawal.
    /// AF-38: RequestedAmount must be >= 100 and &lt;= AvailableForWithdrawal.
    /// </summary>
    [HttpPost("payouts/request")]
    public async Task<ActionResult<AffiliatePayoutRequestDto>> RequestPayout(
        [FromBody] AffiliateRequestPayoutBody body,
        CancellationToken ct)
    {
        var affiliateAccountId = await ResolveAffiliateAccountId(ct);
        if (affiliateAccountId == null)
            return NotFound(new { message = "No affiliate account found for this user" });

        try
        {
            AffiliatePayoutRequest request;
            
            if (body.StoreTenantId != Guid.Empty)
            {
                request = await _payoutService.RequestPayoutAsync(
                    affiliateAccountId.Value, body.Amount, body.StoreTenantId, ct);
            }
            else
            {
                request = await _payoutService.RequestPayoutAsync(
                    affiliateAccountId.Value, body.Amount, ct);
            }

            return Ok(new AffiliatePayoutRequestDto
            {
                Id = request.Id,
                StoreTenantId = request.StoreTenantId,
                RequestedAmount = request.RequestedAmount,
                RemainingAmount = request.RemainingAmount,
                Status = request.Status.ToString(),
                RequestedAtUtc = request.RequestedAtUtc,
                Currency = request.Currency
            });
        }
        catch (AffiliatePayoutAuthorityException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    //
    // AF-19B: Global affiliates - no tenant context required.
    // Affiliate identity comes from JWT claims only (affiliate_account_id).
    // ═══════════════════════════════════════════════════════════════════

    private async Task<HashSet<Guid>> BuildAffiliateOrderIdsForLedgerStatusAsync(
        Guid affiliateAccountId,
        Guid? storeTenantId,
        string status,
        CancellationToken ct)
    {
        // Performance optimization: compute status at DB level instead of loading all snapshots
        var normalizedStatus = status.Trim();
        
        // Get all ledger entries for this affiliate
        var query = _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry => entry.AffiliateAccountId == affiliateAccountId);

        if (storeTenantId.HasValue)
        {
            query = query.Where(entry => entry.StoreTenantId == storeTenantId.Value);
        }

        var entries = await query
            .Select(entry => new
            {
                entry.OrderId,
                entry.OrderItemId,
                entry.EntryType
            })
            .ToListAsync(ct);

        // Group by order and compute status
        var orderStatuses = entries
            .GroupBy(e => e.OrderId)
            .Select(orderGroup => new
            {
                OrderId = orderGroup.Key,
                Status = ComputeOrderStatusFromEntries(orderGroup.Select(e => new { e.OrderItemId, e.EntryType }))
            })
            .Where(x => string.Equals(x.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.OrderId)
            .ToHashSet();

        return orderStatuses;
    }

    private static string ComputeOrderStatusFromEntries(IEnumerable<dynamic> entries)
    {
        // Group by order item to determine per-item status
        var itemStatuses = entries
            .GroupBy(e => e.OrderItemId)
            .Select(itemGroup =>
            {
                var entryTypes = itemGroup.Select(e => (CommissionEntryType)e.EntryType).ToList();
                var hasReversal = entryTypes.Contains(CommissionEntryType.Reversed);
                
                if (hasReversal) return "Reversed";
                if (entryTypes.Contains(CommissionEntryType.Paid)) return "Paid";
                if (entryTypes.Contains(CommissionEntryType.Approved)) return "Approved";
                if (entryTypes.Contains(CommissionEntryType.Earned)) return "Earned";
                return "Pending";
            })
            .ToList();

        // Determine order-level status from item statuses
        if (itemStatuses.Count == 0) return "Pending";
        if (itemStatuses.All(s => s == "Reversed")) return "Reversed";
        if (itemStatuses.Any(s => s == "Reversed")) return "PartiallyReversed";
        if (itemStatuses.All(s => s is "Paid" or "Reversed")) return "Paid";
        if (itemStatuses.All(s => s is "Approved" or "Paid" or "Reversed")) return "Approved";
        return "Earned";
    }

    private async Task<Dictionary<Guid, AffiliateOrderLedgerSnapshot>> BuildAffiliateOrderLedgerSnapshotsAsync(
        Guid affiliateAccountId,
        Guid? storeTenantId,
        IReadOnlyCollection<Guid>? orderIds,
        CancellationToken ct)
    {
        if (orderIds is { Count: 0 })
        {
            return new Dictionary<Guid, AffiliateOrderLedgerSnapshot>();
        }

        var query = _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry => entry.AffiliateAccountId == affiliateAccountId);

        if (storeTenantId.HasValue)
        {
            query = query.Where(entry => entry.StoreTenantId == storeTenantId.Value);
        }

        if (orderIds != null)
        {
            query = query.Where(entry => orderIds.Contains(entry.OrderId));
        }

        var rows = await query
            .Select(entry => new AffiliateOrderLedgerEntryRow(
                entry.OrderId,
                entry.OrderItemId,
                entry.EntryType,
                entry.BaseAmount,
                entry.CommissionAmount,
                entry.Currency))
            .ToListAsync(ct);

        return rows
            .GroupBy(row => row.OrderId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var itemSnapshots = group
                        .GroupBy(row => row.OrderItemId)
                        .ToDictionary(
                            itemGroup => itemGroup.Key,
                            BuildAffiliateOrderItemLedgerSnapshot);

                    var earnedRows = group.Where(row => row.EntryType == CommissionEntryType.Earned).ToList();
                    var currency = NormalizeCurrency(
                        earnedRows.Select(row => row.Currency).FirstOrDefault(currency => !string.IsNullOrWhiteSpace(currency))
                        ?? group.Select(row => row.Currency).FirstOrDefault(currency => !string.IsNullOrWhiteSpace(currency)));

                    return new AffiliateOrderLedgerSnapshot(
                        PaidAmount: earnedRows.Sum(row => row.BaseAmount),
                        TotalCommission: earnedRows.Sum(row => row.CommissionAmount),
                        TotalApproved: group.Where(row => row.EntryType == CommissionEntryType.Approved).Sum(row => row.CommissionAmount),
                        TotalPaid: group.Where(row => row.EntryType == CommissionEntryType.Paid).Sum(row => row.CommissionAmount),
                        TotalReversed: group.Where(row => row.EntryType == CommissionEntryType.Reversed).Sum(row => row.CommissionAmount),
                        Currency: currency,
                        Status: DetermineLedgerOrderStatus(itemSnapshots.Values),
                        Items: itemSnapshots);
                });
    }

    private static AffiliateOrderItemLedgerSnapshot BuildAffiliateOrderItemLedgerSnapshot(
        IEnumerable<AffiliateOrderLedgerEntryRow> rows)
    {
        var rowList = rows.ToList();
        var earnedRows = rowList.Where(row => row.EntryType == CommissionEntryType.Earned).ToList();
        var hasReversal = rowList.Any(row => row.EntryType == CommissionEntryType.Reversed);
        var status = hasReversal ? "Reversed" :
            rowList.Any(row => row.EntryType == CommissionEntryType.Paid) ? "Paid" :
            rowList.Any(row => row.EntryType == CommissionEntryType.Approved) ? "Approved" :
            rowList.Any(row => row.EntryType == CommissionEntryType.Earned) ? "Earned" :
            "Pending";

        return new AffiliateOrderItemLedgerSnapshot(
            PaidAmount: earnedRows.Sum(row => row.BaseAmount),
            CommissionAmount: earnedRows.Sum(row => row.CommissionAmount),
            IsReversed: hasReversal,
            SettlementStatus: status);
    }

    private static string DetermineLedgerOrderStatus(IEnumerable<AffiliateOrderItemLedgerSnapshot> items)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0) return "Pending";
        
        // Check for all-reversed first
        if (itemList.All(item => item.IsReversed)) return "Reversed";
        
        // Check for partial reversals BEFORE checking mixed states that allow reversals
        // This ensures mixed reversed+paid/approved orders return "PartiallyReversed"
        if (itemList.Any(item => item.IsReversed)) return "PartiallyReversed";
        
        // Now check non-reversed item statuses (no reversals at this point)
        if (itemList.All(item => item.SettlementStatus == "Paid")) return "Paid";
        if (itemList.All(item => item.SettlementStatus is "Approved" or "Paid")) return "Approved";
        
        return "Earned";
    }

    private async Task<AffiliateLedgerTotals> BuildAffiliateLedgerTotalsAsync(
        Guid affiliateAccountId,
        Guid? storeTenantId,
        CancellationToken ct)
    {
        var query = _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry => entry.AffiliateAccountId == affiliateAccountId);

        if (storeTenantId.HasValue)
        {
            query = query.Where(entry => entry.StoreTenantId == storeTenantId.Value);
        }

        var rows = await query
            .GroupBy(entry => entry.EntryType)
            .Select(group => new
            {
                EntryType = group.Key,
                Amount = group.Sum(entry => entry.CommissionAmount)
            })
            .ToListAsync(ct);

        var totalOrders = await query
            .Where(entry => entry.EntryType == CommissionEntryType.Earned)
            .Select(entry => entry.OrderId)
            .Distinct()
            .CountAsync(ct);

        var lastCommissionAtUtc = await query
            .Select(entry => (DateTime?)entry.RecordedAtUtc)
            .MaxAsync(ct);

        return new AffiliateLedgerTotals(
            TotalEarned: rows.Where(row => row.EntryType == CommissionEntryType.Earned).Sum(row => row.Amount),
            TotalApproved: rows.Where(row => row.EntryType == CommissionEntryType.Approved).Sum(row => row.Amount),
            TotalPaid: rows.Where(row => row.EntryType == CommissionEntryType.Paid).Sum(row => row.Amount),
            TotalReversed: rows.Where(row => row.EntryType == CommissionEntryType.Reversed).Sum(row => row.Amount),
            TotalOrders: totalOrders,
            LastCommissionAtUtc: lastCommissionAtUtc);
    }

    private async Task<List<AffiliateEarningsSummaryDto>> BuildAffiliateLedgerEarningsSummaryAsync(
        Guid affiliateAccountId,
        Guid? storeTenantId,
        CancellationToken ct)
    {
        var query = _dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry => entry.AffiliateAccountId == affiliateAccountId);

        if (storeTenantId.HasValue)
        {
            query = query.Where(entry => entry.StoreTenantId == storeTenantId.Value);
        }

        var rows = await query
            .GroupBy(entry => new { entry.Currency, entry.EntryType })
            .Select(group => new
            {
                Currency = group.Key.Currency,
                EntryType = group.Key.EntryType,
                Amount = group.Sum(entry => entry.CommissionAmount)
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(row => NormalizeCurrency(row.Currency))
            .Select(group =>
            {
                var earned = group.Where(row => row.EntryType == CommissionEntryType.Earned).Sum(row => row.Amount);
                var reversed = group.Where(row => row.EntryType == CommissionEntryType.Reversed).Sum(row => row.Amount);

                return new AffiliateEarningsSummaryDto
                {
                    Currency = group.Key,
                    TotalEarned = earned,
                    TotalApproved = group.Where(row => row.EntryType == CommissionEntryType.Approved).Sum(row => row.Amount),
                    TotalPaid = group.Where(row => row.EntryType == CommissionEntryType.Paid).Sum(row => row.Amount),
                    TotalReversed = reversed,
                    NetEarnings = earned - reversed
                };
            })
            .OrderBy(summary => summary.Currency)
            .ToList();
    }

    private static string NormalizeCurrency(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency)
            ? "UNKNOWN"
            : currency.Trim().ToUpperInvariant();
    }

    private async Task<List<AffiliateProductIntelligenceDocument>> LoadSqlAdmittedAffiliateIntelligenceDocumentsAsync(
        CancellationToken ct)
    {
        var authorityKeys = await (
                from policy in _dbContext.AffiliateProductPolicies.AsNoTracking().IgnoreQueryFilters()
                join category in _dbContext.AffiliateCategories.AsNoTracking().IgnoreQueryFilters()
                    on policy.AffiliateCategoryId equals (Guid?)category.Id
                join product in _dbContext.Products.AsNoTracking().IgnoreQueryFilters()
                    on new { policy.TenantId, policy.ProductId }
                    equals new { product.TenantId, ProductId = product.Id }
                join tenant in _dbContext.Tenants.AsNoTracking().IgnoreQueryFilters()
                    on policy.TenantId equals tenant.Id
                where policy.IsEnabled &&
                      !policy.IsDeleted &&
                      category.IsActive &&
                      !category.IsDeleted &&
                      ((category.Level == 1 && category.ParentId == null) ||
                       (category.Level == 2 &&
                        category.ParentId != null &&
                        _dbContext.AffiliateCategories.IgnoreQueryFilters().Any(parent =>
                            parent.Id == category.ParentId.Value &&
                            parent.Level == 1 &&
                            parent.ParentId == null &&
                            parent.IsActive &&
                            !parent.IsDeleted))) &&
                      !product.IsDeleted &&
                      product.IsActive &&
                      product.IsOnlineVisible &&
                      product.IsSearchable &&
                      product.PublishStatus == PublishStatus.Published &&
                      tenant.IsActive &&
                      tenant.Status == TenantStatus.Active &&
                      tenant.DeletedAtUtc == null
                select new { policy.TenantId, policy.ProductId })
            .Distinct()
            .ToListAsync(ct);

        if (authorityKeys.Count == 0)
        {
            return [];
        }

        var keySet = authorityKeys
            .Select(key => (key.TenantId, key.ProductId))
            .ToHashSet();
        var productIds = authorityKeys
            .Select(key => key.ProductId)
            .Distinct()
            .ToList();

        // MongoDB is hydration/search metadata only. It receives the SQL-admitted
        // identity set and never establishes a page boundary or total.
        var candidates = await _mongoDbContext.AffiliateProductIntelligence
            .Find(Builders<AffiliateProductIntelligenceDocument>.Filter.In(
                document => document.ProductId,
                productIds))
            .ToListAsync(ct);
        candidates = candidates
            .Where(candidate => keySet.Contains((candidate.TenantId, candidate.ProductId)))
            .GroupBy(candidate => (candidate.TenantId, candidate.ProductId))
            .Select(group => group
                .OrderByDescending(candidate => candidate.LastUpdatedUtc)
                .ThenBy(candidate => candidate.Id)
                .First())
            .ToList();

        return await RecomposeAffiliateIntelligenceDocumentsBySqlAuthorityAsync(candidates, ct);
    }

    internal static IEnumerable<AffiliateProductIntelligenceDocument> FilterAffiliateDocuments(
        IEnumerable<AffiliateProductIntelligenceDocument> documents,
        string? search,
        decimal? minCommissionRate,
        Guid? storeTenantId,
        IReadOnlySet<AffiliateAuthorityKey>? categoryProductKeys)
    {
        var searchTerm = search?.Trim();
        return documents.Where(document =>
            document.BasePrice > 0m &&
            !document.IsOutOfStock &&
            (!storeTenantId.HasValue || document.TenantId == storeTenantId.Value) &&
            (string.IsNullOrWhiteSpace(searchTerm) ||
             document.ProductName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) &&
            (!minCommissionRate.HasValue || document.CommissionRate >= minCommissionRate.Value) &&
            (categoryProductKeys == null ||
             categoryProductKeys.Contains(new AffiliateAuthorityKey(document.TenantId, document.ProductId))));
    }

    internal static IOrderedEnumerable<AffiliateProductIntelligenceDocument> OrderAffiliateDocuments(
        IEnumerable<AffiliateProductIntelligenceDocument> documents,
        string sort)
    {
        var ordered = sort.ToLowerInvariant() switch
        {
            "commission_asc" => documents.OrderBy(document => document.CommissionRate),
            "newest" => documents.OrderByDescending(document => document.LastUpdatedUtc),
            "price_desc" => documents.OrderByDescending(document => document.BasePrice),
            "price_asc" => documents.OrderBy(document => document.BasePrice),
            "ranking" => documents.OrderByDescending(document => document.RankingScore),
            _ => documents.OrderByDescending(document => document.CommissionRate)
        };

        return ordered
            .ThenBy(document => document.TenantId)
            .ThenBy(document => document.ProductId);
    }

    private async Task<List<AffiliateProductIntelligenceDocument>> RecomposeAffiliateIntelligenceDocumentsBySqlAuthorityAsync(
        IReadOnlyCollection<AffiliateProductIntelligenceDocument> candidates,
        CancellationToken ct)
    {
        var visible = new List<AffiliateProductIntelligenceDocument>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var recomposed = await RecomposeAffiliateIntelligenceDocumentBySqlAuthorityAsync(candidate, ct);
            if (recomposed != null)
            {
                visible.Add(recomposed);
            }
        }

        return visible;
    }

    private async Task<AffiliateProductIntelligenceDocument?> RecomposeAffiliateIntelligenceDocumentBySqlAuthorityAsync(
        AffiliateProductIntelligenceDocument candidate,
        CancellationToken ct)
    {
        if (candidate.TenantId == Guid.Empty || candidate.ProductId == Guid.Empty)
        {
            return null;
        }

        var policyAuthority = await _dbContext.AffiliateProductPolicies
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Join(
                _dbContext.AffiliateCategories.AsNoTracking().IgnoreQueryFilters(),
                policy => policy.AffiliateCategoryId,
                category => (Guid?)category.Id,
                (policy, category) => new { Policy = policy, Category = category })
            .Where(item =>
                item.Policy.TenantId == candidate.TenantId &&
                item.Policy.ProductId == candidate.ProductId &&
                item.Policy.IsEnabled &&
                !item.Policy.IsDeleted &&
                item.Category.IsActive &&
                !item.Category.IsDeleted)
            .Select(item => new
            {
                item.Category.Id,
                item.Category.Name,
                item.Category.Slug,
                item.Category.Level,
                item.Category.ParentId,
                item.Policy.CommissionRate,
                item.Policy.MaxCommissionPerItem,
                item.Policy.TemplateType
            })
            .FirstOrDefaultAsync(ct);

        if (policyAuthority == null)
        {
            return null;
        }

        string? parentCategoryName = null;
        string? parentCategorySlug = null;
        if (policyAuthority.Level == 2 && policyAuthority.ParentId.HasValue)
        {
            var parent = await _dbContext.AffiliateCategories
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(item =>
                    item.Id == policyAuthority.ParentId.Value &&
                    item.Level == 1 &&
                    item.IsActive &&
                    !item.IsDeleted)
                .Select(item => new { item.Name, item.Slug })
                .FirstOrDefaultAsync(ct);
            if (parent == null)
            {
                return null;
            }

            parentCategoryName = parent.Name;
            parentCategorySlug = parent.Slug;
        }
        else if (policyAuthority.Level != 1 || policyAuthority.ParentId.HasValue)
        {
            return null;
        }

        var product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            _dbContext,
            new ProductDocument
            {
                TenantId = candidate.TenantId,
                ProductId = candidate.ProductId
            },
            candidate.TenantId,
            ct);

        if (product is not { IsActive: true, IsOnlineVisible: true, IsSearchable: true } ||
            !product.TryResolveAffiliatePublicPricing(out var pricing) ||
            !pricing.InStock)
        {
            return null;
        }

        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.Id == candidate.TenantId && item.IsActive && item.DeletedAtUtc == null)
            .Select(item => new { item.Name, item.Slug, item.Currency })
            .FirstOrDefaultAsync(ct);

        if (tenant == null)
        {
            return null;
        }

        candidate.ProductName = product.Name;
        candidate.ProductSlug = product.Slug ?? candidate.ProductId.ToString();
        candidate.Description = product.Description ?? string.Empty;
        candidate.Images = product.Media
            .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(media => media.Position)
            .Select(media => media.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToList();
        candidate.BasePrice = pricing.Price ?? pricing.MinVariantPrice ?? 0m;
        var estimatedCommission = candidate.BasePrice * policyAuthority.CommissionRate;
        candidate.CommissionRate = policyAuthority.CommissionRate;
        candidate.EstimatedCommission = policyAuthority.MaxCommissionPerItem.HasValue
            ? Math.Min(estimatedCommission, policyAuthority.MaxCommissionPerItem.Value)
            : estimatedCommission;
        candidate.TemplateType = policyAuthority.TemplateType;
        candidate.Currency = tenant.Currency ?? candidate.Currency;
        candidate.TotalAvailableStock = pricing.TotalAvailableStock;
        candidate.IsOutOfStock = !pricing.InStock;
        candidate.StoreName = tenant.Name;
        candidate.StoreSlug = tenant.Slug ?? candidate.StoreSlug;
        candidate.AffiliateCategoryId = policyAuthority.Id;
        candidate.AffiliateCategoryName = policyAuthority.Name;
        candidate.AffiliateCategorySlug = policyAuthority.Slug;
        candidate.AffiliateParentCategoryName = parentCategoryName;
        candidate.AffiliateParentCategorySlug = parentCategorySlug;
        return candidate;
    }

    private async Task<List<AffiliateAuthorityKey>> ResolveLiveAffiliateCategoryProductKeysAsync(
        string categorySlug,
        CancellationToken cancellationToken)
    {
        var category = await _dbContext.AffiliateCategories
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item =>
                item.Slug == categorySlug.Trim() &&
                item.IsActive &&
                !item.IsDeleted)
            .Select(item => new { item.Id, item.Level, item.ParentId })
            .FirstOrDefaultAsync(cancellationToken);
        if (category == null)
        {
            return [];
        }

        List<Guid> categoryIds;
        if (category.Level == 1 && !category.ParentId.HasValue)
        {
            categoryIds = await _dbContext.AffiliateCategories
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(item =>
                    (item.Id == category.Id || item.ParentId == category.Id) &&
                    item.IsActive &&
                    !item.IsDeleted)
                .Select(item => item.Id)
                .ToListAsync(cancellationToken);
        }
        else if (category.Level == 2 && category.ParentId.HasValue &&
                 await _dbContext.AffiliateCategories
                     .AsNoTracking()
                     .IgnoreQueryFilters()
                     .AnyAsync(parent =>
                         parent.Id == category.ParentId.Value &&
                         parent.Level == 1 &&
                         parent.IsActive &&
                         !parent.IsDeleted,
                         cancellationToken))
        {
            categoryIds = [category.Id];
        }
        else
        {
            return [];
        }

        var keys = await _dbContext.AffiliateProductPolicies
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(policy =>
                policy.IsEnabled &&
                !policy.IsDeleted &&
                policy.AffiliateCategoryId.HasValue &&
                categoryIds.Contains(policy.AffiliateCategoryId.Value))
            .Select(policy => new { policy.TenantId, policy.ProductId })
            .Distinct()
            .ToListAsync(cancellationToken);

        return keys
            .Select(key => new AffiliateAuthorityKey(key.TenantId, key.ProductId))
            .ToList();
    }

    internal readonly record struct AffiliateAuthorityKey(Guid TenantId, Guid ProductId);

    private async Task<Guid?> ResolveAffiliateAccountId(CancellationToken ct)
    {
        // AF-19B: Read affiliate_account_id directly from JWT (global affiliates)
        var affiliateAccountClaim = User.FindFirst("affiliate_account_id")?.Value;
        if (!string.IsNullOrEmpty(affiliateAccountClaim) && Guid.TryParse(affiliateAccountClaim, out var affiliateId))
        {
            return affiliateId;
        }

        return null;
    }

    private sealed record AffiliateLedgerTotals(
        decimal TotalEarned,
        decimal TotalApproved,
        decimal TotalPaid,
        decimal TotalReversed,
        int TotalOrders,
        DateTime? LastCommissionAtUtc)
    {
        public decimal NetEarnings => TotalEarned - TotalReversed;
    }

    private sealed record AffiliateOrderLedgerEntryRow(
        Guid OrderId,
        Guid OrderItemId,
        CommissionEntryType EntryType,
        decimal BaseAmount,
        decimal CommissionAmount,
        string Currency);

    private sealed record AffiliateOrderLedgerSnapshot(
        decimal PaidAmount,
        decimal TotalCommission,
        decimal TotalApproved,
        decimal TotalPaid,
        decimal TotalReversed,
        string Currency,
        string Status,
        IReadOnlyDictionary<Guid, AffiliateOrderItemLedgerSnapshot> Items);

    private sealed record AffiliateOrderItemLedgerSnapshot(
        decimal PaidAmount,
        decimal CommissionAmount,
        bool IsReversed,
        string SettlementStatus);
}

// ═══════════════════════════════════════════════════════════════════
// DTOs — Response shapes (read-only, no business logic)
// ═══════════════════════════════════════════════════════════════════

public class AffiliateDashboardResponse
{
    /// <summary>
    /// Earnings broken down by currency.
    /// Prevents collapsing USD + EUR + other currencies into a single meaningless total.
    /// </summary>
    public List<AffiliateEarningsSummaryDto> EarningsByCurrency { get; set; } = new();
    
    public int TotalOrders { get; set; }
    public DateTime? LastCommissionAtUtc { get; set; }
    
    // DEPRECATED: Legacy single-currency fields kept for backward compatibility
    // These aggregate across all currencies to maintain backward compatibility for multi-currency affiliates
    [Obsolete("Use EarningsByCurrency instead. This field collapses multiple currencies incorrectly.")]
    public decimal TotalEarned => EarningsByCurrency?.Sum(e => e.TotalEarned) ?? 0m;
    
    [Obsolete("Use EarningsByCurrency instead. This field collapses multiple currencies incorrectly.")]
    public decimal TotalApproved => EarningsByCurrency?.Sum(e => e.TotalApproved) ?? 0m;
    
    [Obsolete("Use EarningsByCurrency instead. This field collapses multiple currencies incorrectly.")]
    public decimal TotalPaid => EarningsByCurrency?.Sum(e => e.TotalPaid) ?? 0m;
    
    [Obsolete("Use EarningsByCurrency instead. This field collapses multiple currencies incorrectly.")]
    public decimal TotalReversed => EarningsByCurrency?.Sum(e => e.TotalReversed) ?? 0m;
    
    [Obsolete("Use EarningsByCurrency instead. This field collapses multiple currencies incorrectly.")]
    public decimal NetEarnings => EarningsByCurrency?.Sum(e => e.NetEarnings) ?? 0m;
}

public class AffiliateOrdersResponse
{
    public List<AffiliateOrderDto> Orders { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public class AffiliateOrderDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Commission settlement status: Earned | PartiallyReversed | Reversed | Approved | Paid
    /// </summary>
    public string Status { get; set; } = string.Empty;
    
    // ═══════════════════════════════════════════════════════════════════
    // AF-21: ORDER LIFECYCLE STATUS FIELDS
    // ═══════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// AF-21: Order status from POS Admin (Pending, Confirmed, Completed, Cancelled, etc.)
    /// </summary>
    public string OrderStatus { get; set; } = string.Empty;
    
    /// <summary>
    /// AF-21: Fulfillment status from POS Admin (Created, Confirmed, Packed, OutForDelivery, Delivered, Completed, etc.)
    /// </summary>
    public string FulfillmentStatus { get; set; } = string.Empty;
    
    /// <summary>
    /// AF-21: Payment method code (COD, Card, Wallet, etc.)
    /// </summary>
    public string PaymentMethod { get; set; } = string.Empty;
    
    /// <summary>
    /// AF-21: Total amount paid by customer (order total, NOT commission amount)
    /// </summary>
    public decimal PaidAmount { get; set; }
    
    // ═══════════════════════════════════════════════════════════════════
    
    public decimal TotalCommission { get; set; }
    public decimal TotalApproved { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalReversed { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public List<AffiliateOrderItemDto> Items { get; set; } = new();
}

public class AffiliateOrderItemDto
{
    public Guid OrderItemId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal CommissionAmount { get; set; }
    public bool IsReversed { get; set; }
    public string SettlementStatus { get; set; } = "Earned";
}

public class AffiliateEarningsSummaryDto
{
    public string Currency { get; set; } = string.Empty;
    public decimal TotalEarned { get; set; }
    public decimal TotalApproved { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalReversed { get; set; }
    public decimal NetEarnings { get; set; }
}

public class AffiliateRegisterRequest
{
    public string? DisplayName { get; set; }
}

public class AffiliateProfileResponse
{
    public Guid AffiliateAccountId { get; set; }
    public string ReferralCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    
    // AF-16: Payout Wallet
    public string? PayoutWalletNumber { get; set; }
    public string? PayoutWalletType { get; set; }
    public DateTime? PayoutWalletUpdatedAtUtc { get; set; }
}

// AF-16: Update Payout Wallet Request
public class UpdatePayoutWalletRequest
{
    public string WalletNumber { get; set; } = string.Empty;
    public string WalletType { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════════════════════════════
// AF-22: PRODUCT INTELLIGENCE DTOs
// ═══════════════════════════════════════════════════════════════════

public class AffiliateProductDiscoveryResponse
{
    public List<AffiliateProductDiscoveryItem> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public class AffiliateProductDiscoveryItem
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Images { get; set; } = new();
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal CommissionRate { get; set; }
    public decimal EstimatedCommission { get; set; }
    public bool IsOutOfStock { get; set; }
    public bool LowStock { get; set; }
    public int? TotalAvailableStock { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string StoreSlug { get; set; } = string.Empty;
    public string StorefrontUrl { get; set; } = string.Empty;
    public string? CategoryName { get; set; } // AF-30
    public string? CategorySlug { get; set; } // AF-30
}

public class AffiliateProductDetailResponse
{
    public Guid ProductId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Images { get; set; } = new();
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal CommissionRate { get; set; }
    public decimal EstimatedCommission { get; set; }
    public bool IsOutOfStock { get; set; }
    public bool LowStock { get; set; }
    public int? TotalAvailableStock { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string StoreSlug { get; set; } = string.Empty;
    public string StoreLogo { get; set; } = string.Empty;
    public string StorefrontUrl { get; set; } = string.Empty;
    public List<AffiliateProductVariantDto> Variants { get; set; } = new();
}

public class AffiliateProductVariantDto
{
    public Guid VariantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
}

// ═══════════════════════════════════════════════════════════════════
// AF-27B §3: LANDING PAGE DTOs
// ═══════════════════════════════════════════════════════════════════

public class AffiliateLandingPagesResponse
{
    public List<AffiliateLandingPageItem> LandingPages { get; set; } = new();
    public string AffiliateCode { get; set; } = string.Empty;
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public class AffiliateLandingPageItem
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductSlug { get; set; } = string.Empty;
    public string? MainImage { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal CommissionRateDecimal { get; set; }
    public decimal CommissionRatePercentage { get; set; }
    public decimal EstimatedCommission { get; set; }
    public bool IsOutOfStock { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string StoreSlug { get; set; } = string.Empty;

    /// <summary>
    /// Backend-driven absolute URL to inherently resolve correct tenant subdomain.
    /// AF-27C §1: Fixes tenant boundary leak. Never generated by frontend window.location.
    /// </summary>
    public string AbsoluteLandingUrl { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for the store, resolving the correct tenant subdomain structure.
    /// </summary>
    public string StorefrontBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Direct storefront product link with ?ref= for attribution fallback
    /// </summary>
    public string? StorefrontProductUrl { get; set; }

    /// <summary>
    /// AF-28: Current template type assigned to this product's landing page.
    /// Affiliates can override this by appending ?template= to their landing URL.
    /// </summary>
    public string TemplateType { get; set; } = "Classic:v1";
}

// ═══════════════════════════════════════════════════════════════════
// AF-31: CATEGORY DISCOVERY DTOs
// ═══════════════════════════════════════════════════════════════════

public class AffiliateCategoryDiscoveryItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int ProductCount { get; set; }
    public bool IsActive { get; set; }
}

// ═══════════════════════════════════════════════════════════════════
// AF-38: PAYOUT DTOs
// ═══════════════════════════════════════════════════════════════════

public class AffiliatePayoutBalanceResponse
{
    public decimal TotalApproved { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalPendingRequests { get; set; }
    public decimal AvailableForWithdrawal { get; set; }
    public decimal MinimumThreshold { get; set; }
    public bool CanWithdraw { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<AffiliatePayoutRequestDto> History { get; set; } = new();
}

public class AffiliatePayoutRequestDto
{
    public Guid Id { get; set; }
    public Guid StoreTenantId { get; set; }
    public decimal RequestedAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public string? PaymentReference { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class AffiliateRequestPayoutBody
{
    public decimal Amount { get; set; }
    public Guid StoreTenantId { get; set; }
}
