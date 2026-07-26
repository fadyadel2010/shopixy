using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.Services.Checkout;

namespace PointOfSale.Tests;

public sealed class CheckoutCartIdempotencySnapshotBuilderTests
{
    [Fact]
    public void BuildHash_IsDeterministic_WhenCartItemsAreReordered()
    {
        var request = BuildRequest();
        var firstSnapshot = BuildSnapshot(
            cartUpdatedAtUtc: new DateTime(2026, 4, 19, 8, 0, 0, DateTimeKind.Utc),
            items:
            [
                new CartItemSnapshot
                {
                    ProductId = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    VariantId = Guid.Parse("00000000-0000-0000-0000-000000000020"),
                    Quantity = 1,
                    CartPrice = 9.99m
                },
                new CartItemSnapshot
                {
                    ProductId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    VariantId = Guid.Parse("00000000-0000-0000-0000-000000000010"),
                    Quantity = 2,
                    CartPrice = 19.995m
                }
            ]);

        var reorderedSnapshot = BuildSnapshot(
            cartUpdatedAtUtc: firstSnapshot.CartUpdatedAtUtc,
            items:
            [
                new CartItemSnapshot
                {
                    ProductId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    VariantId = Guid.Parse("00000000-0000-0000-0000-000000000010"),
                    Quantity = 2,
                    CartPrice = 19.995m
                },
                new CartItemSnapshot
                {
                    ProductId = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    VariantId = Guid.Parse("00000000-0000-0000-0000-000000000020"),
                    Quantity = 1,
                    CartPrice = 9.99m
                }
            ]);

        var firstHash = CheckoutCartIdempotencySnapshotBuilder.BuildHash(firstSnapshot, request);
        var secondHash = CheckoutCartIdempotencySnapshotBuilder.BuildHash(reorderedSnapshot, request);

        Assert.Equal(firstHash, secondHash);
    }

    [Fact]
    public void BuildHash_Changes_WhenQuantityChanges()
    {
        var request = BuildRequest();
        var originalSnapshot = BuildSnapshot(
            cartUpdatedAtUtc: new DateTime(2026, 4, 19, 8, 0, 0, DateTimeKind.Utc),
            items:
            [
                new CartItemSnapshot
                {
                    ProductId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    VariantId = Guid.Parse("00000000-0000-0000-0000-000000000010"),
                    Quantity = 1,
                    CartPrice = 15m
                }
            ]);

        var changedQuantitySnapshot = BuildSnapshot(
            cartUpdatedAtUtc: originalSnapshot.CartUpdatedAtUtc,
            items:
            [
                new CartItemSnapshot
                {
                    ProductId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    VariantId = Guid.Parse("00000000-0000-0000-0000-000000000010"),
                    Quantity = 2,
                    CartPrice = 15m
                }
            ]);

        var originalHash = CheckoutCartIdempotencySnapshotBuilder.BuildHash(originalSnapshot, request);
        var changedHash = CheckoutCartIdempotencySnapshotBuilder.BuildHash(changedQuantitySnapshot, request);

        Assert.NotEqual(originalHash, changedHash);
    }

    [Fact]
    public void BuildHash_Changes_WhenManualDiscountTotalChanges()
    {
        var request = BuildRequest();
        var snapshotWithoutManualDiscount = BuildSnapshot(
            cartUpdatedAtUtc: new DateTime(2026, 4, 19, 8, 0, 0, DateTimeKind.Utc),
            items:
            [
                new CartItemSnapshot
                {
                    ProductId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    VariantId = Guid.Parse("00000000-0000-0000-0000-000000000010"),
                    Quantity = 1,
                    CartPrice = 100m
                }
            ],
            manualDiscountTotal: 0m);

        var snapshotWithManualDiscount = BuildSnapshot(
            cartUpdatedAtUtc: new DateTime(2026, 4, 19, 8, 0, 0, DateTimeKind.Utc),
            items:
            [
                new CartItemSnapshot
                {
                    ProductId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    VariantId = Guid.Parse("00000000-0000-0000-0000-000000000010"),
                    Quantity = 1,
                    CartPrice = 100m
                }
            ],
            manualDiscountTotal: 10m);

        var hashWithoutDiscount = CheckoutCartIdempotencySnapshotBuilder.BuildHash(snapshotWithoutManualDiscount, request);
        var hashWithDiscount = CheckoutCartIdempotencySnapshotBuilder.BuildHash(snapshotWithManualDiscount, request);

        Assert.NotEqual(hashWithoutDiscount, hashWithDiscount);
    }

    private static CheckoutRequestDto BuildRequest() =>
        new()
        {
            CartId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            ShippingMethodId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            PaymentMethod = "stripe",
            CouponCode = "save10",
            ShippingAddress = new AddressDto
            {
                Name = "Test User",
                Phone = "01000000000",
                Email = "test@example.com",
                Street = "123 Test Street",
                City = "Cairo",
                State = "Cairo",
                ZipCode = "11511",
                Country = "eg"
            }
        };

    private static CartSnapshot BuildSnapshot(
        DateTime cartUpdatedAtUtc,
        List<CartItemSnapshot> items,
        decimal manualDiscountTotal = 0m) =>
        new()
        {
            CartId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            TenantId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
            OwnerType = "session",
            OwnerId = "session-1",
            Currency = "EGP",
            CartUpdatedAtUtc = cartUpdatedAtUtc,
            AppliedCouponCode = "SAVE10",
            DiscountTotal = 5m,
            PromotionDiscountTotal = 2m,
            CartPromotionDiscountTotal = 1m,
            ManualDiscountTotal = manualDiscountTotal,
            Items = items
        };
}
