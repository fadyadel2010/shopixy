namespace PointOfSale.Infrastructure.Reliability;

[Flags]
public enum SqlAuthorityOverlayKind
{
    None = 0,
    Variant = 1,
    Pricing = 2,
    Stock = 4,
    Media = 8,
    Promotion = 16,
    Tombstone = 32,
    Order = 64,
    StoreMetadata = 128,
    Shipping = 256,
    Homepage = 512,
    Category = 1024,
    StorefrontConfig = 2048,
    StorefrontFooter = 4096,
    Checkout = 8192,
    CommerceHubListing = 16384,
    StorefrontNavigation = 32768,
    StorefrontCms = 65536,
    ServiceMarketplace = 131072,
    Coupon = 262144
}

public sealed record SqlAuthorityOverlayResult<TProjection>(
    TProjection? Projection,
    bool FoundSqlAuthority,
    SqlAuthorityOverlayKind AppliedKinds,
    string? RejectionReason = null)
{
    public bool Applied => Projection is not null && FoundSqlAuthority;

    public static SqlAuthorityOverlayResult<TProjection> Missing(string reason) =>
        new(default, false, SqlAuthorityOverlayKind.None, reason);

    public static SqlAuthorityOverlayResult<TProjection> AppliedFromSql(
        TProjection projection,
        SqlAuthorityOverlayKind appliedKinds) =>
        new(projection, true, appliedKinds);
}

public interface ISqlAuthorityOverlay<TProjection>
{
    Task<SqlAuthorityOverlayResult<TProjection>> TryApplyAsync(
        TProjection projection,
        Guid tenantId,
        CancellationToken cancellationToken);
}
