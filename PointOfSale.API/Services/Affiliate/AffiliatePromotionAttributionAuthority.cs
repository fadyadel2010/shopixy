using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.Affiliate;

/// <summary>SQL admission shared by referral capture and final checkout; no financial writes.</summary>
internal static class AffiliatePromotionAttributionAuthority
{
    internal static async Task<Guid?> ResolveProfileAsync(
        ApplicationDbContext db, Guid userId, Guid? savedProfileId, CancellationToken ct)
    {
        return await (from user in db.AffiliateUsers.AsNoTracking().IgnoreQueryFilters()
                      join profile in db.AffiliateAccounts.AsNoTracking().IgnoreQueryFilters()
                          on user.AffiliateAccountId equals profile.Id
                      where user.Id == userId && !user.IsDeleted && user.EmailVerified &&
                            !profile.IsDeleted && profile.Status == AffiliateStatus.Active &&
                            profile.UserId == user.Id &&
                            (!savedProfileId.HasValue || savedProfileId.Value == profile.Id)
                      select (Guid?)profile.Id).SingleOrDefaultAsync(ct);
    }

    internal static async Task<bool> IsProductEligibleAsync(ApplicationDbContext db, Product product, CancellationToken ct)
    {
        var merchantActive = await db.Tenants.AsNoTracking().IgnoreQueryFilters().AnyAsync(t =>
            t.Id == product.TenantId && t.IsActive && t.Status == TenantStatus.Active && t.DeletedAtUtc == null, ct);
        if (!merchantActive) return false;

        // Reuse Phase 2's current SQL catalog pricing/semantic variant and stock admission.
        var current = await ProductSearchSqlAuthorityOverlay.ApplyAsync(db,
            new ProductDocument { ProductId = product.Id, TenantId = product.TenantId }, product.TenantId, ct);
        return current is { IsActive: true, IsOnlineVisible: true, IsSearchable: true, IsAffiliateEnabled: true } &&
            current.TryResolveAffiliatePublicPricing(out var pricing) && pricing.InStock;
    }

    internal static bool MatchesProductEntry(HttpRequest request, Product product, string code)
    {
        if (!HttpMethods.IsGet(request.Method)) return false;
        var segments = (request.Path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? productKey = null;
        if (segments.Length == 5 && segments[0] == "api" && segments[1] == "storefront" &&
            segments[2] == "products" && segments[3] == "by-id") productKey = segments[4];
        else if (segments.Length == 4 && segments[0] == "api" && segments[1] == "storefront" &&
                 segments[2] == "products") productKey = segments[3];
        else if (segments.Length == 2 && segments[0] == "products") productKey = segments[1];
        else if (segments.Length == 3 && segments[0] == "p" &&
                 string.Equals(segments[1], code, StringComparison.OrdinalIgnoreCase)) productKey = segments[2];
        else if (segments.Length == 5 && segments[0] == "api" && segments[1] == "storefront" &&
                 segments[2] == "landing" && string.Equals(segments[3], code, StringComparison.OrdinalIgnoreCase)) productKey = segments[4];

        return productKey != null && (Guid.TryParse(productKey, out var id)
            ? id == product.Id
            : string.Equals(productKey, product.Slug, StringComparison.OrdinalIgnoreCase));
    }
}
