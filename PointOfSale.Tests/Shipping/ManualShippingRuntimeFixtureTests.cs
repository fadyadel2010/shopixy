using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Shipping;

/// <summary>Seed inputs only. Orders, selections, reservations and shipments must be created by real API traffic.</summary>
public sealed class ManualShippingRuntimeFixtureTests
{
    public static readonly Guid TenantId = Guid.Parse("79dfc329-0b39-4f28-9ee8-f409e23b6a16");
    public static readonly Guid ProductId = Guid.Parse("7f257960-4331-4ac1-bc6f-9b058b1d8860");
    public static readonly Guid VariantId = Guid.Parse("cfa96d3f-5a07-4caa-a7ad-5e23e4c0a020");
    public static readonly Guid WarehouseId = Guid.Parse("b6aaf8b1-b548-4515-9b59-41a423b4de20");
    public static readonly Guid MethodId = Guid.Parse("fc1cd3ad-118a-4463-b829-f80a0f79050b");
    public const string MongoDatabase = "shipping_manual_e2e_20261013";
    public static string DatabaseName => IsolatedName("SHIPPING_MANUAL_DATABASE_NAME", "shipping_manual_e2e");
    public static string RuntimeMongoDatabase => IsolatedName("SHIPPING_MANUAL_MONGO_DATABASE", MongoDatabase);

    private static string IsolatedName(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable) ?? fallback;
        if (value != fallback && !System.Text.RegularExpressions.Regex.IsMatch(value, @"\Ashipping_manual_e2e_[a-z0-9_]+\z"))
            throw new InvalidOperationException("Manual runtime fixtures require a separately named shipping disposable database.");
        return value;
    }

    [Fact]
    public async Task PrepareSyntheticInputs_OnlyOnVerifiedIsolatedTargets_WithoutGeographicOrCarrierAuthority()
    {
        var connection = await ShippingIsolatedPostgres.VerifyAsync("SHIPPING_MANUAL_TEST_CONNECTION", DatabaseName);
        var mongoConnection = Environment.GetEnvironmentVariable("SHIPPING_MANUAL_MONGO_CONNECTION")
            ?? throw new InvalidOperationException("Explicit isolated Mongo connection required.");
        var client = new MongoClient(mongoConnection);
        var names = await (await client.ListDatabaseNamesAsync()).ToListAsync();
        Assert.DoesNotContain(RuntimeMongoDatabase, names);
        var tenant = new TenantContext(); tenant.SetTenantId(TenantId);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options, tenant);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(await db.Tenants.AnyAsync(t => t.Id == TenantId));
        Assert.Empty(await db.Orders.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.Shipments.IgnoreQueryFilters().ToListAsync());
        if (!await db.Currencies.AnyAsync(c => c.Code == "USD"))
        {
            db.Currencies.Add(new Currency { Code = "USD", Name = "US Dollar", Symbol = "$", DecimalPlaces = 2 });
            await db.SaveChangesAsync();
        }
        var category = Guid.NewGuid(); var unit = Guid.NewGuid(); var payment = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant { Id = TenantId, Name = "Synthetic Shipping Only", Slug = "shipping-manual", Status = TenantStatus.Active,
            Country = "US", Currency = "USD", StorefrontBaseUrl = "http://shipping-manual.localhost:5184", ContactEmail = "fixture@example.invalid" });
        db.Categories.Add(new Category { Id = category, TenantId = TenantId, Name = "Synthetic shipping inputs" });
        db.Units.Add(new PointOfSale.Core.Entities.Unit { Id = unit, TenantId = TenantId, Name = "Piece", Symbol = "pc" });
        db.Warehouses.Add(new Warehouse { Id = WarehouseId, TenantId = TenantId, Name = "Synthetic Warehouse", Code = "SHIPPING-ONLY",
            IsActive = true, IsDefault = true, Address = "10 Synthetic Street", Phone = "15555550100" });
        db.Products.Add(new Product { Id = ProductId, TenantId = TenantId, CategoryId = category, Name = "Synthetic Shipping Product",
            NameAr = "منتج اختبار الشحن", Slug = "synthetic-shipping-product", SKU = "SHIPPING-ONLY-001", SalePrice = 100, PurchasePrice = 40,
            OnlinePrice = 100, Weight = 1, PublishStatus = PublishStatus.Published, IsOnlineVisible = true, IsActive = true, TrackStock = true });
        db.ProductUnits.Add(new ProductUnit { Id = Guid.NewGuid(), ProductId = ProductId, UnitId = unit, ConversionFactor = 1,
            SellingPrice = 100, PurchasePrice = 40, IsDefault = true, IsBaseUnit = true });
        db.ProductVariants.Add(new ProductVariant { Id = VariantId, TenantId = TenantId, ProductId = ProductId, SKU = "SHIPPING-ONLY-001",
            Price = 100, OnlinePrice = 100, CostPrice = 40, IsDefault = true, IsActive = true, Weight = 1 });
        db.ProductVariantStocks.Add(new ProductVariantStock { Id = Guid.NewGuid(), TenantId = TenantId, VariantId = VariantId, WarehouseId = WarehouseId, Quantity = 100 });
        db.ShippingMethods.Add(new ShippingMethod { Id = MethodId, TenantId = TenantId, Name = "Manual Standard Synthetic", NameAr = "شحن يدوي للاختبار",
            BaseRate = 25, CarrierCode = "manual", ServiceLevel = "standard", FulfillmentWarehouseId = WarehouseId, AvailableCountries = "[\"US\"]", IsActive = true });
        db.ShippingMethods.Add(new ShippingMethod { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Manual Alternate Synthetic", NameAr = "شحن يدوي بديل",
            BaseRate = 40, CarrierCode = "manual", ServiceLevel = "standard", FulfillmentWarehouseId = WarehouseId, AvailableCountries = "[\"US\"]", IsActive = true, SortOrder = 1 });
        db.TenantPaymentMethods.Add(new TenantPaymentMethod { Id = payment, TenantId = TenantId, Code = "COD", DisplayName = "Cash on delivery",
            DisplayNameAr = "الدفع عند الاستلام", IsEnabled = true, SupportsOffline = true, IsSandbox = true });
        db.StoreConfigs.Add(new StoreConfig { Id = Guid.NewGuid(), TenantId = TenantId, StoreName = "Synthetic Shipping Only", StorefrontStatus = "live",
            Behavior = new StoreBehaviorConfig { EnableGuestCheckout = true } });
        await db.SaveChangesAsync();

        // Seed matching read inputs, never checkout responses or provider outcomes.
        var mongo = client.GetDatabase(RuntimeMongoDatabase);
        await mongo.GetCollection<ProductDocument>("products").InsertOneAsync(new ProductDocument {
            TenantId = TenantId, ProductId = ProductId, Name = "Synthetic Shipping Product", NameAr = "منتج اختبار الشحن",
            Slug = "synthetic-shipping-product", SKU = "SHIPPING-ONLY-001", CategoryId = category, Price = 100, OnlinePrice = 100,
            PurchasePrice = 40, IsActive = true, IsOnlineVisible = true, TrackStock = true, StockQuantity = 100, Weight = 1,
            DefaultUnit = new ProductUnitDocument { UnitId = unit, Name = "Piece", SalePrice = 100, ConversionFactor = 1, IsBaseUnit = true },
            Variants = [new ProductVariantDocument { VariantId = VariantId, SKU = "SHIPPING-ONLY-001", Price = 100, OnlinePrice = 100,
                CostPrice = 40, IsDefault = true, IsActive = true, TotalStock = 100, Weight = 1 }],
            CreatedAtUtc = now, SyncedAtUtc = now });
        await mongo.GetCollection<StorefrontConfigDocument>("storefront_configs").InsertOneAsync(new StorefrontConfigDocument {
            Id = TenantId.ToString(), TenantId = TenantId, TenantSlug = "shipping-manual", StoreName = "Synthetic Shipping Only", Currency = "USD",
            StorefrontStatus = "live", UpdatedAt = now, Behavior = new BehaviorConfig { EnableGuestCheckout = true } });
        await mongo.GetCollection<PaymentMethodDocument>("payment_methods").InsertOneAsync(new PaymentMethodDocument {
            TenantId = TenantId, PaymentMethodId = payment, Code = "COD", DisplayName = "Cash on delivery", DisplayNameAr = "الدفع عند الاستلام",
            IsEnabled = true, SupportsOffline = true, IsSandbox = true, IsConfigurationValid = true });
        Assert.Empty(await db.AddressAdmissionEvidence.ToListAsync());
        Assert.Empty(await db.CarrierAccounts.ToListAsync());
        Assert.Empty(await db.CarrierConfigurations.ToListAsync());
    }
}
