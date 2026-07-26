using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Helpers;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Affiliate;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Middleware;

/// <summary>
/// AF-8 / AF-12 / AF-15 / AF-19: Affiliate Referral Click Tracking &amp; Affiliate Mode Activation.
///
/// Intercepts storefront requests with ?ref=CODE query parameter and:
/// 1. AF-19: Resolves AffiliatePromotionLink or AffiliateAccount by ReferralCode GLOBALLY (no tenant filter)
/// 2. Generates a session cookie (AFF_SESSION) if missing
/// 3. Publishes AffiliateLinkClickedEvent (async, fire-and-forget)
/// 4. AF-12: Activates Affiliate Mode (request-scoped)
///
/// AF-19 GLOBAL MARKETPLACE:
/// - Affiliates are global (no tenant binding)
/// - ReferralCode lookup is global (no tenant filter)
/// - Affiliate can promote products from ANY store
/// - StoreTenantId tracks which store the sale belongs to
///
/// DUAL COMPATIBILITY:
/// - If PromotionLink found → v2 flow (sets PromotionLinkId, AffiliateUserId, AffiliateAccountId)
/// - If PromotionLink NOT found → v1 flow (sets AffiliateAccountId only)
/// - Emits telemetry counters: affiliate.v2.used / affiliate.v1.fallback
///
/// RULES:
/// - NO database writes inside middleware — event-driven only
/// - NO PII stored: IP is SHA-256 hashed
/// - Cookie-based session tracking survives page refresh
/// - Only runs on storefront paths (not admin/api paths)
/// - Silently skips on any error — never blocks the request
/// - AF-12: Affiliate Mode is request-scoped only (NOT persisted)
/// </summary>
public class AffiliateReferralMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AffiliateReferralMiddleware> _logger;
    private const string CookieName = "AFF_SESSION";
    private const string AttributionCookieName = "AFF_ATTR";
    private const string RefQueryParam = "ref";
    private const string RefHeaderName = "X-Affiliate-Ref";

    public AffiliateReferralMiddleware(RequestDelegate next, ILogger<AffiliateReferralMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        TenantContext tenantContext,
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        IAffiliateModeMetricsService metricsService,
        IAffiliateAttributionEvidenceTokenService attributionEvidenceTokens)
    {
        try
        {
            var tenantId = tenantContext.TenantId;
            if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
            {
                await _next(context);
                return;
            }
            var currentTenantId = tenantId.Value;

            // AF-27E: Check THREE sources for referral code:
            // 1. Query param ?ref= (legacy flow)
            // 2. Header X-Affiliate-Ref (SPA flow)
            // 3. Route param /p/{referralCode}/{slug} or /api/storefront/landing/{referralCode}/{slug} (landing flow)
            // Priority: query param > header > route param (direct link takes precedence)
            string? rawRef = null;
            string? refSource = null;
            AffiliateAttributionEvidence? protectedEvidence = null;

            if (context.Request.Cookies.TryGetValue(AttributionCookieName, out var existingAttributionRef) &&
                !string.IsNullOrWhiteSpace(existingAttributionRef))
            {
                if (attributionEvidenceTokens.TryValidate(
                        existingAttributionRef,
                        currentTenantId,
                        out var validatedEvidence))
                {
                    protectedEvidence = validatedEvidence;
                    rawRef = validatedEvidence.ReferralCode;
                    refSource = "Cookie:first-click-protected";
                }
                else
                {
                    context.Response.Cookies.Delete(AttributionCookieName);
                    _logger.LogWarning(
                        "[Affiliate:Referral] Rejected tampered, foreign-tenant, or expired attribution cookie | TenantId: {TenantId}",
                        tenantId);
                }
            }

            if (rawRef == null &&
                context.Request.Query.TryGetValue(RefQueryParam, out var refCode) &&
                !string.IsNullOrWhiteSpace(refCode))
            {
                rawRef = refCode.ToString();
                refSource = "QueryParam";
            }
            else if (rawRef == null &&
                     context.Request.Headers.TryGetValue(RefHeaderName, out var refHeader) &&
                     !string.IsNullOrWhiteSpace(refHeader))
            {
                rawRef = refHeader.ToString();
                refSource = "Header";
            }
            else if (rawRef == null)
            {
                // AF-27E: Extract referralCode from route parameter
                var path = context.Request.Path.Value;
                if (!string.IsNullOrEmpty(path))
                {
                    // Match: /p/{referralCode}/{slug}
                    if (path.StartsWith("/p/", StringComparison.OrdinalIgnoreCase))
                    {
                        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                        if (segments.Length >= 2)
                        {
                            rawRef = segments[1]; // segments[0] = "p", segments[1] = referralCode
                            refSource = "RouteParam:/p/";
                        }
                    }
                    // Match: /api/storefront/landing/{referralCode}/{slug}
                    else if (path.StartsWith("/api/storefront/landing/", StringComparison.OrdinalIgnoreCase))
                    {
                        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                        if (segments.Length >= 4)
                        {
                            rawRef = segments[3]; // segments[0] = "api", segments[1] = "storefront", segments[2] = "landing", segments[3] = referralCode
                            refSource = "RouteParam:/api/storefront/landing/";
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(rawRef))
            {
                await _next(context);
                return;
            }

            var referralCode = rawRef!.Trim().ToUpperInvariant();

            context.RequestAborted.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "[AF-27E:AffiliateMW] Ref found: {Ref} | Source: {Source} | Path: {Path}",
                referralCode, refSource, context.Request.Path);

            // ═══════════════════════════════════════════════════════════════════
            // AF-19: GLOBAL MARKETPLACE - Lookup affiliate by ReferralCode globally
            // No tenant filter - affiliates can promote any store
            // ═══════════════════════════════════════════════════════════════════
            
            _logger.LogInformation(
                "[AF-19:GlobalLookup] V2 query starting | ReferralCode: {Ref} | CurrentTenant: {TenantId}",
                referralCode, tenantId);
            
            // Storefront Admission: promotion links are URL-facing store entries.
            // The referral code is globally unique, but a valid storefront entry must
            // match the currently resolved tenant and current product lifecycle.
            var promotionLink = await dbContext.AffiliatePromotionLinks
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(l => l.ReferralCode == referralCode
                         && l.TenantId == currentTenantId
                         && l.IsActive
                         && !l.IsDeleted)
                .Select(l => new
                {
                    l.Id,
                    l.AffiliateUserId,
                    l.AffiliateAccountId,
                    l.ProductId,
                    l.ReferralCode,
                    l.TenantId
                })
                .FirstOrDefaultAsync(context.RequestAborted);

            _logger.LogInformation(
                "[AF-19:GlobalLookup] V2 query result | Found: {Found} | LinkId: {LinkId}",
                promotionLink != null, promotionLink?.Id);

            if (promotionLink != null)
            {
                var product = await dbContext.Products
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        p => p.Id == promotionLink.ProductId &&
                             p.TenantId == currentTenantId &&
                             !p.IsDeleted,
                        context.RequestAborted);

                if (product == null ||
                    !await IsProductCurrentlyAffiliateSelectableAsync(dbContext, product, context.RequestAborted))
                {
                    if (protectedEvidence != null)
                    {
                        context.Response.Cookies.Delete(AttributionCookieName);
                    }

                    _logger.LogWarning(
                        "[Affiliate:Referral] Promotion link rejected by current storefront admission | LinkId: {LinkId} | TenantId: {TenantId} | ProductId: {ProductId} | Ref: {Ref}",
                        promotionLink.Id,
                    tenantId,
                        promotionLink.ProductId,
                        referralCode);
                    await _next(context);
                    return;
                }

                // V2 flow: PromotionLink found and admitted by current store/product authority.
                _logger.LogInformation(
                    "[AF-19:V2] Promotion link resolved | LinkId: {LinkId} | AffiliateUserId: {AffiliateUserId} | " +
                    "AffiliateAccountId: {AffiliateAccountId} | ProductId: {ProductId} | LinkStore: {LinkStore} | " +
                    "OrderStore: {OrderStore} | CrossStore: {CrossStore}",
                    promotionLink.Id, promotionLink.AffiliateUserId,
                    promotionLink.AffiliateAccountId, promotionLink.ProductId, promotionLink.TenantId, 
                    tenantId, promotionLink.TenantId != tenantId);

                // Resolve AffiliateAccountId — either from the link directly,
                // or fall back to looking up the AffiliateUser's account
                var affiliateAccountId = promotionLink.AffiliateAccountId;
                if (!affiliateAccountId.HasValue)
                {
                    // Resolve via AffiliateUser → AffiliateAccount bridge
                    var userAccount = await dbContext.AffiliateUsers
                        .AsNoTracking()
                        .Where(u => u.Id == promotionLink.AffiliateUserId && !u.IsDeleted)
                        .Select(u => new { u.AffiliateAccountId })
                        .FirstOrDefaultAsync(context.RequestAborted);

                    affiliateAccountId = userAccount?.AffiliateAccountId;
                }

                if (!affiliateAccountId.HasValue)
                {
                    _logger.LogWarning(
                        "[AF-15:V2] PromotionLink found but no AffiliateAccountId resolvable | LinkId: {LinkId} | AffiliateUserId: {AffiliateUserId}",
                        promotionLink.Id, promotionLink.AffiliateUserId);
                    await _next(context);
                    return;
                }

                if (protectedEvidence != null &&
                    (protectedEvidence.PromotionLinkId != promotionLink.Id ||
                     protectedEvidence.AffiliateAccountId != affiliateAccountId.Value))
                {
                    context.Response.Cookies.Delete(AttributionCookieName);
                    _logger.LogWarning(
                        "[Affiliate:Referral] Protected promotion-link evidence no longer matches SQL authority | TenantId: {TenantId} | PromotionLinkId: {PromotionLinkId}",
                        tenantId,
                        promotionLink.Id);
                    await _next(context);
                    return;
                }

                var affiliateIdentity = await dbContext.AffiliateAccounts
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(a => a.Id == affiliateAccountId.Value && a.Status == AffiliateStatus.Active && !a.IsDeleted)
                    .Select(a => new { a.DisplayName })
                    .FirstOrDefaultAsync(context.RequestAborted);

                if (affiliateIdentity == null)
                {
                    if (protectedEvidence != null)
                    {
                        context.Response.Cookies.Delete(AttributionCookieName);
                    }

                    _logger.LogWarning(
                        "[AF-15:V2] Affiliate account missing or inactive after PromotionLink resolution | AffiliateAccountId: {AffiliateAccountId}",
                        affiliateAccountId.Value);
                    await _next(context);
                    return;
                }

                // Set v2 affiliate mode (includes v1 AffiliateAccountId for compatibility)
                AffiliateModeResolver.SetAffiliateModeV2(
                    context,
                    affiliateAccountId.Value,
                    promotionLink.ReferralCode, // AF-27E
                    promotionLink.Id,
                    promotionLink.AffiliateUserId,
                    currentTenantId,
                    affiliateIdentity.DisplayName);
                EnsureAttributionCookie(
                    context,
                    attributionEvidenceTokens,
                    currentTenantId,
                    affiliateAccountId.Value,
                    promotionLink.ReferralCode,
                    promotionLink.Id);

                // Session & click tracking
                var sessionId = EnsureSession(context);
                Guid? productId = promotionLink.ProductId;
                var ipHash = HashIp(context.Connection.RemoteIpAddress?.ToString());
                var userAgent = HashUserAgent(context.Request.Headers.UserAgent.ToString());

                var clickEvent = new AffiliateLinkClickedEvent
                {
                    TenantId = tenantId,
                    AffiliateAccountId = affiliateAccountId.Value,
                    ReferralCode = promotionLink.ReferralCode,
                    ProductId = productId,
                    SessionId = sessionId,
                    IpHash = ipHash,
                    UserAgent = userAgent,
                    OccurredAtUtc = DateTime.UtcNow
                };

                await eventPublisher.PublishAsync(clickEvent, context.RequestAborted);

                // AF-12: Record metric
                await metricsService.RecordAffiliateModeActivationAsync(
                    currentTenantId,
                    affiliateAccountId.Value,
                    promotionLink.ReferralCode,
                    context.RequestAborted);

                _logger.LogInformation(
                    "[AF-19:V2] Affiliate Mode activated (v2) | AffiliateAccountId: {AffiliateAccountId} | " +
                    "PromotionLinkId: {PromotionLinkId} | Ref: {Ref} | LinkStore: {LinkStore} | " +
                    "OrderStore: {OrderStore} | CrossStore: {CrossStore} | Telemetry: affiliate.v2.used",
                    affiliateAccountId.Value, promotionLink.Id, referralCode, promotionLink.TenantId, 
                    tenantId, promotionLink.TenantId != tenantId);
            }
            else
            {
                if (protectedEvidence?.PromotionLinkId.HasValue == true)
                {
                    context.Response.Cookies.Delete(AttributionCookieName);
                    _logger.LogWarning(
                        "[Affiliate:Referral] Protected promotion-link evidence has no current active SQL link | TenantId: {TenantId} | PromotionLinkId: {PromotionLinkId}",
                        tenantId,
                        protectedEvidence.PromotionLinkId.Value);
                    await _next(context);
                    return;
                }

                // ═══════════════════════════════════════════════════════════════════
                // V1 FALLBACK — resolve via AffiliateAccounts (legacy flow)
                // AF-19: GLOBAL MARKETPLACE - Lookup globally, no tenant filter
                // ═══════════════════════════════════════════════════════════════════
                
                _logger.LogInformation(
                    "[AF-19:GlobalLookup] V1 fallback query starting | ReferralCode: {Ref} | CurrentTenant: {TenantId} | " +
                    "Looking for Status: {Status} (Active=1)",
                    referralCode, tenantId, (int)AffiliateStatus.Active);
                
                // AF-19: Use IgnoreQueryFilters() - affiliates are global
                var affiliateAccount = await dbContext.AffiliateAccounts
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(a => a.ReferralCode == referralCode && a.Status == AffiliateStatus.Active && !a.IsDeleted)
                    .Select(a => new { a.Id, a.ReferralCode, a.TenantId, a.DisplayName })
                    .FirstOrDefaultAsync(context.RequestAborted);

                _logger.LogInformation(
                    "[AF-19:GlobalLookup] V1 fallback query result | Found: {Found} | AffiliateId: {AffiliateId}",
                    affiliateAccount != null, affiliateAccount?.Id);

                if (affiliateAccount == null)
                {
                    if (protectedEvidence != null)
                    {
                        context.Response.Cookies.Delete(AttributionCookieName);
                    }

                    _logger.LogDebug(
                        "[Affiliate:Referral] Invalid referral code: {Code} | CurrentTenantId: {TenantId}",
                        referralCode, tenantId);
                    await _next(context);
                    return;
                }

                if (protectedEvidence != null &&
                    (protectedEvidence.PromotionLinkId.HasValue ||
                     protectedEvidence.AffiliateAccountId != affiliateAccount.Id))
                {
                    context.Response.Cookies.Delete(AttributionCookieName);
                    _logger.LogWarning(
                        "[Affiliate:Referral] Protected affiliate evidence no longer matches SQL authority | TenantId: {TenantId} | AffiliateAccountId: {AffiliateAccountId}",
                        tenantId,
                        affiliateAccount.Id);
                    await _next(context);
                    return;
                }

                // AF-19: Log cross-store affiliate activation (affiliate's home store != order store)
                if (affiliateAccount.TenantId != tenantId)
                {
                    _logger.LogInformation(
                        "[AF-19:CrossStore] Affiliate from different store | AffiliateId: {AffiliateId} | " +
                        "AffiliateHomeStore: {AffiliateStore} | OrderStore: {OrderStore} | Ref: {Ref}",
                        affiliateAccount.Id, affiliateAccount.TenantId, tenantId, referralCode);
                }

                // Session & click tracking
                var sessionId = EnsureSession(context);
                Guid? productId = ExtractProductId(context.Request.Path);
                var ipHash = HashIp(context.Connection.RemoteIpAddress?.ToString());
                var userAgent = HashUserAgent(context.Request.Headers.UserAgent.ToString());

                var clickEvent = new AffiliateLinkClickedEvent
                {
                    TenantId = tenantId,
                    AffiliateAccountId = affiliateAccount.Id,
                    ReferralCode = affiliateAccount.ReferralCode,
                    ProductId = productId,
                    SessionId = sessionId,
                    IpHash = ipHash,
                    UserAgent = userAgent,
                    OccurredAtUtc = DateTime.UtcNow
                };

                await eventPublisher.PublishAsync(clickEvent, context.RequestAborted);

                // AF-12: Activate v1 Affiliate Mode
                AffiliateModeResolver.SetAffiliateMode(
                    context,
                    affiliateAccount.Id,
                    affiliateAccount.ReferralCode,
                    affiliateAccount.DisplayName); // AF-27E
                EnsureAttributionCookie(
                    context,
                    attributionEvidenceTokens,
                    currentTenantId,
                    affiliateAccount.Id,
                    affiliateAccount.ReferralCode,
                    promotionLinkId: null);

                _logger.LogInformation(
                    "[Affiliate:Mode] Activated (v1 fallback) | AffiliateId: {AffiliateId} | Ref: {Ref} | " +
                    "AffiliateHomeStore: {AffiliateStore} | OrderStore: {OrderStore} | CrossStore: {CrossStore} | Telemetry: affiliate.v1.fallback",
                    affiliateAccount.Id, referralCode, affiliateAccount.TenantId, tenantId, affiliateAccount.TenantId != tenantId);

                // AF-12: Record metric
                await metricsService.RecordAffiliateModeActivationAsync(
                    currentTenantId,
                    affiliateAccount.Id,
                    referralCode,
                    context.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug(
                "[Affiliate:Referral] Request canceled while processing referral | Path: {Path}",
                context.Request.Path.Value ?? "unknown");
        }
        catch (Exception ex)
        {
            // NEVER block the request — analytics failure is non-critical
            _logger.LogWarning(ex,
                "[Affiliate:Referral] Error processing referral (swallowed — non-critical)");
        }

        await _next(context);
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Persists the first accepted referral code for navigation-safe attribution.
    /// The cookie is evidence only; checkout revalidates SQL authority before stamping an order.
    /// </summary>
    private static void EnsureAttributionCookie(
        HttpContext context,
        IAffiliateAttributionEvidenceTokenService attributionEvidenceTokens,
        Guid tenantId,
        Guid affiliateAccountId,
        string referralCode,
        Guid? promotionLinkId)
    {
        if (context.Request.Cookies.TryGetValue(AttributionCookieName, out var existing) &&
            !string.IsNullOrWhiteSpace(existing))
        {
            return;
        }

        var protectedEvidence = attributionEvidenceTokens.Create(
            tenantId,
            affiliateAccountId,
            referralCode,
            promotionLinkId);

        context.Response.Cookies.Append(AttributionCookieName, protectedEvidence, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(30),
            Path = "/"
        });
    }

    /// <summary>
    /// Gets or creates session ID from cookie.
    /// </summary>
    private string EnsureSession(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(CookieName, out var existingSession) &&
            !string.IsNullOrWhiteSpace(existingSession))
        {
            return existingSession;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        context.Response.Cookies.Append(CookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(30),
            Path = "/"
        });
        return sessionId;
    }

    /// <summary>
    /// Attempts to extract a product ID from query params.
    /// The middleware doesn't resolve slugs to IDs — that's the consumer's job.
    /// </summary>
    private static Guid? ExtractProductId(PathString path)
    {
        // Product pages may have ?productId= in the query
        // Slug-based resolution is left to the frontend / storefront controller
        return null;
    }

    private static async Task<bool> IsProductCurrentlyAffiliateSelectableAsync(
        ApplicationDbContext dbContext,
        Product product,
        CancellationToken ct)
    {
        if (product.IsDeleted ||
            !product.IsActive ||
            product.PublishStatus != PublishStatus.Published ||
            !product.IsOnlineVisible)
        {
            return false;
        }

        var policyWithActiveCategory = await dbContext.AffiliateProductPolicies
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Join(
                dbContext.AffiliateCategories.AsNoTracking().IgnoreQueryFilters(),
                policy => policy.AffiliateCategoryId,
                category => category.Id,
                (policy, category) => new { policy, category })
            .AnyAsync(item =>
                item.policy.ProductId == product.Id &&
                item.policy.TenantId == product.TenantId &&
                item.policy.IsEnabled &&
                !item.policy.IsDeleted &&
                item.category.IsActive &&
                !item.category.IsDeleted,
                ct);
        if (!policyWithActiveCategory)
        {
            return false;
        }

        if (!await HasPositiveAffiliatePriceAsync(dbContext, product, ct))
        {
            return false;
        }

        return await HasAvailableAffiliateInventoryAsync(dbContext, product, ct);
    }

    private static async Task<bool> HasPositiveAffiliatePriceAsync(
        ApplicationDbContext dbContext,
        Product product,
        CancellationToken ct)
    {
        if (product.HasVariants)
        {
            return await dbContext.ProductVariants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .WhereCurrentSemanticOwner()
                .AnyAsync(variant =>
                    variant.TenantId == product.TenantId &&
                    variant.ProductId == product.Id &&
                    !variant.IsDeleted &&
                    variant.IsActive &&
                    ((variant.OnlinePrice.HasValue && variant.OnlinePrice.Value > 0m) ||
                     (!variant.OnlinePrice.HasValue && variant.Price > 0m) ||
                     (variant.OnlinePrice.HasValue && variant.OnlinePrice.Value == 0m && variant.Price > 0m)),
                    ct);
        }

        if ((product.OnlinePrice.HasValue && product.OnlinePrice.Value > 0m) ||
            (!product.OnlinePrice.HasValue && product.SalePrice > 0m) ||
            (product.OnlinePrice.HasValue && product.OnlinePrice.Value == 0m && product.SalePrice > 0m))
        {
            return true;
        }

        return await dbContext.ProductUnits
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(unit =>
                unit.ProductId == product.Id &&
                !unit.IsDeleted &&
                unit.SellingPrice.HasValue &&
                unit.SellingPrice.Value > 0m,
                ct);
    }

    private static async Task<bool> HasAvailableAffiliateInventoryAsync(
        ApplicationDbContext dbContext,
        Product product,
        CancellationToken ct)
    {
        if (!product.TrackStock || product.AllowBackorder)
        {
            return true;
        }

        return await dbContext.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(stock =>
                stock.TenantId == product.TenantId &&
                !stock.IsDeleted &&
                stock.Variant != null &&
                stock.Variant.TenantId == product.TenantId &&
                stock.Variant.ProductId == product.Id &&
                !stock.Variant.IsDeleted &&
                stock.Variant.IsActive &&
                stock.Variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                stock.Variant.SemanticSupersededAtUtc == null &&
                stock.Variant.SemanticReplacementVariantId == null &&
                (product.HasVariants || stock.Variant.IsDefault) &&
                stock.Warehouse != null &&
                stock.Warehouse.TenantId == product.TenantId &&
                !stock.Warehouse.IsDeleted &&
                stock.Warehouse.IsActive &&
                stock.Quantity - stock.ReservedQuantity > 0m,
                ct);
    }

    /// <summary>
    /// SHA-256 hash of IP address for analytics without PII.
    /// </summary>
    private static string? HashIp(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// AUDIT FIX F-SEC2: SHA-256 hash of user agent for analytics without PII.
    /// Raw UserAgent combined with other data can constitute PII under GDPR.
    /// Uses the same hashing approach as IP addresses for consistency.
    /// </summary>
    private static string HashUserAgent(string ua)
    {
        if (string.IsNullOrEmpty(ua)) return string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ua));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
