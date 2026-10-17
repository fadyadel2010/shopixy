using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.DTOs.ShippingAdministration;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Core.Interfaces.ShippingAdministration;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;
using PointOfSale.Infrastructure.Shipping.Administration;

namespace PointOfSale.Tests.Shipping;

public sealed class CarrierConnectionConfigurationTests
{
    [Fact]
    public async Task Bosta_CredentialSaveTestAndExplicitSwitchAreIndependentOfNationalGeographyAndBooking()
    {
        await using var f = new Fixture("bosta");
        Assert.Empty(await f.Db.GeographicCatalogPublications.ToListAsync());
        Assert.True((await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, Command("bosta"), f.Actor, "save", default)).Succeeded);
        Assert.True((await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, SavedTest(), f.Actor, "test", default)).Succeeded);
        var before = await f.Service.GetCarrierAccountAsync(f.Tenant, f.Account, default);
        Assert.True(before!.ConnectionTestIsCurrent); Assert.False(before.IntegrationReadiness!.CanExecute);
        Assert.DoesNotContain("CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED", before.IntegrationReadiness.Blockers);
        Assert.False(before.IntegrationReadiness.Enabled);
        var noOp = new ConnectionTestCommand(null, null, null, null, false, null, SaveOnly: true);
        Assert.True((await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, noOp, f.Actor, "retain", default)).Succeeded);
        var retained = await f.Service.GetCarrierAccountAsync(f.Tenant, f.Account, default);
        Assert.Equal(before.ActiveCredentialReferenceId, retained!.ActiveCredentialReferenceId); Assert.True(retained.ConnectionTestIsCurrent);
        Assert.True((await f.Service.ActivateAsync(f.Tenant, f.Account, new(false, false), f.Actor, "activate", default)).Succeeded);
        var activated = await f.Service.GetCarrierAccountAsync(f.Tenant, f.Account, default);
        Assert.True(activated!.IntegrationReadiness!.Enabled); Assert.Equal("Active", activated.State);
        Assert.False(activated.IntegrationReadiness.CanExecute);
        Assert.Contains("BOSTA_PROVIDER_ACCOUNT_ELIGIBILITY_UNVERIFIED", activated.IntegrationReadiness.Blockers);
        Assert.True((await f.Service.DisableAsync(f.Tenant, f.Account, f.Actor, "disable", default)).Succeeded);
        Assert.False((await f.Service.GetCarrierAccountAsync(f.Tenant, f.Account, default))!.IntegrationReadiness!.Enabled);
        Assert.Empty(await f.Db.GeographicPlaces.ToListAsync()); Assert.Equal(1, f.Transport.Calls);
    }

    [Fact]
    public async Task Bosta_FailedOriginSelectionCannotBecomeAnApiKeyAuthenticationFailure()
    {
        var transport = new Transport(GoodBody("bosta"));
        var adapter = Adapter("bosta", new Factory(new HttpClient(transport)));
        var credentials = new CarrierCredentials { ApiKey = "SYNTHETIC-ONLY", ExtraConfig = new() { ["businessLocationId"] = "FOREIGN-PICKUP" } };
        Assert.True((await adapter.ValidateCredentialsAsync(credentials)).Success);
        Assert.Equal(1, transport.Calls);
        Assert.Equal("BOSTA_PICKUP_NOT_OWNED", BostaPickupSelections.Resolve(credentials, new(true, [], null)).ErrorCode);
    }
    [Theory]
    [InlineData("bosta")]
    [InlineData("aramex")]
    [InlineData("dhl")]
    [InlineData("mylerz")]
    public async Task Save_EncryptsRequiredFields_Reloads_AndTestUsesSavedCredentialsWithoutActivation(string carrier)
    {
        await using var f = new Fixture(carrier);
        var save = await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, Command(carrier), f.Actor, "save", default);
        Assert.True(save.Succeeded);
        Assert.Equal(0, f.Transport.Calls);
        var row = await f.Db.CarrierConfigurations.IgnoreQueryFilters().SingleAsync();
        Assert.StartsWith("ENC:", row.ApiKey);
        Assert.False(row.IsEnabled);
        if (carrier != "bosta") Assert.StartsWith("ENC:", row.ApiSecret);
        if (carrier == "aramex") Assert.StartsWith("ENC:", row.Configuration);
        f.Db.ChangeTracker.Clear();
        var reread = await f.Service.GetCarrierAccountAsync(f.Tenant, f.Account, default);
        Assert.Contains("apiKey", reread!.SavedCredentialFields!);
        Assert.False(reread.ConnectionTestIsCurrent);
        var test = await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, SavedTest(), f.Actor, "test", default);
        Assert.True(test.Succeeded);
        Assert.Equal(1, f.Transport.Calls);
        Assert.False((await f.Db.CarrierConfigurations.IgnoreQueryFilters().SingleAsync()).IsEnabled);
        Assert.Empty(await f.Db.CarrierServiceLevelEnablements.IgnoreQueryFilters().ToListAsync());
        var account = await f.Db.CarrierAccounts.IgnoreQueryFilters().SingleAsync();
        Assert.NotEqual(CarrierAccountState.Active, account.State);
        Assert.NotEqual(CarrierOperationalState.Ready, account.OperationalState);
        Assert.True((await f.Service.GetCarrierAccountAsync(f.Tenant, f.Account, default))!.ConnectionTestIsCurrent);
        Assert.DoesNotContain("synthetic-private", System.Text.Json.JsonSerializer.Serialize(reread));
    }

    [Theory]
    [InlineData("bosta")]
    [InlineData("aramex")]
    [InlineData("dhl")]
    [InlineData("mylerz")]
    public async Task EmptySave_IsRejectedWithFieldCode_AndOtherTenantCannotWrite(string carrier)
    {
        await using var f = new Fixture(carrier);
        var empty = await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, new(null, null, null, null, false, null, SaveOnly: true), f.Actor, "empty", default);
        Assert.False(empty.Succeeded); Assert.Contains("CREDENTIAL_APIKEY_MISSING", empty.Blockers);
        Assert.Empty(await f.Db.CarrierConfigurations.IgnoreQueryFilters().ToListAsync());
        var other = await f.Service.RunConnectionTestAsync(Guid.NewGuid(), f.Account, Command(carrier), f.Actor, "other", default);
        Assert.False(other.Succeeded); Assert.Equal("UNKNOWN_CARRIER_ACCOUNT", other.FailureCode);
    }

    [Fact]
    public async Task PartialSave_PreservesSecretsAndExtraSettings_AndInvalidatesHistoricalSuccess()
    {
        await using var f = new Fixture("aramex");
        await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, Command("aramex"), f.Actor, "save", default);
        await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, SavedTest(), f.Actor, "test", default);
        Assert.True((await f.Service.GetCarrierAccountAsync(f.Tenant, f.Account, default))!.ConnectionTestIsCurrent);
        var before = await f.Authority.ResolveConnectionCredentialsAsync(f.Tenant, "aramex");
        var update = new ConnectionTestCommand(null, null, null, null, false, new Dictionary<string, string> { ["optional"] = "changed" }, SaveOnly: true);
        Assert.True((await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, update, f.Actor, "partial", default)).Succeeded);
        var after = await f.Authority.ResolveConnectionCredentialsAsync(f.Tenant, "aramex");
        Assert.Equal(before.Credentials!.ApiKey, after.Credentials!.ApiKey);
        Assert.Equal(before.Credentials.ApiSecret, after.Credentials.ApiSecret);
        Assert.Equal("synthetic-private-pin", after.Credentials.ExtraConfig["AccountPin"]);
        Assert.False((await f.Service.GetCarrierAccountAsync(f.Tenant, f.Account, default))!.ConnectionTestIsCurrent);
    }

    [Theory]
    [InlineData("bosta")]
    [InlineData("aramex")]
    [InlineData("dhl")]
    [InlineData("mylerz")]
    public async Task NewFailure_SupersedesSuccess_AndDiagnosticsNeverContainProviderBody(string carrier)
    {
        await using var f = new Fixture(carrier);
        await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, Command(carrier), f.Actor, "save", default);
        await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, SavedTest(), f.Actor, "good", default);
        f.Transport.Status = HttpStatusCode.Unauthorized;
        f.Transport.Body = "{\"message\":\"synthetic-private-password\"}";
        var failed = await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, SavedTest(), f.Actor, "bad", default);
        Assert.False(failed.Succeeded);
        var reread = await f.Service.GetCarrierAccountAsync(f.Tenant, f.Account, default);
        Assert.Equal("Failed", reread!.LastConnectionTest!.Status);
        Assert.True(reread.ConnectionTestIsCurrent);
        Assert.DoesNotContain("synthetic-private", System.Text.Json.JsonSerializer.Serialize(reread));
        Assert.Equal(2, await f.Db.CarrierConnectionTestRecords.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ChangedEnvironment_RequiresNewTest_AndTestIgnoresSubmittedUnsavedCredentials()
    {
        await using var f = new Fixture("dhl");
        await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, Command("dhl") with { UseSandbox = true }, f.Actor, "save", default);
        var command = SavedTest() with { ApiKey = "must-not-be-used", UseSandbox = false };
        Assert.True((await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, command, f.Actor, "test", default)).Succeeded);
        Assert.Contains("/test/reference-data", f.Transport.LastUri);
        await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, new(null, null, null, null, false, null, SaveOnly: true), f.Actor, "env", default);
        Assert.False((await f.Service.GetCarrierAccountAsync(f.Tenant, f.Account, default))!.ConnectionTestIsCurrent);
    }

    [Fact]
    public async Task HistoricalLocalOnlySuccess_WithoutVersionedAuthenticationEvidence_IsNotCurrent()
    {
        await using var f = new Fixture("dhl");
        await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, Command("dhl"), f.Actor, "save", default);
        await f.Service.RunConnectionTestAsync(f.Tenant, f.Account, SavedTest(), f.Actor, "test", default);
        Assert.True((await f.Service.GetCarrierAccountAsync(f.Tenant, f.Account, default))!.ConnectionTestIsCurrent);
        // A success-shaped historical record cannot establish how credentials were tested.
        var account = await f.Db.CarrierAccounts.IgnoreQueryFilters().SingleAsync();
        f.Db.CarrierConnectionTestRecords.Add(new CarrierConnectionTestRecord
        {
            Id = Guid.NewGuid(), TenantId = f.Tenant, CarrierAccountId = f.Account,
            CredentialReferenceId = account.ActiveCredentialReferenceId, Environment = account.Environment!.Value,
            Status = CarrierConnectionTestStatus.Succeeded, TestedAtUtc = DateTime.UtcNow
        });
        f.Db.CarrierAdministrationAudit.Add(new CarrierAdministrationAudit
        {
            Id = Guid.NewGuid(), TenantId = f.Tenant, CarrierAccountId = f.Account,
            Action = CarrierAdministrationAuditAction.ConnectionTest, ActorId = "offline-fixture",
            Reason = "Historical local field check", OccurredAtUtc = DateTime.UtcNow
        });
        await f.Db.SaveChangesAsync();
        Assert.False((await f.Service.GetCarrierAccountAsync(f.Tenant, f.Account, default))!.ConnectionTestIsCurrent);
    }

    [Fact]
    public async Task Aramex_InvalidEntityAndCountryAreFieldSpecific_WithoutProviderCalls()
    {
        await using var f = new Fixture("aramex");
        var command = Command("aramex");
        Assert.Equal("CREDENTIAL_ACCOUNTENTITY_INVALID", (await f.Service.RunConnectionTestAsync(f.Tenant, f.Account,
            command with { ExtraConfig = new Dictionary<string, string>(command.ExtraConfig!) { ["AccountEntity"] = "invalid" } }, f.Actor, "bad", default)).FailureCode);
        Assert.Equal("CREDENTIAL_ACCOUNTCOUNTRYCODE_INVALID", (await f.Service.RunConnectionTestAsync(f.Tenant, f.Account,
            command with { ExtraConfig = new Dictionary<string, string>(command.ExtraConfig!) { ["AccountCountryCode"] = "invalid" } }, f.Actor, "bad", default)).FailureCode);
        Assert.Equal(0, f.Transport.Calls);
    }

    internal static ConnectionTestCommand Command(string carrier) => new("synthetic-private-user", carrier == "bosta" ? null : "synthetic-private-password",
        carrier == "aramex" ? "synthetic-account" : null, null, false, carrier == "aramex" ? new Dictionary<string, string>
        { ["AccountPin"] = "synthetic-private-pin", ["AccountEntity"] = "CAI", ["AccountCountryCode"] = "EG" } : null, SaveOnly: true);
    internal static ConnectionTestCommand SavedTest() => new(null, null, null, null, false, null, UseSavedCredentials: true);
    internal static string GoodBody(string carrier) => carrier switch
    {
        "bosta" => "{\"success\":true,\"data\":{\"list\":[],\"total\":0}}",
        "aramex" => "{\"HasErrors\":false,\"Notifications\":[],\"Countries\":[{\"Code\":\"EG\"}]}",
        "dhl" => "{\"referenceData\":[{\"datasetName\":\"country\",\"data\":[[{\"attribute\":\"countryCode\",\"value\":\"EG\"}]]}]}",
        _ => "\"synthetic-opaque-token\""
    };
    internal sealed class Transport(string body) : HttpMessageHandler
    {
        internal string Body = body; internal HttpStatusCode Status = HttpStatusCode.OK;
        internal int Calls; internal string? LastUri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; LastUri = request.RequestUri!.ToString(); return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) }); }
    }
    internal sealed class Factory(HttpClient http) : IHttpClientFactory { public HttpClient CreateClient(string name) => http; }
    internal static IShippingCarrierAdapter Adapter(string carrier, IHttpClientFactory factory) => carrier switch
    {
        "bosta" => new BostaAdapter(factory, NullLogger<BostaAdapter>.Instance),
        "aramex" => new AramexAdapter(factory, NullLogger<AramexAdapter>.Instance),
        "dhl" => new DhlAdapter(factory, NullLogger<DhlAdapter>.Instance),
        _ => new MylerzAdapter(factory, NullLogger<MylerzAdapter>.Instance)
    };
    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly ApplicationDbContext Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        internal readonly Guid Tenant = Guid.NewGuid(), Account = Guid.NewGuid();
        internal readonly CarrierAdministrationActor Actor = new("offline-test");
        internal readonly Transport Transport;
        internal readonly CarrierIntegrationAuthority Authority;
        internal readonly ShippingAdministrationApiService Service;
        internal Fixture(string carrier)
        {
            Transport = new(GoodBody(carrier));
            var registry = new ShippingCarrierRegistry([Adapter(carrier, new Factory(new HttpClient(Transport)))], NullLogger<ShippingCarrierRegistry>.Instance);
            var capabilities = new CarrierCapabilityRegistry(registry);
            var encryption = new CarrierCredentialEncryptionService(new EphemeralDataProtectionProvider(), NullLogger<CarrierCredentialEncryptionService>.Instance);
            Authority = new(Db, registry, capabilities, encryption, NullLogger<CarrierIntegrationAuthority>.Instance);
            var runtime = new CarrierAccountAdministrationRuntime(Db, registry, NullLogger<CarrierAccountAdministrationRuntime>.Instance);
            Service = new(Db, runtime, registry, capabilities, Authority);
            Db.CarrierAccounts.Add(new CarrierAccount { Id = Account, TenantId = Tenant, CarrierCode = carrier }); Db.SaveChanges();
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
