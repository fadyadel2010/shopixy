using System.Diagnostics.Metrics;
using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Pricing;

public interface ICommerceHubPriceAuthority
{
    Task<CommerceHubPriceResolution> ResolveAsync(
        ProductVariant variant,
        CommerceHubListing? listing = null,
        CancellationToken cancellationToken = default);

    Task<CommerceHubPriceResolution> ResolveAsync(
        ProductVariant variant,
        decimal? overridePrice,
        CancellationToken cancellationToken = default);

    Task<CommerceHubPriceResolution> ResolveAsync(
        decimal baseVariantPrice,
        decimal? overridePrice = null,
        CancellationToken cancellationToken = default);

    Task<CommerceHubPriceResolution> ResolveAsync(
        Guid tenantId,
        decimal baseVariantPrice,
        decimal? overridePrice = null,
        CancellationToken cancellationToken = default);
}

public sealed class CommerceHubPriceAuthority : ICommerceHubPriceAuthority
{
    private static readonly Meter Meter = new("PointOfSale.CommerceHub.PriceOverride", "1.0.0");
    private static readonly Counter<long> OverrideApplied = Meter.CreateCounter<long>(
        "commercehub.price_override.resolver_override_count");
    private static readonly Counter<long> FallbackApplied = Meter.CreateCounter<long>(
        "commercehub.price_override.resolver_fallback_count");

    public static bool OverridesEnabled => CommerceHubPriceOverrideRuntimePolicy.OverridesEnabled;

    public Task<CommerceHubPriceResolution> ResolveAsync(
        ProductVariant variant,
        CommerceHubListing? listing = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(variant);

        return ResolveAsync(
            listing?.TenantId ?? variant.TenantId,
            variant,
            listing?.CommerceHubPrice,
            cancellationToken);
    }

    public Task<CommerceHubPriceResolution> ResolveAsync(
        ProductVariant variant,
        decimal? overridePrice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(variant);
        cancellationToken.ThrowIfCancellationRequested();

        return ResolveAsync(
            variant.TenantId,
            ResolveBaseVariantPrice(variant.Price, variant.OnlinePrice),
            overridePrice,
            cancellationToken);
    }

    private Task<CommerceHubPriceResolution> ResolveAsync(
        Guid tenantId,
        ProductVariant variant,
        decimal? overridePrice,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return ResolveAsync(
            tenantId,
            ResolveBaseVariantPrice(variant.Price, variant.OnlinePrice),
            overridePrice,
            cancellationToken);
    }

    public static decimal ResolveBaseVariantPrice(decimal price, decimal? onlinePrice) =>
        onlinePrice.HasValue && onlinePrice.Value > 0m
            ? onlinePrice.Value
            : price;

    public Task<CommerceHubPriceResolution> ResolveAsync(
        decimal baseVariantPrice,
        decimal? overridePrice = null,
        CancellationToken cancellationToken = default)
        => ResolveAsync(Guid.Empty, baseVariantPrice, overridePrice, cancellationToken);

    public Task<CommerceHubPriceResolution> ResolveAsync(
        Guid tenantId,
        decimal baseVariantPrice,
        decimal? overridePrice = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var acceptedOverride = CommerceHubPriceOverrideRuntimePolicy.IsEnabledForTenant(tenantId) &&
                               overridePrice.HasValue &&
                               overridePrice.Value > 0m
            ? overridePrice
            : null;

        var finalPrice = acceptedOverride ?? baseVariantPrice;
        var source = acceptedOverride.HasValue
            ? CommerceHubPriceSource.CommerceHubOverride
            : CommerceHubPriceSource.ProductVariantEffectiveOnlinePrice;

        (acceptedOverride.HasValue ? OverrideApplied : FallbackApplied).Add(1);

        return Task.FromResult(new CommerceHubPriceResolution
        {
            FinalPrice = finalPrice,
            BaseVariantPrice = baseVariantPrice,
            OverridePrice = acceptedOverride,
            PriceSource = source,
            AuthorityAccepted = true,
            FailureReason = null
        });
    }
}

public sealed class CommerceHubPriceOverrideRolloutOptions
{
    public const string SectionName = "CommerceHub:PriceOverrides";

    public bool Enabled { get; set; }
    public List<Guid> TenantAllowlist { get; set; } = new();
    public List<string> EnabledCohorts { get; set; } = new();
    public Dictionary<Guid, string> TenantCohorts { get; set; } = new();
}

public static class CommerceHubPriceOverrideRuntimePolicy
{
    private static CommerceHubPriceOverrideRolloutOptions _options = new();

    public static bool OverridesEnabled => Volatile.Read(ref _options).Enabled;

    public static void Configure(CommerceHubPriceOverrideRolloutOptions? options)
    {
        Volatile.Write(ref _options, options ?? new CommerceHubPriceOverrideRolloutOptions());
    }

    public static bool IsEnabledForTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            return false;
        }

        var options = Volatile.Read(ref _options);
        if (!options.Enabled || !options.TenantAllowlist.Contains(tenantId))
        {
            return false;
        }

        if (options.EnabledCohorts.Count == 0)
        {
            return true;
        }

        return options.TenantCohorts.TryGetValue(tenantId, out var cohort) &&
               options.EnabledCohorts.Contains(cohort, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class CommerceHubPriceResolution
{
    public decimal FinalPrice { get; init; }
    public decimal BaseVariantPrice { get; init; }
    public decimal? OverridePrice { get; init; }
    public CommerceHubPriceSource PriceSource { get; init; }
    public bool AuthorityAccepted { get; init; }
    public string? FailureReason { get; init; }
}

public enum CommerceHubPriceSource
{
    ProductVariantEffectiveOnlinePrice = 0,
    CommerceHubOverride = 1
}
