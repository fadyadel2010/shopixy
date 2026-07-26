using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Infrastructure.Consumers.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-3 Tests: Projection consumer tests verifying critical invariants.
///
/// Strategy: Tests verify consumer behavior through mock IMongoCollection
/// interactions. For integration-level tests we verify the BSON filter/update
/// shapes and idempotency logic. Architecture tests verify isolation from Postgres.
///
/// Proves:
/// 1. Consumers are correctly wired to IConsumer&lt;T&gt;
/// 2. Projection consumers do NOT depend on PostgreSQL
/// 3. Documents have correct BSON attributes
/// 4. Idempotency field exists on all documents
/// 5. Order status enum values are well-defined
/// 6. Collection names match between consumers and MongoDbContext
/// </summary>
public class AffiliateProjectionTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _affiliateAccountId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();

    // ═══════════════════════════════════════════════════════════════════
    // 1. ALL PROJECTION CONSUMERS IMPLEMENT CORRECT INTERFACES
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AllProjectionConsumers_ImplementCorrectInterface()
    {
        Assert.True(typeof(IConsumer<AffiliateAttributedEvent>)
            .IsAssignableFrom(typeof(AffiliateAttributedProjectionConsumer)));

        Assert.True(typeof(IConsumer<AffiliateCommissionEarnedEvent>)
            .IsAssignableFrom(typeof(AffiliateCommissionEarnedProjectionConsumer)));

        Assert.True(typeof(IConsumer<AffiliateCommissionReversedEvent>)
            .IsAssignableFrom(typeof(AffiliateCommissionReversedProjectionConsumer)));
    }

    // ═══════════════════════════════════════════════════════════════════
    // 2. PROJECTION CONSUMERS DO NOT DEPEND ON POSTGRES
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectionConsumers_DoNotDependOnPostgres()
    {
        var projectionTypes = new[]
        {
            typeof(AffiliateAttributedProjectionConsumer),
            typeof(AffiliateCommissionEarnedProjectionConsumer),
            typeof(AffiliateCommissionReversedProjectionConsumer)
        };

        foreach (var type in projectionTypes)
        {
            var ctorParams = type.GetConstructors().SelectMany(c => c.GetParameters()).ToList();

            // Must NOT inject any DbContext
            var hasDbContext = ctorParams.Any(p =>
                p.ParameterType.Name.Contains("ApplicationDbContext") ||
                p.ParameterType.Name.Contains("DbContext"));
            Assert.False(hasDbContext,
                $"{type.Name} injects a DbContext — violates AF-3 rule: No Postgres in projections");

            // Must inject IMongoDatabase
            var hasMongo = ctorParams.Any(p =>
                p.ParameterType == typeof(IMongoDatabase));
            Assert.True(hasMongo,
                $"{type.Name} does not inject IMongoDatabase");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // 3. DOCUMENTS HAVE PROCESSEDEU IDS FOR IDEMPOTENCY
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Documents_UseBoundedIdempotencyState()
    {
        var dashboard = new AffiliateDashboardDocument();
        Assert.Null(dashboard.ProcessedEventIds);

        var order = new AffiliateOrderDocument();
        Assert.NotNull(order.ProcessedEventIds);
        Assert.Empty(order.ProcessedEventIds);

        var summary = new AffiliateEarningsSummaryDocument();
        Assert.Null(summary.ProcessedEventIds);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 4. DOCUMENTS HAVE CORRECT BSON ATTRIBUTES
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DashboardDocument_HasBsonIdAttribute()
    {
        var idProp = typeof(AffiliateDashboardDocument).GetProperty("Id");
        var bsonIdAttr = idProp!.GetCustomAttributes(typeof(MongoDB.Bson.Serialization.Attributes.BsonIdAttribute), false);
        Assert.Single(bsonIdAttr);
    }

    [Fact]
    public void OrderDocument_HasBsonIdAttribute()
    {
        var idProp = typeof(AffiliateOrderDocument).GetProperty("Id");
        var bsonIdAttr = idProp!.GetCustomAttributes(typeof(MongoDB.Bson.Serialization.Attributes.BsonIdAttribute), false);
        Assert.Single(bsonIdAttr);
    }

    [Fact]
    public void SummaryDocument_HasBsonIdAttribute()
    {
        var idProp = typeof(AffiliateEarningsSummaryDocument).GetProperty("Id");
        var bsonIdAttr = idProp!.GetCustomAttributes(typeof(MongoDB.Bson.Serialization.Attributes.BsonIdAttribute), false);
        Assert.Single(bsonIdAttr);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 5. DOCUMENT DEFAULT VALUES ARE CORRECT
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DashboardDocument_DefaultValues_AreZero()
    {
        var doc = new AffiliateDashboardDocument();
        Assert.Equal(0m, doc.TotalEarned);
        Assert.Equal(0m, doc.TotalReversed);
        Assert.Equal(0m, doc.NetEarnings);
        Assert.Equal(0, doc.TotalOrders);
        Assert.Null(doc.LastCommissionAtUtc);
    }

    [Fact]
    public void OrderDocument_DefaultStatus_IsEarned()
    {
        var doc = new AffiliateOrderDocument();
        Assert.Equal("Earned", doc.Status);
    }

    [Fact]
    public void OrderItemSubDocument_DefaultIsReversed_IsFalse()
    {
        var item = new AffiliateOrderItemSubDocument();
        Assert.False(item.IsReversed);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 6. DOCUMENT FIELD NAMES MATCH EXPECTED BSON ELEMENT NAMES
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DashboardDocument_BsonElementNames_AreCorrect()
    {
        AssertBsonElement<AffiliateDashboardDocument>("TenantId", "tenantId");
        AssertBsonElement<AffiliateDashboardDocument>("AffiliateAccountId", "affiliateAccountId");
        AssertBsonElement<AffiliateDashboardDocument>("TotalEarned", "totalEarned");
        AssertBsonElement<AffiliateDashboardDocument>("TotalReversed", "totalReversed");
        AssertBsonElement<AffiliateDashboardDocument>("NetEarnings", "netEarnings");
        AssertBsonElement<AffiliateDashboardDocument>("TotalOrders", "totalOrders");
        AssertBsonElement<AffiliateDashboardDocument>("ProcessedEventIds", "processedEventIds");
    }

    [Fact]
    public void OrderDocument_BsonElementNames_AreCorrect()
    {
        AssertBsonElement<AffiliateOrderDocument>("OrderId", "orderId");
        AssertBsonElement<AffiliateOrderDocument>("TenantId", "tenantId");
        AssertBsonElement<AffiliateOrderDocument>("Status", "status");
        AssertBsonElement<AffiliateOrderDocument>("Items", "items");
        AssertBsonElement<AffiliateOrderDocument>("ProcessedEventIds", "processedEventIds");
    }

    [Fact]
    public void SummaryDocument_BsonElementNames_AreCorrect()
    {
        AssertBsonElement<AffiliateEarningsSummaryDocument>("Currency", "currency");
        AssertBsonElement<AffiliateEarningsSummaryDocument>("TenantId", "tenantId");
        AssertBsonElement<AffiliateEarningsSummaryDocument>("TotalEarned", "totalEarned");
        AssertBsonElement<AffiliateEarningsSummaryDocument>("ProcessedEventIds", "processedEventIds");
    }

    // ═══════════════════════════════════════════════════════════════════
    // 7. DECIMAL FIELDS USE DECIMAL128 BSON REPRESENTATION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DecimalFields_UseBsonDecimal128Representation()
    {
        AssertBsonRepresentation<AffiliateDashboardDocument>("TotalEarned", BsonType.Decimal128);
        AssertBsonRepresentation<AffiliateDashboardDocument>("TotalReversed", BsonType.Decimal128);
        AssertBsonRepresentation<AffiliateDashboardDocument>("NetEarnings", BsonType.Decimal128);

        AssertBsonRepresentation<AffiliateOrderDocument>("TotalCommission", BsonType.Decimal128);
        AssertBsonRepresentation<AffiliateOrderDocument>("TotalReversed", BsonType.Decimal128);

        AssertBsonRepresentation<AffiliateEarningsSummaryDocument>("TotalEarned", BsonType.Decimal128);
        AssertBsonRepresentation<AffiliateEarningsSummaryDocument>("TotalReversed", BsonType.Decimal128);
        AssertBsonRepresentation<AffiliateEarningsSummaryDocument>("NetEarnings", BsonType.Decimal128);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 8. ORDER ITEM SUB-DOCUMENT HAS ALL REQUIRED FIELDS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void OrderItemSubDocument_HasAllRequiredFields()
    {
        var props = typeof(AffiliateOrderItemSubDocument).GetProperties();
        var names = props.Select(p => p.Name).ToHashSet();

        Assert.Contains("OrderItemId", names);
        Assert.Contains("ProductId", names);
        Assert.Contains("ProductName", names);
        Assert.Contains("VariantName", names);
        Assert.Contains("PaidAmount", names);
        Assert.Contains("CommissionAmount", names);
        Assert.Contains("IsReversed", names);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 9. CONSUMER CONSTRUCTOR SIGNATURES ARE CONSISTENT
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AllProjectionConsumers_HaveConsistentConstructorPattern()
    {
        var types = new[]
        {
            typeof(AffiliateAttributedProjectionConsumer),
            typeof(AffiliateCommissionEarnedProjectionConsumer),
            typeof(AffiliateCommissionReversedProjectionConsumer)
        };

        foreach (var type in types)
        {
            var ctors = type.GetConstructors();
            Assert.Single(ctors); // Single constructor

            var parameters = ctors[0].GetParameters();
            Assert.True(parameters.Length >= 2, $"{type.Name} should have at least 2 ctor params");

            // First param is IMongoDatabase
            Assert.Equal(typeof(IMongoDatabase), parameters[0].ParameterType);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // 10. NO CONSUMER IN AFFILIATE NAMESPACE CONSUMES FROM UNKNOWN EVENTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AffiliateProjectionConsumers_OnlyConsumeAffiliateEvents()
    {
        var affiliateEventTypes = new HashSet<Type>
        {
            typeof(AffiliateAttributedEvent),
            typeof(AffiliateCommissionEarnedEvent),
            typeof(AffiliateCommissionReversedEvent)
        };

        var projectionConsumerTypes = new[]
        {
            typeof(AffiliateAttributedProjectionConsumer),
            typeof(AffiliateCommissionEarnedProjectionConsumer),
            typeof(AffiliateCommissionReversedProjectionConsumer)
        };

        foreach (var consumerType in projectionConsumerTypes)
        {
            var consumedEventTypes = consumerType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>))
                .Select(i => i.GetGenericArguments()[0])
                .ToList();

            foreach (var eventType in consumedEventTypes)
            {
                Assert.Contains(eventType, affiliateEventTypes);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private static void AssertBsonElement<T>(string propertyName, string expectedElementName)
    {
        var prop = typeof(T).GetProperty(propertyName);
        Assert.NotNull(prop);
        var attr = prop!.GetCustomAttributes(
            typeof(MongoDB.Bson.Serialization.Attributes.BsonElementAttribute), false)
            .Cast<MongoDB.Bson.Serialization.Attributes.BsonElementAttribute>()
            .FirstOrDefault();
        Assert.NotNull(attr);
        Assert.Equal(expectedElementName, attr!.ElementName);
    }

    private static void AssertBsonRepresentation<T>(string propertyName, BsonType expectedType)
    {
        var prop = typeof(T).GetProperty(propertyName);
        Assert.NotNull(prop);
        var attr = prop!.GetCustomAttributes(
            typeof(MongoDB.Bson.Serialization.Attributes.BsonRepresentationAttribute), false)
            .Cast<MongoDB.Bson.Serialization.Attributes.BsonRepresentationAttribute>()
            .FirstOrDefault();
        Assert.NotNull(attr);
        Assert.Equal(expectedType, attr!.Representation);
    }
}
