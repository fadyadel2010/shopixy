using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed class BostaSetupTenantBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultDiscoveryProofIsServerOwnedAndConfigurationChangesCannotAuthorizeIt(bool changeConfiguration)
    {
        var tenant = Guid.NewGuid(); var context = new TenantContext(); context.SetTenantId(tenant);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var account = new CarrierAccount { Id = Guid.NewGuid(), TenantId = tenant, CarrierCode = "bosta", Environment = CarrierAccountEnvironment.Production,
            ActiveCredentialReferenceId = Guid.NewGuid(), State = CarrierAccountState.Disabled };
        db.CarrierAccounts.Add(account); await db.SaveChangesAsync();
        var configuration = System.Text.Json.JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>($$"""{"Id":"{{Guid.NewGuid()}}","CarrierCode":"bosta"}""")!;
        var credentials = new CarrierCredentials { ApiKey = "SYNTHETIC-NOT-LIVE" };
        var resolution = new CarrierCredentialsResolution(true, "bosta", credentials, configuration, []);
        var after = changeConfiguration ? resolution with { Credentials = new() { ApiKey = "ROTATED-NOT-LIVE" } } : resolution;
        var authority = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        authority.SetupSequence(a => a.ResolveConnectionCredentialsAsync(tenant, "bosta", It.IsAny<CancellationToken>())).ReturnsAsync(resolution).ReturnsAsync(after);
        var adapter = new Mock<IShippingCarrierAdapter>();
        adapter.As<IBostaPickupLocationDiscovery>().Setup(a => a.GetPickupLocationsAsync(credentials, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BostaPickupLocationResult(true, [new("SYNTHETIC-DEFAULT", true, "EG", "CITY", "City", "DISTRICT", "District") { CountryId = "COUNTRY" }], null));
        var registry = new Mock<IShippingCarrierRegistry>(); registry.Setup(r => r.GetAdapter("bosta")).Returns(adapter.Object);
        using var cache = new BostaPickupVerificationCache();
        var controller = new AdminBostaSetupController(db, context, authority.Object, registry.Object, cache) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var response = await controller.PickupLocations(account.Id, default);
        if (changeConfiguration) { Assert.IsType<ConflictObjectResult>(response); Assert.Null(cache.Read(tenant, configuration.Id, account.Id, account.ActiveCredentialReferenceId, credentials)); }
        else
        {
            var ok = Assert.IsType<OkObjectResult>(response);
            Assert.Contains("ProviderDefault", System.Text.Json.JsonSerializer.Serialize(ok.Value));
            Assert.NotNull(cache.Read(tenant, configuration.Id, account.Id, account.ActiveCredentialReferenceId, credentials));
        }
        Assert.Equal(CarrierAccountState.Disabled, account.State); Assert.Empty(credentials.ExtraConfig);
        Assert.Equal(1, await db.CarrierAccounts.IgnoreQueryFilters().CountAsync(a => a.TenantId == tenant));
        Assert.Empty(await db.CarrierConnectionTestRecords.IgnoreQueryFilters().ToListAsync());
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task AccountCredentialRotationDuringDiscoveryInvalidatesReturnedLocations()
    {
        var tenant = Guid.NewGuid(); var context = new TenantContext(); context.SetTenantId(tenant);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var id = Guid.NewGuid(); var account = new CarrierAccount { Id = id, TenantId = tenant, CarrierCode = "bosta", Environment = CarrierAccountEnvironment.Production, ActiveCredentialReferenceId = Guid.NewGuid() };
        db.CarrierAccounts.Add(account); await db.SaveChangesAsync();
        var authority = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        authority.Setup(a => a.ResolveConnectionCredentialsAsync(tenant, "bosta", It.IsAny<CancellationToken>())).ReturnsAsync(new CarrierCredentialsResolution(true, "bosta",
            new() { ApiKey = "synthetic" }, System.Text.Json.JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>("""{"CarrierCode":"bosta"}"""), []));
        var adapter = new Mock<IShippingCarrierAdapter>();
        adapter.As<IBostaPickupLocationDiscovery>().Setup(a => a.GetPickupLocationsAsync(It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .Returns(async () => { account.ActiveCredentialReferenceId = Guid.NewGuid(); await db.SaveChangesAsync(); return new BostaPickupLocationResult(true, [], null); });
        var registry = new Mock<IShippingCarrierRegistry>(); registry.Setup(r => r.GetAdapter("bosta")).Returns(adapter.Object);
        var controller = new AdminBostaSetupController(db, context, authority.Object, registry.Object) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<ConflictObjectResult>(await controller.PickupLocations(id, default));
    }

    [Fact]
    public async Task ForeignAccountCannotResolveCredentialsOrCallProvider()
    {
        var context = new TenantContext(); context.SetTenantId(Guid.NewGuid());
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var id = Guid.NewGuid(); db.CarrierAccounts.Add(new CarrierAccount { Id = id, TenantId = Guid.NewGuid(), CarrierCode = "bosta" }); await db.SaveChangesAsync();
        var authority = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict); var registry = new Mock<IShippingCarrierRegistry>(MockBehavior.Strict);
        var controller = new AdminBostaSetupController(db, context, authority.Object, registry.Object) {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<NotFoundObjectResult>(await controller.PickupLocations(id, default));
        authority.VerifyNoOtherCalls(); registry.VerifyNoOtherCalls();
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task SavedCredentialEnvironmentMismatchCannotQueryWrongProviderAccount()
    {
        var tenant = Guid.NewGuid(); var context = new TenantContext(); context.SetTenantId(tenant);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var id = Guid.NewGuid(); db.CarrierAccounts.Add(new CarrierAccount { Id = id, TenantId = tenant, CarrierCode = "bosta", Environment = CarrierAccountEnvironment.Production }); await db.SaveChangesAsync();
        var authority = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        authority.Setup(a => a.ResolveConnectionCredentialsAsync(tenant, "bosta", It.IsAny<CancellationToken>())).ReturnsAsync(new CarrierCredentialsResolution(true, "bosta",
            new() { UseSandbox = true, ApiKey = "synthetic" }, System.Text.Json.JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>("""{"CarrierCode":"bosta"}"""), []));
        var registry = new Mock<IShippingCarrierRegistry>(MockBehavior.Strict);
        var controller = new AdminBostaSetupController(db, context, authority.Object, registry.Object) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<ConflictObjectResult>(await controller.PickupLocations(id, default)); registry.VerifyNoOtherCalls();
        Assert.Equal(1, await db.CarrierAccounts.IgnoreQueryFilters().CountAsync());
    }
}
