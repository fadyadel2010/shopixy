using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Shipping;

public sealed class ManualShippingPosInputFixtureTests
{
    [Fact]
    public async Task CreateSyntheticStaffInput_WithoutChangingTenantApprovalOrEntitlements()
    {
        var connection = await ShippingIsolatedPostgres.VerifyAsync("SHIPPING_MANUAL_TEST_CONNECTION", ManualShippingRuntimeFixtureTests.DatabaseName);
        var password = Environment.GetEnvironmentVariable("SHIPPING_MANUAL_POS_PASSWORD")
            ?? throw new InvalidOperationException("Private synthetic POS password input required.");
        var context = new TenantContext(); context.SetTenantId(ManualShippingRuntimeFixtureTests.TenantId);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options, context);
        Assert.True(await db.Tenants.AnyAsync(t => t.Id == ManualShippingRuntimeFixtureTests.TenantId && t.Slug == "shipping-manual"));
        var id = Guid.Parse("997d0cda-09e6-4a6b-89c8-f8a270955e4c");
        Assert.False(await db.Users.AnyAsync(u => u.Id == id || u.Username == "shipping-isolated-operator"));
        var user = new User { Id = id, TenantId = ManualShippingRuntimeFixtureTests.TenantId, Username = "shipping-isolated-operator",
            Email = "shipping-isolated-operator@example.invalid", FirstName = "Synthetic", LastName = "Operator", Role = UserRole.Admin, IsActive = true };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        Assert.True(await db.Users.AsNoTracking().AnyAsync(u => u.Id == id && u.NormalizedUsername != null));
    }
}
