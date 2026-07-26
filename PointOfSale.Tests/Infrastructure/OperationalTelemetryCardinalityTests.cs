using System.Diagnostics.Metrics;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Tests.Infrastructure;

public sealed class OperationalTelemetryCardinalityTests
{
    [Fact]
    public void AbandonedCartRecoveryTelemetry_UsesTenantBucketsAndNormalizedReasons()
    {
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var telemetry = new AbandonedCartRecoveryTelemetry();

        using var listener = CreateLongListener(
            AbandonedCartRecoveryTelemetry.MeterName,
            "abandoned_cart.callback.auth_failure_total",
            out var measurements);

        telemetry.RecordCallbackAuthFailure(tenantId, "Admin Tenant Scope Mismatch!");

        var measurement = Assert.Single(measurements);
        Assert.Equal(ExpectedTenantBucket(tenantId), Tag(measurement.Tags, "tenant_bucket"));
        Assert.Equal("admin_tenant_scope_mismatch", Tag(measurement.Tags, "reason"));
        Assert.DoesNotContain(measurement.Tags, tag => tag.Key == "tenant_id");
    }

    [Fact]
    public void StorefrontPublishTelemetry_UsesTenantBucketsAndNormalizedTags()
    {
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        using var telemetry = new StorefrontPublishTelemetry();

        using var listener = CreateLongListener(
            StorefrontPublishTelemetry.MeterName,
            "admin_rollback_invalid_total",
            out var measurements);

        telemetry.RecordInvalidRollback(tenantId, "Invalid Rollback Reason!");

        var measurement = Assert.Single(measurements);
        Assert.Equal(ExpectedTenantBucket(tenantId), Tag(measurement.Tags, "tenant_bucket"));
        Assert.Equal("invalid_rollback_reason", Tag(measurement.Tags, "reason"));
        Assert.DoesNotContain(measurement.Tags, tag => tag.Key == "tenant_id");
    }

    [Fact]
    public void AffiliateTelemetry_UsesTenantBucketsAndNormalizedTags()
    {
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var telemetry = new AffiliateTelemetry();

        using var listener = CreateLongListener(
            AffiliateTelemetry.MeterName,
            "affiliate.commission.skipped_total",
            out var measurements);

        telemetry.RecordSkipped(tenantId, "No Attribution!");

        var measurement = Assert.Single(measurements);
        Assert.Equal(ExpectedTenantBucket(tenantId), Tag(measurement.Tags, "tenant_bucket"));
        Assert.Equal("no_attribution", Tag(measurement.Tags, "reason"));
        Assert.DoesNotContain(measurement.Tags, tag => tag.Key == "tenant_id");
    }

    [Fact]
    public void AnalyticsReliabilityTelemetry_UsesTenantBucketsAndNormalizedDimensions()
    {
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var telemetry = new AnalyticsReliabilityTelemetry();

        using var listener = CreateLongListener(
            AnalyticsReliabilityTelemetry.MeterName,
            "analytics_payload_rejected_total",
            out var measurements);

        telemetry.RecordPayloadRejected(tenantId, "Too Large Payload!");

        var measurement = Assert.Single(measurements);
        Assert.Equal(ExpectedTenantBucket(tenantId), Tag(measurement.Tags, "tenant_bucket"));
        Assert.Equal("too_large_payload", Tag(measurement.Tags, "reason"));
        Assert.DoesNotContain(measurement.Tags, tag => tag.Key == "tenant_id");
    }

    [Fact]
    public void SearchTelemetry_NormalizesDynamicSearchType()
    {
        var telemetry = new SearchTelemetry();

        using var listener = CreateLongListener(
            SearchTelemetry.MeterName,
            "elasticsearch.search.total",
            out var measurements);

        telemetry.RecordSearch("CommerceHub Suggest/Zero Result", 12.5d, 0, isSuccess: true);

        var measurement = Assert.Single(measurements);
        Assert.Equal("commercehub_suggestzero_result", Tag(measurement.Tags, "search_type"));
    }

    [Fact]
    public void InventoryReservationTelemetry_NormalizesMutationSource()
    {
        var telemetry = new InventoryReservationTelemetry();

        using var listener = CreateDoubleListener(
            InventoryReservationTelemetry.MeterName,
            "inventory.reservations.committed_quantity_total",
            out var measurements);

        telemetry.RecordCommit(3m, "Checkout Expiry Repair!");

        var measurement = Assert.Single(measurements);
        Assert.Equal("checkout_expiry_repair", Tag(measurement.Tags, "source"));
    }

    [Fact]
    public void RedisPerformanceTelemetry_UsesBoundedCacheAndPatternDimensions()
    {
        var telemetry = new RedisPerformanceTelemetry();

        using var hitListener = CreateLongListener(
            RedisPerformanceTelemetry.MeterName,
            "redis.cache.hits",
            out var hitMeasurements);

        telemetry.RecordHit("homepage-sections-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var hit = Assert.Single(hitMeasurements);
        Assert.Equal("homepage", Tag(hit.Tags, "cache_type"));

        using var patternListener = CreateLongListener(
            RedisPerformanceTelemetry.MeterName,
            "redis.cache.pattern_invalidations",
            out var patternMeasurements);

        telemetry.RecordPatternInvalidation("pos:tenant:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa:product:12345:*");

        var pattern = Assert.Single(patternMeasurements);
        Assert.Equal("product", Tag(pattern.Tags, "pattern_family"));
        Assert.DoesNotContain(pattern.Tags, tag => tag.Key == "pattern");
    }

    [Fact]
    public void AccountingMetrics_UsesTenantBucketsAndNormalizedGatewayTags()
    {
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        using var telemetry = new AccountingMetrics(Microsoft.Extensions.Logging.Abstractions.NullLogger<AccountingMetrics>.Instance);

        using var listener = CreateLongListener(
            AccountingMetrics.MeterName,
            "accounting.posted_total",
            out var measurements);

        telemetry.IncrementEcommercePaymentsPosted(tenantId, "Stripe Connect");

        var measurement = Assert.Single(measurements);
        Assert.Equal(ExpectedTenantBucket(tenantId), Tag(measurement.Tags, "tenant_bucket"));
        Assert.Equal("online_payment", Tag(measurement.Tags, "operation_type"));
        Assert.Equal("stripe_connect", Tag(measurement.Tags, "gateway"));
        Assert.DoesNotContain(measurement.Tags, tag => tag.Key == "tenant_id");
    }

    private static MeterListener CreateLongListener(
        string meterName,
        string instrumentName,
        out List<(long Value, KeyValuePair<string, object?>[] Tags)> measurements)
    {
        var capturedMeasurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        measurements = capturedMeasurements;
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, currentListener) =>
            {
                if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
                {
                    currentListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            capturedMeasurements.Add((measurement, tags.ToArray()));
        });
        listener.Start();
        return listener;
    }

    private static MeterListener CreateDoubleListener(
        string meterName,
        string instrumentName,
        out List<(double Value, KeyValuePair<string, object?>[] Tags)> measurements)
    {
        var capturedMeasurements = new List<(double Value, KeyValuePair<string, object?>[] Tags)>();
        measurements = capturedMeasurements;
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, currentListener) =>
            {
                if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
                {
                    currentListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            capturedMeasurements.Add((measurement, tags.ToArray()));
        });
        listener.Start();
        return listener;
    }

    private static string Tag(KeyValuePair<string, object?>[] tags, string key) =>
        tags.Single(tag => tag.Key == key).Value?.ToString() ?? string.Empty;

    private static string ExpectedTenantBucket(Guid tenantId)
    {
        var bytes = tenantId.ToByteArray();
        var hash = BitConverter.ToInt32(bytes, 0);
        return $"bucket_{Math.Abs(hash % 10)}";
    }
}
