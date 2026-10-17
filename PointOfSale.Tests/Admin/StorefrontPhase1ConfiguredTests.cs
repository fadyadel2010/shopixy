using System.Data;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Checkout;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Admin;

// Explicit existing-Development opt-in. Never provisions, migrates or resets a
// database. Synthetic writes are transaction-local; no order API is invoked.
public sealed class StorefrontPhase1ConfiguredTests
{
    [StorefrontPhase1ConfiguredFact]
    public async Task ExistingTenant_AggregatesAndConfiguration_IndependentReadOnlySql()
    {
        var tenant = Guid.Parse(Environment.GetEnvironmentVariable("STOREFRONT_PHASE1_TENANT")!);
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var connection = new NpgsqlConnection(configured.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, tx)) await readOnly.ExecuteNonQueryAsync();
        async Task<long> Count(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            command.Parameters.AddWithValue("tenant", tenant);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        var items = await Count("SELECT count(*) FROM \"OnlineCustomerWishlistItems\" WHERE \"TenantId\"=@tenant AND NOT \"IsRemoved\"");
        var customers = await Count("SELECT count(DISTINCT \"OnlineCustomerId\") FROM \"OnlineCustomerWishlistItems\" WHERE \"TenantId\"=@tenant AND NOT \"IsRemoved\"");
        var recent = await Count("SELECT count(*) FROM \"OnlineCustomerWishlistItems\" WHERE \"TenantId\"=@tenant AND NOT \"IsRemoved\" AND \"CreatedAtUtc\">=CURRENT_TIMESTAMP-INTERVAL '7 days'");
        var domains = await Count("SELECT count(*) FROM \"Domains\" WHERE \"TenantId\"=@tenant AND NOT \"IsDeleted\"");
        var fields = await Count("SELECT count(*) FROM \"CheckoutFieldDefinitions\" WHERE \"TenantId\"=@tenant AND NOT \"IsDeleted\"");
        var enabled = await Count("SELECT count(*) FROM \"CheckoutFieldDefinitions\" WHERE \"TenantId\"=@tenant AND NOT \"IsDeleted\" AND \"IsEnabled\"");
        var evidence = new { observedUtc = DateTime.UtcNow, tenant, items, customers, average = customers == 0 ? 0 : Math.Round((double)items / customers, 1), recent, domains, fields, enabled, readOnly = true };
        await File.WriteAllTextAsync(Path.Combine(EvidenceDirectory(), "database-independent-counts.json"), JsonSerializer.Serialize(evidence));
        Assert.True(items >= customers);
        await tx.RollbackAsync();
    }

    [StorefrontPhase1ConfiguredFact]
    public async Task CheckoutFields_AdminPersistenceRuntimeAndServerValidation_RollBackAllSyntheticRows()
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var tenant = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'; SET LOCAL statement_timeout = '30s'");
            db.Tenants.AddRange(new Tenant { Id = tenant, Name = "SYNTHETIC Phase1 rollback", Slug = $"p1-{tenant:N}" },
                new Tenant { Id = foreignTenant, Name = "SYNTHETIC Phase1 foreign", Slug = $"p1-{foreignTenant:N}" });
            await db.SaveChangesAsync();
            var admin = new CheckoutFieldDefinitionService(db);
            var system = await admin.InitializeSystemFieldsAsync(tenant, null);
            Assert.Equal(10, system.Fields.Count);
            Assert.Equal(system.Fields.Select(f => f.Id), (await admin.InitializeSystemFieldsAsync(tenant, null)).Fields.Select(f => f.Id));
            var custom = await admin.CreateCustomFieldAsync(tenant, new CreateCheckoutFieldDefinitionRequest {
                FieldType = "SELECT", Label = "SYNTHETIC choice", HelpText = "مرحلة الاختبار", IsEnabled = true, IsRequired = true,
                Configuration = new() { Options = [new() { Value = "one", Label = "واحد" }] }
            }, null);
            db.ChangeTracker.Clear();
            var runtime = new StorefrontCheckoutFieldDefinitionService(db);
            Assert.Contains((await runtime.GetFieldsAsync(tenant)).Fields, f => f.Id == custom.Id && f.IsRequired && f.HelpText == "مرحلة الاختبار");
            var request = new CheckoutRequestDto { CartId = Guid.NewGuid(), ShippingAddress = new AddressDto { Name = "Synthetic", Phone = "+201000000000", City = "Synthetic", Street = "Synthetic", Country = "EG" } };
            var checkout = CreateValidationService(db);
            var required = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() => ValidateFields(checkout, tenant, request));
            Assert.Equal("CUSTOM_FIELD_REQUIRED", required.ErrorCode);
            request.CustomFields = [new() { FieldId = custom.Id, Value = "invalid" }];
            var invalid = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() => ValidateFields(checkout, tenant, request));
            Assert.Equal("CUSTOM_FIELD_INVALID_OPTION", invalid.ErrorCode);
            request.CustomFields = [new() { FieldId = custom.Id, Value = "one" }];
            await ValidateFields(checkout, tenant, request);
            request.CustomFields.Add(new() { FieldId = custom.Id, Value = "one" });
            Assert.Equal("CUSTOM_FIELD_DUPLICATE", (await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() => ValidateFields(checkout, tenant, request))).ErrorCode);
            request.CustomFields = [new() { FieldId = Guid.NewGuid(), Value = "one" }];
            Assert.Equal("CUSTOM_FIELD_UNKNOWN", (await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() => ValidateFields(checkout, tenant, request))).ErrorCode);
            await admin.UpdateFieldAsync(tenant, custom.Id, new() { IsEnabled = false, IsRequired = false }, null);
            request.CustomFields = [];
            await ValidateFields(checkout, tenant, request);
            Assert.DoesNotContain((await runtime.GetFieldsAsync(tenant)).Fields, f => f.Id == custom.Id);
            Assert.Equal("FIELD_NOT_FOUND", (await Assert.ThrowsAsync<CheckoutFieldDefinitionException>(() => admin.UpdateFieldAsync(foreignTenant, custom.Id, new() { Label = "Foreign" }, null))).Code);
            await admin.ArchiveCustomFieldAsync(tenant, custom.Id, null);
            Assert.DoesNotContain((await admin.GetFieldsAsync(tenant)).Fields, f => f.Id == custom.Id);
            Assert.Empty(await db.Orders.IgnoreQueryFilters().Where(o => o.TenantId == tenant).ToListAsync());
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenant || t.Id == foreignTenant));
        Assert.False(await observer.CheckoutFieldDefinitions.IgnoreQueryFilters().AnyAsync(f => f.TenantId == tenant));
        await File.WriteAllTextAsync(Path.Combine(EvidenceDirectory(), "checkout-fields-postgres-rollback.json"), JsonSerializer.Serialize(new { tenant, foreignTenant, rolledBack = true, freshObserverEmpty = true, orderSubmitted = false, serverRequiredValidationExecuted = true }));
    }

    private static string EvidenceDirectory() => Environment.GetEnvironmentVariable("STOREFRONT_PHASE1_EVIDENCE")!;

    private static async Task ValidateFields(CheckoutService service, Guid tenant, CheckoutRequestDto request)
    {
        // Execute the actual server admission used before and inside the checkout
        // transaction. Do not invoke order creation, pricing, stock or payments.
        var method = typeof(CheckoutService).GetMethod("ValidateCheckoutFieldDefinitionsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(service, [tenant, "synthetic", "Session", "synthetic", null, request, null, CancellationToken.None])!;
        await task;
    }

    private static CheckoutService CreateValidationService(ApplicationDbContext db) => new(
        dbContext: db, variantRead: null!, productRead: null!, shippingRead: null!, paymentRead: null!, variantStockRepository: null!,
        mongoDatabase: null!, eventPublisher: null!, couponValidationService: null!, promotionEngine: null!, paymentGateway: null!,
        paymentRuntimeReadinessService: null!, storefrontPricingService: null!, checkoutExecutionLockManager: null!, checkoutRequestIdempotencyCoordinator: null!,
        marketplaceEventOutbox: null!, phoneRiskProfileService: null!, orderVelocityService: null!, dataProtectionProvider: new EphemeralDataProtectionProvider(),
        clientSecretProtector: null!, paymobArtifactProtector: null!, checkoutSessionTokenService: null!, checkoutAttachmentService: null!, warehouseResolutionService: null!,
        r2Options: Options.Create(new R2Options()), paymentLifecycleSettings: Options.Create(new PaymentLifecycleSettings()),
        logger: NullLogger<CheckoutService>.Instance, httpContextAccessor: null!, cartService: null!);
}

public sealed class StorefrontPhase1ConfiguredFactAttribute : FactAttribute
{
    public StorefrontPhase1ConfiguredFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("STOREFRONT_PHASE1_CONFIGURED") != "1")
            Skip = "Explicit Phase1 existing-database read/rollback opt-in required. No provisioning fallback.";
    }
}
