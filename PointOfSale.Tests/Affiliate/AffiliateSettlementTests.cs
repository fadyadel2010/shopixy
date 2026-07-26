using System.Reflection;
using MassTransit;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Infrastructure.Consumers.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.Services.Affiliate;
using Xunit;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-7: Settlement lifecycle tests.
///
/// Categories:
/// 1. Domain invariants (CreateApproved, CreatePaid factory methods)
/// 2. Enum extension (Approved, Paid values)
/// 3. Idempotency key format
/// 4. Settlement consumer architecture
/// 5. MongoDB document settlement fields
/// 6. Order status computation
/// </summary>
public class AffiliateSettlementTests
{
    [Fact]
    public void AffiliatePayoutReadinessScope_EmptyAffectedStoreArray_DoesNotBroadenDifferentBaseTenant()
    {
        var baseTenantId = Guid.NewGuid();
        var unrelatedStoreTenantId = Guid.NewGuid();
        var method = typeof(AffiliateSettlementOrchestrator).GetMethod(
            "TenantJsonScopeMayAffectStore",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var scopedResult = (bool)method!.Invoke(
            null,
            new object?[] { baseTenantId, "[]", unrelatedStoreTenantId })!;
        var unscopedResult = (bool)method.Invoke(
            null,
            new object?[] { null, "[]", unrelatedStoreTenantId })!;

        Assert.False(scopedResult);
        Assert.True(unscopedResult);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DOMAIN INVARIANTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateApproved_RequiresEarnedEntry()
    {
        var earned = CreateTestEarnedEntry();
        var approved = AffiliateCommissionLedger.CreateApproved(earned);

        Assert.NotNull(approved);
        Assert.Equal(CommissionEntryType.Approved, approved.EntryType);
        Assert.Equal(earned.Id, approved.OriginalEntryId);
    }

    [Fact]
    public void CreateApproved_NonEarnedEntry_Throws()
    {
        // Create an Earned, then Approved, then try to approve the Approved
        var earned = CreateTestEarnedEntry();
        var approved = AffiliateCommissionLedger.CreateApproved(earned);

        Assert.Throws<InvalidOperationException>(() =>
            AffiliateCommissionLedger.CreateApproved(approved));
    }

    [Fact]
    public void CreateApproved_FromReversed_Throws()
    {
        var reversed = CreateTestReversedEntry();

        Assert.Throws<InvalidOperationException>(() =>
            AffiliateCommissionLedger.CreateApproved(reversed));
    }

    [Fact]
    public void CreatePaid_RequiresApprovedEntry()
    {
        var earned = CreateTestEarnedEntry();
        var approved = AffiliateCommissionLedger.CreateApproved(earned);
        var paid = AffiliateCommissionLedger.CreatePaid(approved, approvedMaxPerItem: null);

        Assert.NotNull(paid);
        Assert.Equal(CommissionEntryType.Paid, paid.EntryType);
        Assert.Equal(approved.Id, paid.OriginalEntryId);
    }

    [Fact]
    public void CreatePaid_FromEarned_Throws()
    {
        var earned = CreateTestEarnedEntry();

        Assert.Throws<InvalidOperationException>(() =>
            AffiliateCommissionLedger.CreatePaid(earned, approvedMaxPerItem: null));
    }

    [Fact]
    public void CreatePaid_FromReversed_Throws()
    {
        var reversed = CreateTestReversedEntry();

        Assert.Throws<InvalidOperationException>(() =>
            AffiliateCommissionLedger.CreatePaid(reversed, approvedMaxPerItem: null));
    }

    [Fact]
    public void CreateApproved_CopiesFinancialData_NoRecalculation()
    {
        var earned = CreateTestEarnedEntry();
        var approved = AffiliateCommissionLedger.CreateApproved(earned);

        Assert.Equal(earned.BaseAmount, approved.BaseAmount);
        Assert.Equal(earned.CommissionRate, approved.CommissionRate);
        Assert.Equal(earned.CommissionAmount, approved.CommissionAmount);
        Assert.Equal(earned.Currency, approved.Currency);
        Assert.Equal(earned.TenantId, approved.TenantId);
        Assert.Equal(earned.AffiliateAccountId, approved.AffiliateAccountId);
        Assert.Equal(earned.OrderId, approved.OrderId);
        Assert.Equal(earned.OrderItemId, approved.OrderItemId);
    }

    [Fact]
    public void CreatePaid_CopiesFinancialData_NoRecalculation()
    {
        var earned = CreateTestEarnedEntry();
        var approved = AffiliateCommissionLedger.CreateApproved(earned);
        var paid = AffiliateCommissionLedger.CreatePaid(approved, approvedMaxPerItem: null);

        Assert.Equal(earned.BaseAmount, paid.BaseAmount);
        Assert.Equal(earned.CommissionRate, paid.CommissionRate);
        Assert.Equal(earned.CommissionAmount, paid.CommissionAmount);
        Assert.Equal(earned.Currency, paid.Currency);
    }

    [Fact]
    public void CreatePaid_WithApprovedMaxPerItemBelowApprovedAuthority_FailsClosed()
    {
        // Create an earned entry with 100.00 base amount and 10% commission rate
        // Expected commission without cap: 100.00 * 0.10 = 10.00
        var earned = AffiliateCommissionLedger.CreateEarned(
            storeTenantId: Guid.NewGuid(),
            attributionId: Guid.NewGuid(),
            affiliateAccountId: Guid.NewGuid(),
            orderId: Guid.NewGuid(),
            orderItemId: Guid.NewGuid(),
            paidAmount: 100.00m,
            commissionRate: 0.10m,
            maxPerItem: null,
            currency: "EGP");

        var approved = AffiliateCommissionLedger.CreateApproved(earned);

        // A lower cap at payout time would rewrite already-approved commission authority.
        // The domain must fail closed instead of silently shrinking the paid lifecycle row.
        var capAmount = 7.50m;
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AffiliateCommissionLedger.CreatePaid(approved, approvedMaxPerItem: capAmount));

        Assert.Contains("Payable commission amount does not match approved calculation", exception.Message);
    }

    [Fact]
    public void CreatePaid_WithApprovedMaxPerItem_NoCapWhenHigherThanCalculated()
    {
        // Create an earned entry with 100.00 base amount and 10% commission rate
        // Expected commission without cap: 100.00 * 0.10 = 10.00
        var earned = AffiliateCommissionLedger.CreateEarned(
            storeTenantId: Guid.NewGuid(),
            attributionId: Guid.NewGuid(),
            affiliateAccountId: Guid.NewGuid(),
            orderId: Guid.NewGuid(),
            orderItemId: Guid.NewGuid(),
            paidAmount: 100.00m,
            commissionRate: 0.10m,
            maxPerItem: null,
            currency: "EGP");

        var approved = AffiliateCommissionLedger.CreateApproved(earned);

        // Set cap higher than calculated commission (10.00)
        var capAmount = 15.00m;
        var paid = AffiliateCommissionLedger.CreatePaid(approved, approvedMaxPerItem: capAmount);

        Assert.Equal(CommissionEntryType.Paid, paid.EntryType);
        Assert.Equal(approved.Id, paid.OriginalEntryId);
        Assert.Equal(100.00m, paid.BaseAmount);
        Assert.Equal(0.10m, paid.CommissionRate);
        // Commission should remain at calculated 10.00, not capped
        Assert.Equal(10.00m, paid.CommissionAmount);
        Assert.Equal("EGP", paid.Currency);
    }

    [Fact]
    public void CreatePaid_WithApprovedMaxPerItem_EqualsCalculated_NoChange()
    {
        // Create an earned entry with 100.00 base amount and 10% commission rate
        // Expected commission without cap: 100.00 * 0.10 = 10.00
        var earned = AffiliateCommissionLedger.CreateEarned(
            storeTenantId: Guid.NewGuid(),
            attributionId: Guid.NewGuid(),
            affiliateAccountId: Guid.NewGuid(),
            orderId: Guid.NewGuid(),
            orderItemId: Guid.NewGuid(),
            paidAmount: 100.00m,
            commissionRate: 0.10m,
            maxPerItem: null,
            currency: "EGP");

        var approved = AffiliateCommissionLedger.CreateApproved(earned);

        // Set cap equal to calculated commission (10.00)
        var capAmount = 10.00m;
        var paid = AffiliateCommissionLedger.CreatePaid(approved, approvedMaxPerItem: capAmount);

        Assert.Equal(CommissionEntryType.Paid, paid.EntryType);
        Assert.Equal(approved.Id, paid.OriginalEntryId);
        Assert.Equal(100.00m, paid.BaseAmount);
        Assert.Equal(0.10m, paid.CommissionRate);
        // Commission should equal calculated amount with no extra rounding/capping logic
        Assert.Equal(10.00m, paid.CommissionAmount);
        Assert.Equal("EGP", paid.Currency);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ENUM EXTENSION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CreatePaidNetOfReversals_UsesPayableRemainder()
    {
        var earned = CreateTestEarnedEntry();
        var approved = AffiliateCommissionLedger.CreateApproved(earned);
        var paid = AffiliateCommissionLedger.CreatePaidNetOfReversals(
            approved,
            payableBaseAmount: 60.00m,
            payableCommissionAmount: 6.00m,
            approvedMaxPerItem: null);

        Assert.Equal(CommissionEntryType.Paid, paid.EntryType);
        Assert.Equal(approved.Id, paid.OriginalEntryId);
        Assert.Equal(approved.StoreTenantId, paid.StoreTenantId);
        Assert.Equal(approved.AffiliateAccountId, paid.AffiliateAccountId);
        Assert.Equal(approved.OrderId, paid.OrderId);
        Assert.Equal(approved.OrderItemId, paid.OrderItemId);
        Assert.Equal(60.00m, paid.BaseAmount);
        Assert.Equal(6.00m, paid.CommissionAmount);
        Assert.Equal(approved.CommissionRate, paid.CommissionRate);
        Assert.Equal(approved.Currency, paid.Currency);
        Assert.Equal($"AFFILIATE_COMM:PAID:{approved.OrderItemId}", paid.IdempotencyKey);
    }

    [Fact]
    public void CreatePaidNetOfReversals_CannotExceedApprovedAuthority()
    {
        var earned = CreateTestEarnedEntry();
        var approved = AffiliateCommissionLedger.CreateApproved(earned);

        Assert.Throws<ArgumentException>(() =>
            AffiliateCommissionLedger.CreatePaidNetOfReversals(
                approved,
                payableBaseAmount: approved.BaseAmount,
                payableCommissionAmount: approved.CommissionAmount + 0.0001m,
                approvedMaxPerItem: null));
    }

    [Fact]
    public void CommissionEntryType_HasApprovedAndPaidValues()
    {
        Assert.Equal(2, (int)CommissionEntryType.Approved);
        Assert.Equal(3, (int)CommissionEntryType.Paid);
    }

    [Fact]
    public void CommissionEntryType_AllValues()
    {
        var values = Enum.GetValues<CommissionEntryType>();
        Assert.Equal(4, values.Length);
        Assert.Contains(CommissionEntryType.Earned, values);
        Assert.Contains(CommissionEntryType.Reversed, values);
        Assert.Contains(CommissionEntryType.Approved, values);
        Assert.Contains(CommissionEntryType.Paid, values);
    }

    // ═══════════════════════════════════════════════════════════════════
    // IDEMPOTENCY KEY FORMAT
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void IdempotencyKey_Approved_MatchesDesign()
    {
        var earned = CreateTestEarnedEntry();
        var approved = AffiliateCommissionLedger.CreateApproved(earned);

        var expected = $"AFFILIATE_COMM:APPROVED:{earned.OrderItemId}";
        Assert.Equal(expected, approved.IdempotencyKey);
    }

    [Fact]
    public void IdempotencyKey_Paid_MatchesDesign()
    {
        var earned = CreateTestEarnedEntry();
        var approved = AffiliateCommissionLedger.CreateApproved(earned);
        var paid = AffiliateCommissionLedger.CreatePaid(approved, approvedMaxPerItem: null);

        var expected = $"AFFILIATE_COMM:PAID:{earned.OrderItemId}";
        Assert.Equal(expected, paid.IdempotencyKey);
    }

    [Fact]
    public void IdempotencyKeys_AreUniquePerEntryType()
    {
        var earned = CreateTestEarnedEntry();
        var approved = AffiliateCommissionLedger.CreateApproved(earned);
        var paid = AffiliateCommissionLedger.CreatePaid(approved, approvedMaxPerItem: null);

        // All four entry types produce distinct keys for same OrderItemId
        var keys = new[]
        {
            earned.IdempotencyKey,
            approved.IdempotencyKey,
            paid.IdempotencyKey
        };

        Assert.Equal(keys.Distinct().Count(), keys.Length);
    }

    // ═══════════════════════════════════════════════════════════════════
    // SETTLEMENT CONSUMER ARCHITECTURE
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AffiliateOrderDeliveredConsumer_ImplementsCorrectInterface()
    {
        var interfaces = typeof(AffiliateOrderDeliveredConsumer).GetInterfaces();
        Assert.Contains(interfaces, i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition() == typeof(IConsumer<>) &&
            i.GetGenericArguments()[0] == typeof(OrderDeliveredEvent));
        Assert.Contains(interfaces, i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition() == typeof(IConsumer<>) &&
            i.GetGenericArguments()[0] == typeof(FulfillmentDeliveredEvent));
    }

    [Fact]
    public void AffiliateOrderDeliveredConsumer_DoesNotDependOnMongoDB()
    {
        var ctor = typeof(AffiliateOrderDeliveredConsumer).GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType.FullName ?? "").ToList();

        Assert.DoesNotContain(paramTypes, t => t.Contains("MongoDB") || t.Contains("IMongoDatabase"));
    }

    [Fact]
    public void SettlementProjectionConsumers_ImplementCorrectInterfaces()
    {
        var approvedConsumer = typeof(AffiliateCommissionApprovedProjectionConsumer);
        var paidConsumer = typeof(AffiliateCommissionPaidProjectionConsumer);

        Assert.True(typeof(IConsumer<AffiliateCommissionApprovedEvent>).IsAssignableFrom(approvedConsumer));
        Assert.True(typeof(IConsumer<AffiliateCommissionPaidEvent>).IsAssignableFrom(paidConsumer));
    }

    [Fact]
    public void SettlementProjectionConsumers_DoNotDependOnPostgres()
    {
        var projectionTypes = new[]
        {
            typeof(AffiliateCommissionApprovedProjectionConsumer),
            typeof(AffiliateCommissionPaidProjectionConsumer)
        };

        foreach (var type in projectionTypes)
        {
            var ctor = type.GetConstructors().Single();
            var hasDbContext = ctor.GetParameters().Any(p =>
                p.ParameterType.Name.Contains("ApplicationDbContext") ||
                p.ParameterType.Name.Contains("DbContext"));

            Assert.False(hasDbContext,
                $"{type.Name} injects a DbContext — violates AF-7 rule: No Postgres in projections");
        }
    }

    [Fact]
    public void SettlementProjectionConsumers_HaveConsistentConstructorPattern()
    {
        var projectionTypes = new[]
        {
            typeof(AffiliateCommissionApprovedProjectionConsumer),
            typeof(AffiliateCommissionPaidProjectionConsumer)
        };

        foreach (var type in projectionTypes)
        {
            var ctor = type.GetConstructors().Single();
            var paramTypes = ctor.GetParameters().Select(p => p.ParameterType);

            // Must have IMongoDatabase parameter
            Assert.Contains(paramTypes, t => t.FullName?.Contains("IMongoDatabase") ?? false);

            // Must have ILogger parameter
            Assert.Contains(paramTypes, t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>));
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // EVENT SHAPES
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AffiliateCommissionApprovedEvent_HasRequiredProperties()
    {
        var evt = new AffiliateCommissionApprovedEvent
        {
            OrderId = Guid.NewGuid(),
            OrderNumber = "ORD-001",
            AffiliateAccountId = Guid.NewGuid(),
            TotalApproved = 50.00m,
            Currency = "EGP",
            ItemCount = 3
        };

        Assert.NotEqual(Guid.Empty, evt.OrderId);
        Assert.Equal("ORD-001", evt.OrderNumber);
        Assert.Equal(50.00m, evt.TotalApproved);
        Assert.Equal("EGP", evt.Currency);
        Assert.Equal(3, evt.ItemCount);
    }

    [Fact]
    public void AffiliateCommissionPaidEvent_HasRequiredProperties()
    {
        var evt = new AffiliateCommissionPaidEvent
        {
            OrderId = Guid.NewGuid(),
            OrderNumber = "ORD-002",
            AffiliateAccountId = Guid.NewGuid(),
            TotalPaid = 75.00m,
            Currency = "USD",
            ItemCount = 2
        };

        Assert.NotEqual(Guid.Empty, evt.OrderId);
        Assert.Equal(75.00m, evt.TotalPaid);
        Assert.Equal("USD", evt.Currency);
        Assert.Equal(2, evt.ItemCount);
    }

    [Fact]
    public void SettlementEvents_InheritFromIntegrationEvent()
    {
        Assert.True(typeof(IntegrationEvent).IsAssignableFrom(typeof(AffiliateCommissionApprovedEvent)));
        Assert.True(typeof(IntegrationEvent).IsAssignableFrom(typeof(AffiliateCommissionPaidEvent)));
    }

    // ═══════════════════════════════════════════════════════════════════
    // MONGODB DOCUMENT SETTLEMENT FIELDS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DashboardDocument_HasSettlementFields()
    {
        var doc = new AffiliateDashboardDocument();
        Assert.Equal(0m, doc.TotalApproved);
        Assert.Equal(0m, doc.TotalPaid);
    }

    [Fact]
    public void OrderDocument_HasSettlementFields()
    {
        var doc = new AffiliateOrderDocument();
        Assert.Equal(0m, doc.TotalApproved);
        Assert.Equal(0m, doc.TotalPaid);
    }

    [Fact]
    public void OrderItemSubDocument_HasSettlementStatus()
    {
        var item = new AffiliateOrderItemSubDocument();
        Assert.Equal("Earned", item.SettlementStatus);
    }

    [Fact]
    public void EarningsSummaryDocument_HasSettlementFields()
    {
        var doc = new AffiliateEarningsSummaryDocument();
        Assert.Equal(0m, doc.TotalApproved);
        Assert.Equal(0m, doc.TotalPaid);
    }

    [Fact]
    public void SettlementFields_HaveBsonDecimal128()
    {
        var dashboardProps = new[] { "TotalApproved", "TotalPaid" };
        foreach (var propName in dashboardProps)
        {
            var prop = typeof(AffiliateDashboardDocument).GetProperty(propName);
            Assert.NotNull(prop);
            var bsonRep = prop!.GetCustomAttributes()
                .FirstOrDefault(a => a.GetType().Name == "BsonRepresentationAttribute");
            Assert.NotNull(bsonRep);
        }

        var summaryProps = new[] { "TotalApproved", "TotalPaid" };
        foreach (var propName in summaryProps)
        {
            var prop = typeof(AffiliateEarningsSummaryDocument).GetProperty(propName);
            Assert.NotNull(prop);
            var bsonRep = prop!.GetCustomAttributes()
                .FirstOrDefault(a => a.GetType().Name == "BsonRepresentationAttribute");
            Assert.NotNull(bsonRep);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // SETTLEMENT LIFECYCLE PREVENTION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CannotApprove_WithoutEarned()
    {
        // Reversed entry cannot be approved
        var reversed = CreateTestReversedEntry();
        var ex = Assert.Throws<InvalidOperationException>(
            () => AffiliateCommissionLedger.CreateApproved(reversed));

        Assert.Contains("Cannot approve non-Earned entry", ex.Message);
    }

    [Fact]
    public void CannotPay_WithoutApproved()
    {
        // Earned entry cannot be paid directly
        var earned = CreateTestEarnedEntry();
        var ex = Assert.Throws<InvalidOperationException>(
            () => AffiliateCommissionLedger.CreatePaid(earned, approvedMaxPerItem: null));

        Assert.Contains("Cannot pay non-Approved entry", ex.Message);
    }

    [Fact]
    public void CannotPay_Reversed()
    {
        var reversed = CreateTestReversedEntry();
        Assert.Throws<InvalidOperationException>(
            () => AffiliateCommissionLedger.CreatePaid(reversed, approvedMaxPerItem: null));
    }

    [Fact]
    public void FullSettlementLifecycle_Earned_Approved_Paid()
    {
        var earned = CreateTestEarnedEntry();
        Assert.Equal(CommissionEntryType.Earned, earned.EntryType);
        Assert.Null(earned.OriginalEntryId);

        var approved = AffiliateCommissionLedger.CreateApproved(earned);
        Assert.Equal(CommissionEntryType.Approved, approved.EntryType);
        Assert.Equal(earned.Id, approved.OriginalEntryId);

        var paid = AffiliateCommissionLedger.CreatePaid(approved, approvedMaxPerItem: null);
        Assert.Equal(CommissionEntryType.Paid, paid.EntryType);
        Assert.Equal(approved.Id, paid.OriginalEntryId);

        // IDs are unique
        var ids = new[] { earned.Id, approved.Id, paid.Id };
        Assert.Equal(3, ids.Distinct().Count());

        // All share the same financial data
        Assert.Equal(earned.CommissionAmount, paid.CommissionAmount);
        Assert.Equal(earned.BaseAmount, paid.BaseAmount);
    }

    [Fact]
    public void Approved_HasDifferentId_FromEarned()
    {
        var earned = CreateTestEarnedEntry();
        var approved = AffiliateCommissionLedger.CreateApproved(earned);

        Assert.NotEqual(earned.Id, approved.Id);
    }

    [Fact]
    public void Paid_HasDifferentId_FromApproved()
    {
        var earned = CreateTestEarnedEntry();
        var approved = AffiliateCommissionLedger.CreateApproved(earned);
        var paid = AffiliateCommissionLedger.CreatePaid(approved, approvedMaxPerItem: null);

        Assert.NotEqual(approved.Id, paid.Id);
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private static AffiliateCommissionLedger CreateTestEarnedEntry()
    {
        return AffiliateCommissionLedger.CreateEarned(
            storeTenantId: Guid.NewGuid(),
            attributionId: Guid.NewGuid(),
            affiliateAccountId: Guid.NewGuid(),
            orderId: Guid.NewGuid(),
            orderItemId: Guid.NewGuid(),
            paidAmount: 100.00m,
            commissionRate: 0.10m,
            maxPerItem: null,
            currency: "EGP");
    }

    private static AffiliateCommissionLedger CreateTestReversedEntry()
    {
        var earned = CreateTestEarnedEntry();
        return AffiliateCommissionLedger.CreateReversal(
            storeTenantId: earned.TenantId ?? Guid.NewGuid(),
            originalEntryId: earned.Id,
            attributionId: earned.AttributionId,
            affiliateAccountId: earned.AffiliateAccountId,
            orderId: earned.OrderId,
            orderItemId: earned.OrderItemId,
            originalBaseAmount: earned.BaseAmount,
            originalCommission: earned.CommissionAmount,
            originalRate: earned.CommissionRate,
            currency: earned.Currency);
    }
}
