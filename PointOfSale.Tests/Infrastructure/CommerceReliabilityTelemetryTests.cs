using System.Diagnostics.Metrics;
using PointOfSale.Infrastructure.Instrumentation;
using Xunit;

namespace PointOfSale.Tests.Infrastructure;

/// <summary>
/// Tests for CommerceReliabilityTelemetry to ensure metric dimensions are bounded
/// and protected against high-cardinality issues.
/// </summary>
public class CommerceReliabilityTelemetryTests : IDisposable
{
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly MeterListener _listener;
    private readonly List<(string MetricName, KeyValuePair<string, object?>[] Tags)> _recordedMeasurements;
    private readonly List<(string MetricName, KeyValuePair<string, object?>[] Tags)> _recordedDoubleMeasurements;

    public CommerceReliabilityTelemetryTests()
    {
        _recordedMeasurements = new List<(string, KeyValuePair<string, object?>[])>();
        _recordedDoubleMeasurements = new List<(string, KeyValuePair<string, object?>[])>();

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CommerceReliabilityTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            _recordedMeasurements.Add((instrument.Name, tags.ToArray()));
        });
        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            _recordedDoubleMeasurements.Add((instrument.Name, tags.ToArray()));
        });

        _listener.Start();
        _telemetry = new CommerceReliabilityTelemetry();
    }

    [Theory]
    [InlineData("valid_reason", "valid_reason")]
    [InlineData("UPPERCASE_REASON", "uppercase_reason")]
    [InlineData("Mixed-Case_Reason", "mixed-case_reason")]
    [InlineData("reason with spaces", "reason_with_spaces")]
    [InlineData("reason!@#$%^&*()+=[]{}|;:',.<>?/", "reason")]
    [InlineData("", "unknown")]
    [InlineData("   ", "unknown")]
    [InlineData(null, "unknown")]
    [InlineData("!@#$%^&*()", "invalid_reason")]
    public void RecordCheckoutFinalizeFailure_NormalizesReasonCorrectly(string? inputReason, string expectedNormalized)
    {
        // Act
        _telemetry.RecordCheckoutFinalizeFailure(inputReason!);

        // Assert
        var measurement = _recordedMeasurements.Single(m => m.MetricName == "checkout_finalize_failure_total");
        var reasonTag = measurement.Tags.Single(t => t.Key == "reason");
        Assert.Equal(expectedNormalized, reasonTag.Value);
    }

    [Fact]
    public void RecordCheckoutFinalizeFailure_TruncatesLongReasons()
    {
        // Arrange - Create a reason longer than 64 characters
        var longReason = new string('a', 100);

        // Act
        _telemetry.RecordCheckoutFinalizeFailure(longReason);

        // Assert
        var measurement = _recordedMeasurements.Single(m => m.MetricName == "checkout_finalize_failure_total");
        var reasonTag = measurement.Tags.Single(t => t.Key == "reason");
        var normalizedReason = reasonTag.Value as string;
        
        Assert.NotNull(normalizedReason);
        Assert.True(normalizedReason.Length <= 64, $"Reason length {normalizedReason.Length} exceeds maximum of 64");
        Assert.Equal(new string('a', 64), normalizedReason);
    }

    [Fact]
    public void RecordCheckoutSessionAuthorizationFailure_NormalizesReason()
    {
        // Arrange
        var dangerousReason = "Exception: System.InvalidOperationException at line 123";

        // Act
        _telemetry.RecordCheckoutSessionAuthorizationFailure("create_or_resume", dangerousReason);

        // Assert
        var measurement = _recordedMeasurements.Single(m => m.MetricName == "checkout_session_authorization_failure_total");
        var reasonTag = measurement.Tags.Single(t => t.Key == "reason");
        var normalizedReason = reasonTag.Value as string;
        
        Assert.NotNull(normalizedReason);
        Assert.DoesNotContain("Exception", normalizedReason);
        Assert.DoesNotContain(":", normalizedReason);
        Assert.DoesNotContain(".", normalizedReason);
        Assert.Equal("exception_systeminvalidoperationexception_at_line_123", normalizedReason);
    }

    [Fact]
    public void RecordCurrencyProjectionMismatch_NormalizesReason()
    {
        // Act
        _telemetry.RecordCurrencyProjectionMismatch("storefront_config", "Missing Currency!");

        // Assert
        var measurement = _recordedMeasurements.Single(m => m.MetricName == "currency_projection_mismatch_total");
        var reasonTag = measurement.Tags.Single(t => t.Key == "reason");
        Assert.Equal("missing_currency", reasonTag.Value);
    }

    [Fact]
    public void RecordSearchFailure_NormalizesReason()
    {
        // Act
        _telemetry.RecordSearchFailure("unified", "Connection Failed: Timeout");

        // Assert
        var measurement = _recordedMeasurements.Single(m => m.MetricName == "search_failure_total");
        var reasonTag = measurement.Tags.Single(t => t.Key == "reason");
        Assert.Equal("connection_failed_timeout", reasonTag.Value);
    }

    [Fact]
    public void ReviewedOperationalMetricDimensions_NormalizeRawStringTags()
    {
        _telemetry.RecordPaymentIntentRejected("Non Pending State!", "Paymob Manual!");
        _telemetry.RecordRejectedDuplicateIntent("Stripe Express", "Partially Refunded");
        _telemetry.RecordCheckoutSessionAuthorizationFailure("Create Or Resume", "Scope Mismatch!");
        _telemetry.RecordCheckoutSessionDuplicateDetected("Resume Checkout!", 2);
        _telemetry.RecordCheckoutSessionReconciliation("Resume Checkout!", "Invalidate Duplicates!", 2);
        _telemetry.RecordCheckoutSessionRacePrevented("Resume Checkout!", "Active Session Unique Index!");
        _telemetry.RecordProjectionSyncDispatched("Payment Methods Sync", 3);
        _telemetry.RecordSearchFailure("Storefront Search", "Timeout!");
        _telemetry.RecordOrphanFilesDeleted(1, "Local Legacy Disk");
        _telemetry.RecordShippingWebhookValidationFailure("Mylerz Carrier", "Invalid Signature!", "HMAC SHA256");
        _telemetry.RecordShippingWebhookDuplicate("Aramex Carrier", "Already Processed");
        _telemetry.RecordStockSyncFailure("After Persist", "Product Stock Changed", terminal: true);
        _telemetry.RecordStockSyncRetry("Product Stock Changed", 2);
        _telemetry.RecordStockDivergenceDetected("Mongo Search Overlay", "Mismatch");
        _telemetry.RecordPaymentTransactionDeadlock("Settle Payment", "Serializable Read Write");

        Assert.Equal(
            "paymob_manual",
            _recordedMeasurements.Single(m => m.MetricName == "payment_intent_rejected_total").Tags.Single(t => t.Key == "provider").Value);
        Assert.Equal(
            "stripe_express",
            _recordedMeasurements.Single(m => m.MetricName == "rejected_duplicate_intent_total").Tags.Single(t => t.Key == "provider").Value);
        Assert.Equal(
            "partially_refunded",
            _recordedMeasurements.Single(m => m.MetricName == "rejected_duplicate_intent_total").Tags.Single(t => t.Key == "existing_status").Value);
        Assert.Equal(
            "create_or_resume",
            _recordedMeasurements.Single(m => m.MetricName == "checkout_session_authorization_failure_total").Tags.Single(t => t.Key == "endpoint").Value);
        Assert.Equal(
            "resume_checkout",
            _recordedMeasurements.Single(m => m.MetricName == "checkout_session_duplicate_detected_total").Tags.Single(t => t.Key == "surface").Value);
        Assert.Equal(
            "resume_checkout",
            _recordedMeasurements.Single(m => m.MetricName == "checkout_session_reconciliation_total").Tags.Single(t => t.Key == "surface").Value);
        Assert.Equal(
            "resume_checkout",
            _recordedMeasurements.Single(m => m.MetricName == "checkout_session_race_prevented_total").Tags.Single(t => t.Key == "surface").Value);
        Assert.Equal(
            "payment_methods_sync",
            _recordedMeasurements.Single(m => m.MetricName == "projection_sync_dispatched_total").Tags.Single(t => t.Key == "projection").Value);
        Assert.Equal(
            "storefront_search",
            _recordedMeasurements.Single(m => m.MetricName == "search_failure_total").Tags.Single(t => t.Key == "surface").Value);
        Assert.Equal(
            "local_legacy_disk",
            _recordedMeasurements.Single(m => m.MetricName == "orphan_files_deleted_total").Tags.Single(t => t.Key == "storage_provider").Value);
        Assert.Equal(
            "mylerz_carrier",
            _recordedMeasurements.Single(m => m.MetricName == "shipping_webhook_validation_failure_total").Tags.Single(t => t.Key == "carrier").Value);
        Assert.Equal(
            "hmac_sha256",
            _recordedMeasurements.Single(m => m.MetricName == "shipping_webhook_validation_failure_total").Tags.Single(t => t.Key == "validation_method").Value);
        Assert.Equal(
            "aramex_carrier",
            _recordedMeasurements.Single(m => m.MetricName == "shipping_webhook_duplicate_total").Tags.Single(t => t.Key == "carrier").Value);
        Assert.Equal(
            "after_persist",
            _recordedMeasurements.Single(m => m.MetricName == "stock_sync_failure_total").Tags.Single(t => t.Key == "stage").Value);
        Assert.Equal(
            "product_stock_changed",
            _recordedMeasurements.Single(m => m.MetricName == "stock_sync_failure_total").Tags.Single(t => t.Key == "event_type").Value);
        Assert.Equal(
            "product_stock_changed",
            _recordedMeasurements.Single(m => m.MetricName == "stock_sync_retry_total").Tags.Single(t => t.Key == "event_type").Value);
        Assert.Equal(
            "mongo_search_overlay",
            _recordedMeasurements.Single(m => m.MetricName == "stock_divergence_detected_total").Tags.Single(t => t.Key == "surface").Value);
        Assert.Equal(
            "serializable_read_write",
            _recordedMeasurements.Single(m => m.MetricName == "payment_transaction_deadlock_total").Tags.Single(t => t.Key == "isolation_level").Value);
    }

    [Fact]
    public void RecordStockSyncDeliveryLag_NormalizesStage()
    {
        _telemetry.RecordStockSyncDeliveryLag("After Persist", 12.5d);

        var measurement = _recordedDoubleMeasurements.Single(m => m.MetricName == "stock_sync_delivery_lag_seconds");
        Assert.Equal("after_persist", measurement.Tags.Single(t => t.Key == "stage").Value);
    }

    [Fact]
    public void RecordShippingWebhookValidationFailure_NormalizesReason()
    {
        // Act
        _telemetry.RecordShippingWebhookValidationFailure("aramex", "Invalid Signature!", "hmac_sha256");

        // Assert
        var measurement = _recordedMeasurements.Single(m => m.MetricName == "shipping_webhook_validation_failure_total");
        var reasonTag = measurement.Tags.Single(t => t.Key == "reason");
        Assert.Equal("invalid_signature", reasonTag.Value);
    }

    [Fact]
    public void RecordShippingWebhookDuplicate_NormalizesReason()
    {
        // Act
        _telemetry.RecordShippingWebhookDuplicate("bosta", "Already Processed");

        // Assert
        var measurement = _recordedMeasurements.Single(m => m.MetricName == "shipping_webhook_duplicate_total");
        var reasonTag = measurement.Tags.Single(t => t.Key == "reason");
        Assert.Equal("already_processed", reasonTag.Value);
    }

    [Fact]
    public void RecordShippingWebhookTenantResolutionFailed_NormalizesReasonAndSource()
    {
        _telemetry.RecordShippingWebhookTenantResolutionFailed("Mylerz", "Tenant Resolution Ambiguous!", "Tracking Number");

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "shipping_webhook_tenant_resolution_failed_total");
        Assert.Equal("tenant_resolution_ambiguous", measurement.Tags.Single(t => t.Key == "reason").Value);
        Assert.Equal("tracking_number", measurement.Tags.Single(t => t.Key == "resolution_source").Value);
    }

    [Fact]
    public void RecordGenericWebhookHardeningMetrics_NormalizesTags()
    {
        _telemetry.RecordWebhookPayloadRejected("Stripe", "Invalid Content Type!", "Payment");
        _telemetry.RecordWebhookRateLimited("Paymob", "Tenant A", "Payment");
        _telemetry.RecordWebhookInvalidSignature("Mylerz", "Missing Signature!", "Shipping");
        _telemetry.RecordWebhookSizeLimitExceeded("DHL", "Shipping");

        var rejected = _recordedMeasurements.Single(m => m.MetricName == "webhook_payload_rejected_total");
        Assert.Equal("stripe", rejected.Tags.Single(t => t.Key == "provider").Value);
        Assert.Equal("invalid_content_type", rejected.Tags.Single(t => t.Key == "reason").Value);
        Assert.Equal("payment", rejected.Tags.Single(t => t.Key == "kind").Value);

        var rateLimited = _recordedMeasurements.Single(m => m.MetricName == "webhook_rate_limited_total");
        Assert.Equal("paymob", rateLimited.Tags.Single(t => t.Key == "provider").Value);
        Assert.Equal("tenant_a", rateLimited.Tags.Single(t => t.Key == "tenant_partition").Value);
        Assert.Equal("payment", rateLimited.Tags.Single(t => t.Key == "kind").Value);

        var invalidSignature = _recordedMeasurements.Single(m => m.MetricName == "webhook_invalid_signature_total");
        Assert.Equal("mylerz", invalidSignature.Tags.Single(t => t.Key == "provider").Value);
        Assert.Equal("missing_signature", invalidSignature.Tags.Single(t => t.Key == "reason").Value);
        Assert.Equal("shipping", invalidSignature.Tags.Single(t => t.Key == "kind").Value);

        var sizeLimit = _recordedMeasurements.Single(m => m.MetricName == "webhook_size_limit_exceeded_total");
        Assert.Equal("dhl", sizeLimit.Tags.Single(t => t.Key == "provider").Value);
        Assert.Equal("shipping", sizeLimit.Tags.Single(t => t.Key == "kind").Value);
    }

    [Fact]
    public void RecordShippingSyncFailed_NormalizesReason()
    {
        var tenantId = Guid.NewGuid();
        var expectedTenantBucket = GetTenantBucket(tenantId);

        _telemetry.RecordShippingSyncFailed(tenantId, "Outbox Commit Failed!");

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "shipping_sync_failed_total");
        Assert.Equal(expectedTenantBucket, measurement.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("outbox_commit_failed", measurement.Tags.Single(t => t.Key == "reason").Value);
    }

    [Fact]
    public void RecordOperationalOutcomeMetrics_NormalizeTags()
    {
        var tenantId = Guid.NewGuid();
        var expectedTenantBucket = GetTenantBucket(tenantId);

        _telemetry.RecordCheckoutProcessed(tenantId, "Cash On Delivery", "Success");
        _telemetry.RecordPaymentOutcome(tenantId, "Paymob", "Credit Card", "Failed");
        _telemetry.RecordWebhookProcessing("Stripe", "Payment", "Processed");
        _telemetry.RecordCouponConsumed(tenantId, "Checkout");
        _telemetry.RecordBackgroundJobExecution("Redirect Url Recovery", "Dead Lettered");
        _telemetry.RecordAccountingOutboxDelivery(tenantId, "Payment Captured", "Retry");

        var checkout = _recordedMeasurements.Single(m => m.MetricName == "checkout_processed_total");
        Assert.Equal(expectedTenantBucket, checkout.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("cash_on_delivery", checkout.Tags.Single(t => t.Key == "payment_method").Value);
        Assert.Equal("success", checkout.Tags.Single(t => t.Key == "outcome").Value);

        var payment = _recordedMeasurements.Single(m => m.MetricName == "payment_outcome_total");
        Assert.Equal("paymob", payment.Tags.Single(t => t.Key == "provider").Value);
        Assert.Equal("credit_card", payment.Tags.Single(t => t.Key == "payment_method").Value);
        Assert.Equal("failed", payment.Tags.Single(t => t.Key == "outcome").Value);

        var webhook = _recordedMeasurements.Single(m => m.MetricName == "webhook_processing_total");
        Assert.Equal("stripe", webhook.Tags.Single(t => t.Key == "provider").Value);
        Assert.Equal("payment", webhook.Tags.Single(t => t.Key == "kind").Value);
        Assert.Equal("processed", webhook.Tags.Single(t => t.Key == "outcome").Value);

        var coupon = _recordedMeasurements.Single(m => m.MetricName == "coupon_consumed_total");
        Assert.Equal(expectedTenantBucket, coupon.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("checkout", coupon.Tags.Single(t => t.Key == "scope").Value);

        var backgroundJob = _recordedMeasurements.Single(m => m.MetricName == "background_job_execution_total");
        Assert.Equal("redirect_url_recovery", backgroundJob.Tags.Single(t => t.Key == "job").Value);
        Assert.Equal("dead_lettered", backgroundJob.Tags.Single(t => t.Key == "outcome").Value);

        var accounting = _recordedMeasurements.Single(m => m.MetricName == "accounting_outbox_delivery_total");
        Assert.Equal(expectedTenantBucket, accounting.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("payment_captured", accounting.Tags.Single(t => t.Key == "event_type").Value);
        Assert.Equal("retry", accounting.Tags.Single(t => t.Key == "outcome").Value);
    }

    [Fact]
    public void RecordCouponLifecycleMetrics_NormalizeTags()
    {
        var tenantId = Guid.NewGuid();
        var expectedTenantBucket = GetTenantBucket(tenantId);

        _telemetry.RecordCouponReservationCreated(tenantId, "Checkout");
        _telemetry.RecordCouponReservationReleased(tenantId, "Payment Failure", "Reservation Expired");
        _telemetry.RecordCouponUsageConfirmed(tenantId, "Payment Success");
        _telemetry.RecordCouponSplitBrainDetected(tenantId, "Coupon Validation Service", "Discount Mismatch");

        var created = _recordedMeasurements.Single(m => m.MetricName == "coupon_reservation_created_total");
        Assert.Equal(expectedTenantBucket, created.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("checkout", created.Tags.Single(t => t.Key == "scope").Value);

        var released = _recordedMeasurements.Single(m => m.MetricName == "coupon_reservation_released_total");
        Assert.Equal(expectedTenantBucket, released.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("payment_failure", released.Tags.Single(t => t.Key == "scope").Value);
        Assert.Equal("reservation_expired", released.Tags.Single(t => t.Key == "reason").Value);

        var confirmed = _recordedMeasurements.Single(m => m.MetricName == "coupon_usage_confirmed_total");
        Assert.Equal(expectedTenantBucket, confirmed.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("payment_success", confirmed.Tags.Single(t => t.Key == "scope").Value);

        var splitBrain = _recordedMeasurements.Single(m => m.MetricName == "coupon_split_brain_detected_total");
        Assert.Equal(expectedTenantBucket, splitBrain.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("coupon_validation_service", splitBrain.Tags.Single(t => t.Key == "surface").Value);
        Assert.Equal("discount_mismatch", splitBrain.Tags.Single(t => t.Key == "reason").Value);
    }

    [Fact]
    public void RecordStockDivergenceDetected_NormalizesReason()
    {
        // Act
        _telemetry.RecordStockDivergenceDetected("cart", "Mongo SQL Stock Mismatch");

        // Assert
        var measurement = _recordedMeasurements.Single(m => m.MetricName == "stock_divergence_detected_total");
        var reasonTag = measurement.Tags.Single(t => t.Key == "reason");
        Assert.Equal("mongo_sql_stock_mismatch", reasonTag.Value);
    }

    [Fact]
    public void RecordPaymentIntentRejected_NormalizesReason()
    {
        // Act
        _telemetry.RecordPaymentIntentRejected("Non-Pending State!", "paymob");

        // Assert
        var measurement = _recordedMeasurements.Single(m => m.MetricName == "payment_intent_rejected_total");
        var reasonTag = measurement.Tags.Single(t => t.Key == "reason");
        Assert.Equal("non-pending_state", reasonTag.Value);
    }

    [Theory]
    [InlineData("replay", "replay")]
    [InlineData("RePlay", "replay")]
    [InlineData("re play ", "re_play")]
    [InlineData("re-play!", "re-play")]
    [InlineData("RE PLAY", "re_play")]
    public void RecordCheckoutIdempotencyReuse_EmitsSourceTag(string input, string expected)
    {
        _telemetry.RecordCheckoutIdempotencyReuse(input);

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "checkout_idempotency_reuse_total");
        Assert.Equal(expected, measurement.Tags.Single(t => t.Key == "source").Value);
    }

    [Theory]
    [InlineData("db_unique_guard", "db_unique_guard")]
    [InlineData("DB_Unique_Guard", "db_unique_guard")]
    [InlineData("db unique guard ", "db_unique_guard")]
    [InlineData("db-unique-guard!", "db-unique-guard")]
    [InlineData("DB UNIQUE GUARD", "db_unique_guard")]
    public void RecordCheckoutDuplicateOrderPrevented_EmitsSourceTag(string input, string expected)
    {
        _telemetry.RecordCheckoutDuplicateOrderPrevented(input);

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "checkout_duplicate_order_prevented_total");
        Assert.Equal(expected, measurement.Tags.Single(t => t.Key == "source").Value);
    }

    [Theory]
    [InlineData("add_item", "add_item")]
    [InlineData("Add_Item", "add_item")]
    [InlineData("add item ", "add_item")]
    [InlineData("add-item!", "add-item")]
    [InlineData("ADD ITEM", "add_item")]
    public void RecordCartConcurrencyConflict_EmitsOperationTag(string input, string expected)
    {
        _telemetry.RecordCartConcurrencyConflict(input);

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "cart_concurrency_conflict_total");
        Assert.Equal(expected, measurement.Tags.Single(t => t.Key == "operation").Value);
    }

    [Theory]
    [InlineData("merge_cart", "merge_cart")]
    [InlineData("Merge_Cart", "merge_cart")]
    [InlineData("merge cart ", "merge_cart")]
    [InlineData("merge-cart!", "merge-cart")]
    [InlineData("MERGE CART", "merge_cart")]
    public void RecordCartUpdateLostPrevented_EmitsOperationTag(string input, string expected)
    {
        _telemetry.RecordCartUpdateLostPrevented(input);

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "cart_update_lost_prevented_total");
        Assert.Equal(expected, measurement.Tags.Single(t => t.Key == "operation").Value);
    }

    [Fact]
    public void RecordPromotionEngineFailure_NormalizesSurfaceAndReason()
    {
        _telemetry.RecordPromotionEngineFailure("Checkout Preview", "Invalid Operation Exception");

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "promotion_engine_failure_total");
        Assert.Equal("checkout_preview", measurement.Tags.Single(t => t.Key == "surface").Value);
        Assert.Equal("invalid_operation_exception", measurement.Tags.Single(t => t.Key == "reason").Value);
    }

    [Fact]
    public void RecordCouponUsageLimitReached_EmitsTenantAndScope()
    {
        var tenantId = Guid.NewGuid();
        var expectedTenantBucket = GetTenantBucket(tenantId);

        _telemetry.RecordCouponUsageLimitReached(tenantId, "Per Customer");

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "coupon_usage_limit_reached_total");
        Assert.Equal(expectedTenantBucket, measurement.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("per_customer", measurement.Tags.Single(t => t.Key == "scope").Value);
    }

    [Fact]
    public void RecordCouponInvalidConfiguration_NormalizesSurfaceAndReason()
    {
        var tenantId = Guid.NewGuid();

        _telemetry.RecordCouponInvalidConfiguration(tenantId, "Admin Create", "Max Usage Per Customer > Max Usage");

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "coupon_invalid_configuration_total");
        Assert.Equal("admin_create", measurement.Tags.Single(t => t.Key == "surface").Value);
        Assert.Equal("max_usage_per_customer__max_usage", measurement.Tags.Single(t => t.Key == "reason").Value);
    }

    [Fact]
    public void RecordCheckoutTotalMismatch_NormalizesField()
    {
        var tenantId = Guid.NewGuid();

        _telemetry.RecordCheckoutTotalMismatch(tenantId, "Coupon Discount");

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "checkout_total_mismatch_total");
        Assert.Equal("coupon_discount", measurement.Tags.Single(t => t.Key == "field").Value);
    }

    [Fact]
    public void RecordOtpRequest_NormalizesOutcomeAndReason()
    {
        var tenantId = Guid.NewGuid();
        var expectedTenantBucket = GetTenantBucket(tenantId);

        _telemetry.RecordOtpRequest(tenantId, "WhatsApp", "Accepted", "Existing Code Reused!");

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "otp_request_total");
        Assert.Equal(expectedTenantBucket, measurement.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("whatsapp", measurement.Tags.Single(t => t.Key == "channel").Value);
        Assert.Equal("accepted", measurement.Tags.Single(t => t.Key == "outcome").Value);
        Assert.Equal("existing_code_reused", measurement.Tags.Single(t => t.Key == "reason").Value);
    }

    [Fact]
    public void RecordOtpDispatchFailed_NormalizesReason()
    {
        var tenantId = Guid.NewGuid();

        _telemetry.RecordOtpDispatchFailed(tenantId, "whatsapp", "Feature Gate Disabled");

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "otp_dispatch_failed_total");
        Assert.Equal("feature_gate_disabled", measurement.Tags.Single(t => t.Key == "reason").Value);
    }

    [Fact]
    public void RecordOtpVerificationFailed_NormalizesReason()
    {
        var tenantId = Guid.NewGuid();

        _telemetry.RecordOtpVerificationFailed(tenantId, "Invalid OTP!");

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "otp_verification_failed_total");
        Assert.Equal("invalid_otp", measurement.Tags.Single(t => t.Key == "reason").Value);
    }

    [Fact]
    public void RecordRecoveryAndOtpAbuseMetrics_NormalizeTags()
    {
        var tenantId = Guid.NewGuid();

        _telemetry.RecordAbandonedCartSkipped(tenantId, "Checkout Activity", "Publish Failed!");
        _telemetry.RecordRecoverySkippedNoContact(tenantId, "No Verified Contact!");
        _telemetry.RecordOtpRateLimited(tenantId, "Phone Number", "OTP Request Rate Limit");
        _telemetry.RecordOtpVerificationBlocked(tenantId, "OTP Attempts Exhausted");
        _telemetry.RecordOtpAbuseDetected(tenantId, "Verification", "OTP Attempts Exhausted");

        Assert.Equal(
            "publish_failed",
            _recordedMeasurements.Single(m => m.MetricName == "abandoned_cart_skipped_total").Tags.Single(t => t.Key == "reason").Value);
        Assert.Equal(
            "no_verified_contact",
            _recordedMeasurements.Single(m => m.MetricName == "recovery_skipped_no_contact_total").Tags.Single(t => t.Key == "reason").Value);
        Assert.Equal(
            "phone_number",
            _recordedMeasurements.Single(m => m.MetricName == "otp_rate_limited_total").Tags.Single(t => t.Key == "scope").Value);
        Assert.Equal(
            "otp_attempts_exhausted",
            _recordedMeasurements.Single(m => m.MetricName == "otp_verification_blocked_total").Tags.Single(t => t.Key == "reason").Value);
        Assert.Equal(
            "verification",
            _recordedMeasurements.Single(m => m.MetricName == "otp_abuse_detected_total").Tags.Single(t => t.Key == "vector").Value);
    }

    [Fact]
    public void RecordCheckoutSessionDuplicateDetected_RecordsMetricWithSurface()
    {
        // Act
        _telemetry.RecordCheckoutSessionDuplicateDetected("create_or_resume", 3);

        // Assert
        var measurement = _recordedMeasurements.Single(m => m.MetricName == "checkout_session_duplicate_detected_total");
        var surfaceTag = measurement.Tags.Single(t => t.Key == "surface");
        Assert.Equal("create_or_resume", surfaceTag.Value);
    }

    [Fact]
    public void RecordCheckoutSessionReconciliation_RecordsMetricWithNormalizedAction()
    {
        // Act
        _telemetry.RecordCheckoutSessionReconciliation("convert", "Invalidate Duplicates!", 2);

        // Assert
        var measurement = _recordedMeasurements.Single(m => m.MetricName == "checkout_session_reconciliation_total");
        var surfaceTag = measurement.Tags.Single(t => t.Key == "surface");
        var actionTag = measurement.Tags.Single(t => t.Key == "action");
        
        Assert.Equal("convert", surfaceTag.Value);
        Assert.NotNull(actionTag.Value);
        Assert.Equal("invalidate_duplicates", actionTag.Value);
    }

    [Fact]
    public void RecordCheckoutSessionRacePrevented_RecordsMetricWithNormalizedMechanism()
    {
        // Act
        _telemetry.RecordCheckoutSessionRacePrevented("create_or_resume", "Active Session Unique Index!");

        // Assert
        var measurement = _recordedMeasurements.Single(m => m.MetricName == "checkout_session_race_prevented_total");
        var surfaceTag = measurement.Tags.Single(t => t.Key == "surface");
        var mechanismTag = measurement.Tags.Single(t => t.Key == "mechanism");
        
        Assert.Equal("create_or_resume", surfaceTag.Value);
        Assert.NotNull(mechanismTag.Value);
        Assert.Equal("active_session_unique_index", mechanismTag.Value);
    }

    [Fact]
    public void RecordAuthStateDegraded_NormalizesTags()
    {
        _telemetry.RecordAuthStateDegraded("Silent Retry", "NETWORK ERROR!");

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "auth_state_degraded_total");
        Assert.Equal("silent_retry", measurement.Tags.Single(t => t.Key == "source").Value);
        Assert.Equal("network_error", measurement.Tags.Single(t => t.Key == "reason").Value);
    }

    [Fact]
    public void RecordCheckoutSelectionInvalidated_NormalizesTags()
    {
        _telemetry.RecordCheckoutSelectionInvalidated("Shipping Method", "Missing From Latest Config!");

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "checkout_selection_invalidated_total");
        Assert.Equal("shipping_method", measurement.Tags.Single(t => t.Key == "selection_type").Value);
        Assert.Equal("missing_from_latest_config", measurement.Tags.Single(t => t.Key == "reason").Value);
    }

    [Fact]
    public void RecordCartStateMismatchDetected_NormalizesTags()
    {
        _telemetry.RecordCartStateMismatchDetected("Cart Store", "Subtotal Mismatch!");

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "cart_state_mismatch_detected_total");
        Assert.Equal("cart_store", measurement.Tags.Single(t => t.Key == "surface").Value);
        Assert.Equal("subtotal_mismatch", measurement.Tags.Single(t => t.Key == "reason").Value);
    }

    [Fact]
    public void RecordCheckoutReliabilityRecoveryMetrics_NormalizeTags()
    {
        var tenantId = Guid.NewGuid();
        var expectedTenantBucket = GetTenantBucket(tenantId);

        _telemetry.RecordCheckoutEventPublishFailed("Create Or Resume", "Session Started", "Outbox Flush");
        _telemetry.RecordCheckoutFalseFailure("Update", "Dispatch Failure After Persist");
        _telemetry.RecordCheckoutPartialSuccess("Create Or Resume", "Async Reconciliation");
        _telemetry.RecordRecoveryCartRestored(tenantId, "Stored Snapshot");
        _telemetry.RecordRecoveryCartRestoreFailed(tenantId, "Snapshot Deserialize Failed");

        var publishFailure = _recordedMeasurements.Single(m => m.MetricName == "checkout_event_publish_failed_total");
        Assert.Equal("create_or_resume", publishFailure.Tags.Single(t => t.Key == "operation").Value);
        Assert.Equal("session_started", publishFailure.Tags.Single(t => t.Key == "stage").Value);
        Assert.Equal("outbox_flush", publishFailure.Tags.Single(t => t.Key == "dispatch_mode").Value);

        var falseFailure = _recordedMeasurements.Single(m => m.MetricName == "checkout_false_failure_total");
        Assert.Equal("update", falseFailure.Tags.Single(t => t.Key == "endpoint").Value);
        Assert.Equal("dispatch_failure_after_persist", falseFailure.Tags.Single(t => t.Key == "reason").Value);

        var partialSuccess = _recordedMeasurements.Single(m => m.MetricName == "checkout_partial_success_total");
        Assert.Equal("create_or_resume", partialSuccess.Tags.Single(t => t.Key == "endpoint").Value);
        Assert.Equal("async_reconciliation", partialSuccess.Tags.Single(t => t.Key == "recovery").Value);

        var restored = _recordedMeasurements.Single(m => m.MetricName == "recovery_cart_restored_total");
        Assert.Equal(expectedTenantBucket, restored.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("stored_snapshot", restored.Tags.Single(t => t.Key == "source").Value);

        var restoreFailed = _recordedMeasurements.Single(m => m.MetricName == "recovery_cart_restore_failed_total");
        Assert.Equal(expectedTenantBucket, restoreFailed.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.DoesNotContain(restoreFailed.Tags, tag => tag.Key == "tenant_id");
        Assert.Equal("snapshot_deserialize_failed", restoreFailed.Tags.Single(t => t.Key == "reason").Value);
    }

    [Fact]
    public void CurrencyRecoveryMetrics_DoNotEmitRawTenantOrOrderIdentifiers()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var expectedTenantBucket = GetTenantBucket(tenantId);

        _telemetry.RecordCurrencyMissing(tenantId, orderId, "Payment Recovery!");

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "currency_missing_total");
        Assert.Equal(expectedTenantBucket, measurement.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal(true, measurement.Tags.Single(t => t.Key == "tenant_present").Value);
        Assert.Equal(true, measurement.Tags.Single(t => t.Key == "order_present").Value);
        Assert.Equal("payment_recovery", measurement.Tags.Single(t => t.Key == "source").Value);
        Assert.DoesNotContain(measurement.Tags, tag => tag.Key == "tenant_id");
        Assert.DoesNotContain(measurement.Tags, tag => tag.Key == "order_id");
    }

    [Fact]
    public void RecordCmsConflictDetected_NormalizesEntityType()
    {
        _telemetry.RecordCmsConflictDetected("CMS Page");

        var measurement = _recordedMeasurements.Single(m => m.MetricName == "cms_conflict_detected_total");
        Assert.Equal("cms_page", measurement.Tags.Single(t => t.Key == "entity_type").Value);
    }

    [Fact]
    public void RecordShipmentAndStorefrontRecoveryMetrics_NormalizeTags()
    {
        var tenantId = Guid.NewGuid();

        _telemetry.RecordShipmentExternalCallAfterCommit(tenantId, "Mylerz", "Retryable Failure");
        _telemetry.RecordShipmentReconciliation(tenantId, "Persist Retry", "Post Carrier Save Failed");
        _telemetry.RecordGuestPaymentRecoverySuccess("Volatile Checkout Token");
        _telemetry.RecordCartUnexpectedClearBlocked("Mismatched Active Order Context");
        _telemetry.RecordTrackingTokenRemovedFromUrl("Query");
        _telemetry.RecordTrackingTokenInvalidAccess(tenantId, "Invalid Token");
        _telemetry.RecordGuestTrackingDenied(tenantId, "Missing Token");
        _telemetry.RecordAuthDegradedRecovery("Scheduled Retry", "Recovered");
        _telemetry.RecordCheckoutStateLeakPrevented("User Switch");

        // Compute expected tenant bucket using same logic as production
        var expectedTenantBucket = GetTenantBucket(tenantId);

        var externalCall = _recordedMeasurements.Single(m => m.MetricName == "shipment_external_call_after_commit_total");
        Assert.Equal(expectedTenantBucket, externalCall.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("mylerz", externalCall.Tags.Single(t => t.Key == "carrier").Value);
        Assert.Equal("retryable_failure", externalCall.Tags.Single(t => t.Key == "outcome").Value);

        var reconciliation = _recordedMeasurements.Single(m => m.MetricName == "shipment_reconciliation_total");
        Assert.Equal(expectedTenantBucket, reconciliation.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("persist_retry", reconciliation.Tags.Single(t => t.Key == "action").Value);
        Assert.Equal("post_carrier_save_failed", reconciliation.Tags.Single(t => t.Key == "reason").Value);

        var guestRecovery = _recordedMeasurements.Single(m => m.MetricName == "guest_payment_recovery_success_total");
        Assert.Equal("volatile_checkout_token", guestRecovery.Tags.Single(t => t.Key == "source").Value);

        var blockedClear = _recordedMeasurements.Single(m => m.MetricName == "cart_unexpected_clear_blocked_total");
        Assert.Equal("mismatched_active_order_context", blockedClear.Tags.Single(t => t.Key == "reason").Value);

        var removedToken = _recordedMeasurements.Single(m => m.MetricName == "tracking_token_removed_from_url_total");
        Assert.Equal("query", removedToken.Tags.Single(t => t.Key == "source").Value);

        var invalidToken = _recordedMeasurements.Single(m => m.MetricName == "tracking_token_invalid_access_total");
        Assert.Equal(expectedTenantBucket, invalidToken.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("invalid_token", invalidToken.Tags.Single(t => t.Key == "reason").Value);

        var deniedTracking = _recordedMeasurements.Single(m => m.MetricName == "guest_tracking_denied_total");
        Assert.Equal(expectedTenantBucket, deniedTracking.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("missing_token", deniedTracking.Tags.Single(t => t.Key == "reason").Value);

        var degradedRecovery = _recordedMeasurements.Single(m => m.MetricName == "auth_degraded_recovery_total");
        Assert.Equal("scheduled_retry", degradedRecovery.Tags.Single(t => t.Key == "source").Value);
        Assert.Equal("recovered", degradedRecovery.Tags.Single(t => t.Key == "outcome").Value);

        var checkoutLeak = _recordedMeasurements.Single(m => m.MetricName == "checkout_state_leak_prevented_total");
        Assert.Equal("user_switch", checkoutLeak.Tags.Single(t => t.Key == "trigger").Value);
    }

    [Fact]
    public void RecordSecurityHardeningMetrics_NormalizeTags()
    {
        var tenantId = Guid.NewGuid();
        var expectedTenantBucket = GetTenantBucket(tenantId);

        _telemetry.RecordTenantAccessViolation("Smart Offer Redeem Tenant Claim Mismatch", "/api/storefront/smart-offers/redeem");
        _telemetry.RecordPromotionFailClosed(tenantId, "Checkout", "Promotion Engine Failure");
        _telemetry.RecordCouponAbuseBlocked("Spoofed Header", "Rate Limit Bypass");
        _telemetry.RecordSmartOfferAbuseBlocked("Raw Coupon Code", "Rejected");
        _telemetry.RecordAdminConflictDetected("Store Config", "Stale Row Version");

        var tenantViolation = _recordedMeasurements.Single(m => m.MetricName == "tenant_access_violation_total");
        Assert.Equal("smart_offer_redeem_tenant_claim_mismatch", tenantViolation.Tags.Single(t => t.Key == "violation_type").Value);
        Assert.Equal("/api/storefront/smart-offers/redeem", tenantViolation.Tags.Single(t => t.Key == "path").Value);

        var promotionFailClosed = _recordedMeasurements.Single(m => m.MetricName == "promotion_fail_closed_total");
        Assert.Equal(expectedTenantBucket, promotionFailClosed.Tags.Single(t => t.Key == "tenant_bucket").Value);
        Assert.Equal("checkout", promotionFailClosed.Tags.Single(t => t.Key == "surface").Value);
        Assert.Equal("promotion_engine_failure", promotionFailClosed.Tags.Single(t => t.Key == "reason").Value);

        var couponAbuse = _recordedMeasurements.Single(m => m.MetricName == "coupon_abuse_blocked_total");
        Assert.Equal("spoofed_header", couponAbuse.Tags.Single(t => t.Key == "vector").Value);
        Assert.Equal("rate_limit_bypass", couponAbuse.Tags.Single(t => t.Key == "reason").Value);

        var smartOfferAbuse = _recordedMeasurements.Single(m => m.MetricName == "smart_offer_abuse_blocked_total");
        Assert.Equal("raw_coupon_code", smartOfferAbuse.Tags.Single(t => t.Key == "vector").Value);
        Assert.Equal("rejected", smartOfferAbuse.Tags.Single(t => t.Key == "reason").Value);

        var adminConflict = _recordedMeasurements.Single(m => m.MetricName == "admin_conflict_detected_total");
        Assert.Equal("store_config", adminConflict.Tags.Single(t => t.Key == "resource").Value);
        Assert.Equal("stale_row_version", adminConflict.Tags.Single(t => t.Key == "reason").Value);
    }

    /// <summary>
    /// Computes tenant bucket using same logic as CreateTenantBucketTag in production.
    /// Maps tenant GUIDs to a fixed set of 10 buckets (bucket_0 through bucket_9).
    /// 
    /// IMPORTANT: This method intentionally mirrors the production CreateTenantBucketTag implementation.
    /// Keep both methods in sync - any changes to the bucketing algorithm must be applied to both.
    /// </summary>
    private static string GetTenantBucket(Guid tenantId)
    {
        var bytes = tenantId.ToByteArray();
        var hash = BitConverter.ToInt32(bytes, 0);
        var bucket = Math.Abs(hash % 10);
        return $"bucket_{bucket}";
    }

    public void Dispose()
    {
        _listener.Dispose();
        _telemetry.Dispose();
    }
}
