using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.Infrastructure.Idempotency;

namespace PointOfSale.API.Services.Checkout;

public static class CheckoutCartIdempotencySnapshotBuilder
{
    /// <summary>
    /// Current version of the idempotency snapshot schema.
    /// Increment this when adding/removing/renaming fields in the snapshot records
    /// to prevent hash collisions during rolling deployments.
    /// 
    /// Version History:
    /// - v1: Initial schema (Items, Discounts without ManualDiscountTotal, ShippingMethodId, PaymentMethodId, AddressHash, CartUpdatedAt)
    /// - v2: Added ManualDiscountTotal to CheckoutCartIdempotencyDiscounts
    /// - v3: Added structured address details and canonical geographic candidates
    /// - v4: Added WarehouseResolution authority fields
    /// - v5: Added server-derived unit weight to the canonical cart snapshot
    /// 
    /// IMPORTANT: When incrementing this version:
    /// 1. Document the change in the version history above
    /// 2. Consider the impact on in-flight checkouts during deployment
    /// 3. Old version hashes will not match new version hashes for the same cart state
    /// 4. This is intentional to prevent duplicate orders during schema transitions
    /// </summary>
    private const int CurrentSnapshotVersion = 5;

    public static CheckoutCartIdempotencySnapshot BuildCanonicalSnapshot(
        CartSnapshot cartSnapshot,
        CheckoutRequestDto request,
        Guid? warehouseId = null)
    {
        ArgumentNullException.ThrowIfNull(cartSnapshot);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ShippingAddress);

        var effectiveBillingAddress = request.BillingAddress ?? request.ShippingAddress;
        var addressHash = DeterministicRequestHasher.ComputeHash(new
        {
            ShippingAddress = BuildAddressFingerprint(request.ShippingAddress),
            BillingAddress = BuildAddressFingerprint(effectiveBillingAddress)
        });

        var items = cartSnapshot.Items
            .OrderBy(item => item.ProductId)
            .ThenBy(item => item.VariantId ?? Guid.Empty)
            .ThenBy(item => item.Quantity)
            .ThenBy(item => item.CartPrice)
            .Select(item => new CheckoutCartIdempotencyItem(
                item.ProductId,
                item.VariantId,
                item.Quantity,
                NormalizeCurrency(item.CartPrice),
                NormalizeWeight(item.Weight)))
            .ToList();

        return new CheckoutCartIdempotencySnapshot(
            Items: items,
            Discounts: new CheckoutCartIdempotencyDiscounts(
                cartSnapshot.AppliedCouponId,
                cartSnapshot.AppliedCouponCode?.Trim().ToUpperInvariant(),
                NormalizeCurrency(cartSnapshot.CouponDiscountTotal > 0m ? cartSnapshot.CouponDiscountTotal : cartSnapshot.DiscountTotal),
                NormalizeCurrency(cartSnapshot.PromotionDiscountTotal),
                NormalizeCurrency(cartSnapshot.CartPromotionDiscountTotal),
                NormalizeCurrency(cartSnapshot.ManualDiscountTotal),
                cartSnapshot.AppliedPromotionId,
                cartSnapshot.AppliedPromotionType?.Trim().ToUpperInvariant()),
            ShippingMethodId: request.ShippingMethodId,
            PaymentMethodId: request.PaymentMethod.Trim().ToUpperInvariant(),
            WarehouseResolution: new CheckoutWarehouseResolutionIdempotencySnapshot(
                request.CheckoutSessionId,
                request.WarehouseResolutionId,
                warehouseId,
                request.WarehouseResolutionVersion),
            AddressHash: addressHash,
            CartUpdatedAt: EnsureUtc(cartSnapshot.CartUpdatedAtUtc));
    }

    public static string BuildHash(
        CartSnapshot cartSnapshot,
        CheckoutRequestDto request,
        Guid? warehouseId = null)
    {
        var snapshot = BuildCanonicalSnapshot(cartSnapshot, request, warehouseId);
        
        // Include version in hash to prevent collisions during schema evolution
        var versionedPayload = new
        {
            Version = CurrentSnapshotVersion,
            Snapshot = snapshot
        };
        
        return DeterministicRequestHasher.ComputeHash(versionedPayload);
    }

    private static object BuildAddressFingerprint(AddressDto address) => new
    {
        Name = address.Name?.Trim(),
        Phone = address.Phone?.Trim(),
        Email = address.Email?.Trim().ToLowerInvariant(),
        Street = address.Street?.Trim(),
        Street2 = address.Street2?.Trim(),
        City = address.City?.Trim(),
        State = address.State?.Trim(),
        ZipCode = address.ZipCode?.Trim(),
        Country = address.Country?.Trim().ToUpperInvariant(),
        Building = address.Building?.Trim(),
        Floor = address.Floor?.Trim(),
        Apartment = address.Apartment?.Trim(),
        Landmark = address.Landmark?.Trim(),
        Geography = address.Geography == null ? null : new
        {
            address.Geography.CountryPlaceId,
            address.Geography.AdministrativeAreaPlaceId,
            address.Geography.LocalityPlaceId,
            address.Geography.DistrictPlaceId,
            address.Geography.ZonePlaceId
        }
    };

    private static decimal NormalizeCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal NormalizeWeight(decimal value) =>
        Math.Round(Math.Max(0m, value), 6, MidpointRounding.AwayFromZero);

    private static DateTime EnsureUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        if (value.Kind == DateTimeKind.Unspecified)
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        return value.ToUniversalTime();
    }
}

public sealed record CheckoutCartIdempotencySnapshot(
    IReadOnlyList<CheckoutCartIdempotencyItem> Items,
    CheckoutCartIdempotencyDiscounts Discounts,
    Guid ShippingMethodId,
    string PaymentMethodId,
    CheckoutWarehouseResolutionIdempotencySnapshot WarehouseResolution,
    string AddressHash,
    DateTime CartUpdatedAt);

public sealed record CheckoutWarehouseResolutionIdempotencySnapshot(
    Guid? CheckoutSessionId,
    Guid? WarehouseResolutionId,
    Guid? WarehouseId,
    int? ResolutionVersion);

public sealed record CheckoutCartIdempotencyItem(
    Guid ProductId,
    Guid? VariantId,
    int Quantity,
    decimal UnitPrice,
    decimal UnitWeightKg);

public sealed record CheckoutCartIdempotencyDiscounts(
    Guid? CouponId,
    string? CouponCode,
    decimal CouponDiscountTotal,
    decimal ItemPromotionDiscountTotal,
    decimal CartPromotionDiscountTotal,
    decimal ManualDiscountTotal,
    Guid? PromotionId,
    string? PromotionType);
