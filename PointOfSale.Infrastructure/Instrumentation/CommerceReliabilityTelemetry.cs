using System.Diagnostics.Metrics;

namespace PointOfSale.Infrastructure.Instrumentation;

public sealed class CommerceReliabilityTelemetry : IDisposable
{
    public const string MeterName = "PointOfSale.Commerce.Reliability";

    private const int MaxReasonLength = 64;
    private static readonly HashSet<char> AllowedReasonChars = new(
        "abcdefghijklmnopqrstuvwxyz0123456789_-".ToCharArray());

    /// <summary>
    /// Normalizes a reason string to prevent high-cardinality metric dimensions.
    /// Ensures the reason is lowercase, alphanumeric with underscores/hyphens only,
    /// and truncated to a safe length. This prevents exception messages, stack traces,
    /// or dynamic values from creating unbounded time series.
    /// </summary>
    /// <param name="reason">The raw reason string from caller</param>
    /// <returns>A normalized, bounded reason string safe for metric dimensions</returns>
    private static string NormalizeReasonForTelemetry(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "unknown";
        }

        var normalized = reason.Trim().ToLowerInvariant();

        // Replace spaces with underscores
        normalized = normalized.Replace(' ', '_');

        // Filter to allowed characters only
        var filtered = new string(normalized.Where(c => AllowedReasonChars.Contains(c)).ToArray());

        if (string.IsNullOrEmpty(filtered))
        {
            return "invalid_reason";
        }

        // Truncate to max length
        return filtered.Length > MaxReasonLength
            ? filtered[..MaxReasonLength]
            : filtered;
    }

    /// <summary>
    /// Normalizes a URL path to prevent high-cardinality metric dimensions by collapsing
    /// dynamic segments (GUIDs, numeric IDs) into placeholders.
    /// </summary>
    /// <param name="path">The raw URL path</param>
    /// <returns>A normalized path with dynamic segments replaced by placeholders</returns>
    private static string NormalizePathForTelemetry(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "unknown";
        }

        var segments = path.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalizedSegments = new List<string>(segments.Length);

        foreach (var segment in segments)
        {
            // Check if segment is a GUID (with or without hyphens)
            if (Guid.TryParse(segment, out _))
            {
                normalizedSegments.Add("{guid}");
            }
            // Check if segment is purely numeric (likely an ID)
            else if (segment.All(char.IsDigit))
            {
                normalizedSegments.Add("{id}");
            }
            else
            {
                // Keep the segment as-is, but normalize it
                normalizedSegments.Add(segment.ToLowerInvariant());
            }
        }

        var normalized = "/" + string.Join("/", normalizedSegments);
        
        // Truncate if too long
        return normalized.Length > MaxReasonLength
            ? normalized[..MaxReasonLength]
            : normalized;
    }

    private readonly Meter _meter;

    public Counter<long> PaymentIntentRejectedTotal { get; }
    public Counter<long> PaymentConfigResetTotal { get; }
    public Counter<long> PaymentConfigClearedTotal { get; }
    public Counter<long> PaymentConfigInvalidTotal { get; }
    public Counter<long> PaymentMethodExposedInvalidTotal { get; }
    public Counter<long> RejectedDuplicateIntentTotal { get; }
    public Counter<long> PaymentAfterExpiryDetectedTotal { get; }
    public Counter<long> PaymentIntentDuplicateAttemptTotal { get; }
    public Counter<long> WebhookLatePaymentTotal { get; }
    public Counter<long> GuestTrackingUnauthorizedAttemptTotal { get; }
    public Counter<long> CheckoutFinalizeFailureTotal { get; }
    public Counter<long> CheckoutSessionAuthorizationFailureTotal { get; }
    public Counter<long> CheckoutSessionDuplicateDetectedTotal { get; }
    public Counter<long> CheckoutSessionReconciliationTotal { get; }
    public Counter<long> CheckoutSessionRacePreventedTotal { get; }
    public Counter<long> CheckoutEventPublishFailedTotal { get; }
    public Counter<long> CheckoutFalseFailureTotal { get; }
    public Counter<long> CheckoutPartialSuccessTotal { get; }
    public Counter<long> AuthStateDegradedTotal { get; }
    public Counter<long> AuthDegradedRecoveryTotal { get; }
    public Counter<long> CheckoutSelectionInvalidatedTotal { get; }
    public Counter<long> CartStateMismatchDetectedTotal { get; }
    public Counter<long> CartConcurrencyConflictTotal { get; }
    public Counter<long> CartUpdateLostPreventedTotal { get; }
    public Histogram<double> CartMutationLockDurationSeconds { get; }
    public Counter<long> CheckoutIdempotencyReuseTotal { get; }
    public Counter<long> CheckoutDuplicateOrderPreventedTotal { get; }
    public Counter<long> CheckoutProcessedTotal { get; }
    public Counter<long> PromotionEngineFailureTotal { get; }
    public Counter<long> TenantAccessViolationTotal { get; }
    public Counter<long> PromotionFailClosedTotal { get; }
    public Counter<long> PromotionFailOpenTotal { get; }
    public Counter<long> CouponUsageLimitReachedTotal { get; }
    public Counter<long> CouponReservationCreatedTotal { get; }
    public Counter<long> CouponReservationReleasedTotal { get; }
    public Counter<long> CouponUsageConfirmedTotal { get; }
    public Counter<long> CouponSplitBrainDetectedTotal { get; }
    public Counter<long> CouponConsumedTotal { get; }
    public Counter<long> CouponInvalidConfigurationTotal { get; }
    public Counter<long> CouponAbuseBlockedTotal { get; }
    public Counter<long> SmartOfferAbuseBlockedTotal { get; }
    public Counter<long> CheckoutTotalMismatchTotal { get; }
    public Counter<long> CouponEventPersistFailureTotal { get; }
    public Counter<long> CmsConflictDetectedTotal { get; }
    public Counter<long> AdminConflictDetectedTotal { get; }
    public Counter<long> CurrencyProjectionMismatchTotal { get; }
    public Counter<long> CurrencyMissingTotal { get; }
    public Counter<long> CurrencyInvalidTotal { get; }
    public Counter<long> CurrencyMismatchTotal { get; }
    public Counter<long> CurrencyMissingEventTotal { get; }
    public Counter<long> CurrencyInvalidEventTotal { get; }
    public Counter<long> ProjectionSyncDispatchedTotal { get; }
    public Counter<long> SearchFailureTotal { get; }
    public Counter<long> OrphanFilesDeletedTotal { get; }
    public Counter<long> ShippingWebhookValidationFailureTotal { get; }
    public Counter<long> ShippingWebhookDuplicateTotal { get; }
    public Counter<long> ShippingWebhookTenantResolvedTotal { get; }
    public Counter<long> ShippingWebhookTenantResolutionFailedTotal { get; }
    public Counter<long> WebhookPayloadRejectedTotal { get; }
    public Counter<long> WebhookProcessingTotal { get; }
    public Counter<long> WebhookRateLimitedTotal { get; }
    public Counter<long> WebhookInvalidSignatureTotal { get; }
    public Counter<long> WebhookSizeLimitExceededTotal { get; }
    public Counter<long> PaymentOutcomeTotal { get; }
    public Counter<long> ShippingSyncFailedTotal { get; }
    public Counter<long> StockSyncFailureTotal { get; }
    public Counter<long> StockSyncRetryTotal { get; }
    public Counter<long> StockDivergenceDetectedTotal { get; }
    public Counter<long> AbandonedCartGuestTrackedTotal { get; }
    public Counter<long> AbandonedCartSkippedTotal { get; }
    public Counter<long> RecoveryAttemptedTotal { get; }
    public Counter<long> RecoverySkippedNoContactTotal { get; }
    public Counter<long> RecoveryCartRestoredTotal { get; }
    public Counter<long> RecoveryCartRestoreFailedTotal { get; }
    public Counter<long> OtpRequestTotal { get; }
    public Counter<long> OtpDispatchSuccessTotal { get; }
    public Counter<long> OtpDispatchFailedTotal { get; }
    public Counter<long> OtpChannelUnavailableTotal { get; }
    public Counter<long> OtpVerificationFailedTotal { get; }
    public Counter<long> OtpRateLimitedTotal { get; }
    public Counter<long> OtpVerificationBlockedTotal { get; }
    public Counter<long> OtpAbuseDetectedTotal { get; }
    public Counter<long> RateLimitRecordFailureTotal { get; }
    public Counter<long> PaymentTransactionDeadlockTotal { get; }
    public Counter<long> PaymentTransactionRetryTotal { get; }
    public Counter<long> PaymentAmountMismatchTotal { get; }
    public Counter<long> BackgroundJobExecutionTotal { get; }
    public Counter<long> AccountingOutboxDeliveryTotal { get; }
    public Counter<long> ShipmentExternalCallAfterCommitTotal { get; }
    public Counter<long> ShipmentExternalDuplicatePreventedTotal { get; }
    public Counter<long> ShipmentReconciliationTotal { get; }
    public Counter<long> MigrationConcurrentIndexAppliedTotal { get; }
    public Counter<long> GuestPaymentRecoverySuccessTotal { get; }
    public Counter<long> SettlementBatchMismatchTotal { get; }
    public Counter<long> CodSettlementRejectedTotal { get; }
    public Counter<long> GuestRecoverySuccessTotal { get; }
    public Counter<long> TrackingTokenRemovedFromUrlTotal { get; }
    public Counter<long> TrackingTokenInvalidAccessTotal { get; }
    public Counter<long> GuestTrackingDeniedTotal { get; }
    public Counter<long> CheckoutStateLeakPreventedTotal { get; }
    public Counter<long> CartUnexpectedClearBlockedTotal { get; }
    public Histogram<double> StockSyncDeliveryLagSeconds { get; }
    public Histogram<double> PaymentTransactionDurationSeconds { get; }

    public CommerceReliabilityTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        PaymentIntentRejectedTotal = _meter.CreateCounter<long>(
            "payment_intent_rejected_total",
            unit: "count",
            description: "Payment intent creation attempts rejected before persistence.");

        PaymentConfigResetTotal = _meter.CreateCounter<long>(
            "payment_config_reset_total",
            unit: "count",
            description: "Explicit payment-configuration reset requests completed.");

        PaymentConfigClearedTotal = _meter.CreateCounter<long>(
            "payment_config_cleared_total",
            unit: "count",
            description: "Stored payment-provider configurations cleared after explicit confirmation.");

        PaymentConfigInvalidTotal = _meter.CreateCounter<long>(
            "payment_config_invalid_total",
            unit: "count",
            description: "Invalid or incomplete payment-provider configurations detected by write or enable validation.");

        PaymentMethodExposedInvalidTotal = _meter.CreateCounter<long>(
            "payment_method_exposed_invalid_total",
            unit: "count",
            description: "Enabled payment methods suppressed from storefront exposure because configuration was invalid.");

        RejectedDuplicateIntentTotal = _meter.CreateCounter<long>(
            "rejected_duplicate_intent_total",
            unit: "count",
            description: "Duplicate payment intent creation attempts rejected by order/provider idempotency.");

        PaymentAfterExpiryDetectedTotal = _meter.CreateCounter<long>(
            "payment_after_expiry_detected_total",
            unit: "count",
            description: "Provider success notifications received after the local inventory reservation was missing or expired.");

        PaymentIntentDuplicateAttemptTotal = _meter.CreateCounter<long>(
            "payment_intent_duplicate_attempt_total",
            unit: "count",
            description: "Concurrent or replayed payment intent creation attempts blocked by local idempotency claims.");

        WebhookLatePaymentTotal = _meter.CreateCounter<long>(
            "webhook_late_payment_total",
            unit: "count",
            description: "Webhook success events quarantined because payment could no longer be safely accepted.");

        GuestTrackingUnauthorizedAttemptTotal = _meter.CreateCounter<long>(
            "guest_tracking_unauthorized_attempt_total",
            unit: "count",
            description: "Guest order tracking attempts missing or failing signed-token verification.");

        CheckoutFinalizeFailureTotal = _meter.CreateCounter<long>(
            "checkout_finalize_failure_total",
            unit: "count",
            description: "Checkout finalization cleanup failures reported after backend payment success.");

        CheckoutSessionAuthorizationFailureTotal = _meter.CreateCounter<long>(
            "checkout_session_authorization_failure_total",
            unit: "count",
            description: "Checkout session authorization failures and ownership mismatch attempts rejected before token issuance.");

        CheckoutSessionDuplicateDetectedTotal = _meter.CreateCounter<long>(
            "checkout_session_duplicate_detected_total",
            unit: "count",
            description: "Duplicate recoverable checkout sessions detected for the same tenant/cart pair.");

        CheckoutSessionReconciliationTotal = _meter.CreateCounter<long>(
            "checkout_session_reconciliation_total",
            unit: "count",
            description: "Checkout session reconciliation actions that invalidated or merged duplicate sessions.");

        CheckoutSessionRacePreventedTotal = _meter.CreateCounter<long>(
            "checkout_session_race_prevented_total",
            unit: "count",
            description: "Checkout session race conditions prevented by locking or unique-session guards.");

        CheckoutEventPublishFailedTotal = _meter.CreateCounter<long>(
            "checkout_event_publish_failed_total",
            unit: "count",
            description: "Checkout-session event publication attempts that failed after business state handling started.");

        CheckoutFalseFailureTotal = _meter.CreateCounter<long>(
            "checkout_false_failure_total",
            unit: "count",
            description: "Checkout session requests that would have been falsely reported as failed after a durable save.");

        CheckoutPartialSuccessTotal = _meter.CreateCounter<long>(
            "checkout_partial_success_total",
            unit: "count",
            description: "Checkout session requests completed with durable persistence but degraded dispatch or reconciliation.");

        AuthStateDegradedTotal = _meter.CreateCounter<long>(
            "auth_state_degraded_total",
            unit: "count",
            description: "Storefront auth flows that entered a degraded state after a transient verification failure.");

        AuthDegradedRecoveryTotal = _meter.CreateCounter<long>(
            "auth_degraded_recovery_total",
            unit: "count",
            description: "Storefront authentication degraded-state recovery outcomes.");

        CheckoutSelectionInvalidatedTotal = _meter.CreateCounter<long>(
            "checkout_selection_invalidated_total",
            unit: "count",
            description: "Checkout shipping or payment selections preserved as stale because the latest configuration no longer matched.");

        CartStateMismatchDetectedTotal = _meter.CreateCounter<long>(
            "cart_state_mismatch_detected_total",
            unit: "count",
            description: "Cart responses rejected or flagged because item rows and aggregate state were inconsistent.");

        CartConcurrencyConflictTotal = _meter.CreateCounter<long>(
            "cart_concurrency_conflict_total",
            unit: "count",
            description: "Cart mutations rejected with conflict because another cart write or checkout owns the cart lock.");

        CartUpdateLostPreventedTotal = _meter.CreateCounter<long>(
            "cart_update_lost_prevented_total",
            unit: "count",
            description: "Cart mutations completed under the cart lock, preventing Redis read-modify-write lost updates.");

        CartMutationLockDurationSeconds = _meter.CreateHistogram<double>(
            "cart_mutation_lock_duration_seconds",
            unit: "s",
            description: "Elapsed time from first cart lock acquisition through token-owned release.");

        CheckoutIdempotencyReuseTotal = _meter.CreateCounter<long>(
            "checkout_idempotency_reuse_total",
            unit: "count",
            description: "Checkout requests replayed or recovered through the server-generated idempotency key.");

        CheckoutDuplicateOrderPreventedTotal = _meter.CreateCounter<long>(
            "checkout_duplicate_order_prevented_total",
            unit: "count",
            description: "Checkout attempts prevented from creating a duplicate active order for the same cart.");

        CheckoutProcessedTotal = _meter.CreateCounter<long>(
            "checkout_processed_total",
            unit: "count",
            description: "Checkout attempts completed with a terminal success or failure outcome.");

        PromotionEngineFailureTotal = _meter.CreateCounter<long>(
            "promotion_engine_failure_total",
            unit: "count",
            description: "Promotion calculation attempts that failed and forced checkout to stop.");

        TenantAccessViolationTotal = _meter.CreateCounter<long>(
            "tenant_access_violation_total",
            unit: "count",
            description: "Tenant isolation violations rejected before request processing could continue.");

        PromotionFailClosedTotal = _meter.CreateCounter<long>(
            "promotion_fail_closed_total",
            unit: "count",
            description: "Promotion calculation failures that were rejected with fail-closed behavior.");

        PromotionFailOpenTotal = _meter.CreateCounter<long>(
            "promotion_fail_open_total",
            unit: "count",
            description: "Promotion calculation failures where checkout continued without applying promotional discounts.");

        CouponUsageLimitReachedTotal = _meter.CreateCounter<long>(
            "coupon_usage_limit_reached_total",
            unit: "count",
            description: "Coupon validations or consumptions rejected because a configured usage limit was reached.");

        CouponReservationCreatedTotal = _meter.CreateCounter<long>(
            "coupon_reservation_created_total",
            unit: "count",
            description: "Coupon reservations created for pending orders before payment collection.");

        CouponReservationReleasedTotal = _meter.CreateCounter<long>(
            "coupon_reservation_released_total",
            unit: "count",
            description: "Coupon reservations released because the pending order failed, expired, or was cancelled.");

        CouponUsageConfirmedTotal = _meter.CreateCounter<long>(
            "coupon_usage_confirmed_total",
            unit: "count",
            description: "Coupon usages confirmed only after successful payment collection.");

        CouponSplitBrainDetectedTotal = _meter.CreateCounter<long>(
            "coupon_split_brain_detected_total",
            unit: "count",
            description: "Detected mismatches or missing inputs that would cause coupon behavior to diverge across flows.");

        CouponConsumedTotal = _meter.CreateCounter<long>(
            "coupon_consumed_total",
            unit: "count",
            description: "Coupons successfully consumed by checkout after durable validation and persistence.");

        CouponInvalidConfigurationTotal = _meter.CreateCounter<long>(
            "coupon_invalid_configuration_total",
            unit: "count",
            description: "Coupon writes or validations rejected because the coupon configuration was invalid.");

        CouponAbuseBlockedTotal = _meter.CreateCounter<long>(
            "coupon_abuse_blocked_total",
            unit: "count",
            description: "Coupon abuse attempts blocked by rate limiting or trusted-subject enforcement.");

        SmartOfferAbuseBlockedTotal = _meter.CreateCounter<long>(
            "smart_offer_abuse_blocked_total",
            unit: "count",
            description: "Smart offer abuse attempts blocked by server-side validation or rate limiting.");

        CheckoutTotalMismatchTotal = _meter.CreateCounter<long>(
            "checkout_total_mismatch_total",
            unit: "count",
            description: "Checkout submissions rejected because client totals did not match backend-authoritative pricing.");

        CouponEventPersistFailureTotal = _meter.CreateCounter<long>(
            "coupon_event_persist_failure_total",
            unit: "count",
            description: "Cart coupon mutations that failed before the coupon state and outbox event could be durably committed.");

        CmsConflictDetectedTotal = _meter.CreateCounter<long>(
            "cms_conflict_detected_total",
            unit: "count",
            description: "CMS writes rejected because a newer version already existed.");

        AdminConflictDetectedTotal = _meter.CreateCounter<long>(
            "admin_conflict_detected_total",
            unit: "count",
            description: "Admin mutations rejected because the provided concurrency token was stale or missing.");

        CurrencyProjectionMismatchTotal = _meter.CreateCounter<long>(
            "currency_projection_mismatch_total",
            unit: "count",
            description: "Detected currency drift between projected storefront/shipping data and tenant currency authority.");

        CurrencyMissingTotal = _meter.CreateCounter<long>(
            "currency_missing_total",
            unit: "count",
            description: "Operations rejected because a required currency value was missing or invalid.");

        CurrencyInvalidTotal = _meter.CreateCounter<long>(
            "currency_invalid_total",
            unit: "count",
            description: "Operations rejected because a currency value was invalid or malformed.");

        CurrencyMismatchTotal = _meter.CreateCounter<long>(
            "currency_mismatch_total",
            unit: "count",
            description: "Operations rejected because financial currencies did not match the tenant authority.");

        CurrencyMissingEventTotal = _meter.CreateCounter<long>(
            "currency_missing_event_total",
            unit: "count",
            description: "Event publication attempts aborted because a required currency was missing or invalid.");

        CurrencyInvalidEventTotal = _meter.CreateCounter<long>(
            "currency_invalid_event_total",
            unit: "count",
            description: "Event publication attempts aborted because a currency value was invalid or malformed.");

        ProjectionSyncDispatchedTotal = _meter.CreateCounter<long>(
            "projection_sync_dispatched_total",
            unit: "count",
            description: "Projection sync events durably staged for outbox dispatch.");

        SearchFailureTotal = _meter.CreateCounter<long>(
            "search_failure_total",
            unit: "count",
            description: "Storefront search requests that failed due to unavailable search infrastructure.");

        OrphanFilesDeletedTotal = _meter.CreateCounter<long>(
            "orphan_files_deleted_total",
            unit: "count",
            description: "Expired orphaned proof files deleted by cleanup jobs.");

        ShippingWebhookValidationFailureTotal = _meter.CreateCounter<long>(
            "shipping_webhook_validation_failure_total",
            unit: "count",
            description: "Shipping webhook requests rejected by signature, shared-secret, tenant resolution, or trust-boundary validation.");

        ShippingWebhookDuplicateTotal = _meter.CreateCounter<long>(
            "shipping_webhook_duplicate_total",
            unit: "count",
            description: "Shipping webhook requests ignored as duplicates after durable idempotency checks.");

        ShippingWebhookTenantResolvedTotal = _meter.CreateCounter<long>(
            "shipping_webhook_tenant_resolved_total",
            unit: "count",
            description: "Shipping webhook requests whose tenant was resolved explicitly before tenant-scoped processing.");

        ShippingWebhookTenantResolutionFailedTotal = _meter.CreateCounter<long>(
            "shipping_webhook_tenant_resolution_failed_total",
            unit: "count",
            description: "Shipping webhook requests rejected because tenant resolution failed or was ambiguous.");

        WebhookPayloadRejectedTotal = _meter.CreateCounter<long>(
            "webhook_payload_rejected_total",
            unit: "count",
            description: "Webhook requests rejected before provider processing due to malformed headers, content type, empty payloads, or validation failures.");

        WebhookProcessingTotal = _meter.CreateCounter<long>(
            "webhook_processing_total",
            unit: "count",
            description: "Webhook processing attempts grouped by provider, kind, and terminal outcome.");

        WebhookRateLimitedTotal = _meter.CreateCounter<long>(
            "webhook_rate_limited_total",
            unit: "count",
            description: "Webhook requests rejected by the webhook-specific rate limiter.");

        WebhookInvalidSignatureTotal = _meter.CreateCounter<long>(
            "webhook_invalid_signature_total",
            unit: "count",
            description: "Webhook requests rejected because the provider signature or HMAC was missing or invalid.");

        WebhookSizeLimitExceededTotal = _meter.CreateCounter<long>(
            "webhook_size_limit_exceeded_total",
            unit: "count",
            description: "Webhook requests rejected because the payload exceeded the provider-specific size limit.");

        PaymentOutcomeTotal = _meter.CreateCounter<long>(
            "payment_outcome_total",
            unit: "count",
            description: "Payment outcomes emitted by the payment lifecycle after durable state changes.");

        ShippingSyncFailedTotal = _meter.CreateCounter<long>(
            "shipping_sync_failed_total",
            unit: "count",
            description: "Shipping method sync attempts that failed before the outbox commit completed.");

        StockSyncFailureTotal = _meter.CreateCounter<long>(
            "stock_sync_failure_total",
            unit: "count",
            description: "Stock sync dispatch failures observed while delivering durable outbox messages.");

        StockSyncRetryTotal = _meter.CreateCounter<long>(
            "stock_sync_retry_total",
            unit: "count",
            description: "Stock sync outbox retry attempts scheduled after a failed publish.");

        StockDivergenceDetectedTotal = _meter.CreateCounter<long>(
            "stock_divergence_detected_total",
            unit: "count",
            description: "Detected SQL versus read-model stock divergence requiring SQL revalidation.");

        AbandonedCartGuestTrackedTotal = _meter.CreateCounter<long>(
            "abandoned_cart_guest_tracked_total",
            unit: "count",
            description: "Guest cart activity events durably tracked for abandoned-cart recovery.");

        AbandonedCartSkippedTotal = _meter.CreateCounter<long>(
            "abandoned_cart_skipped_total",
            unit: "count",
            description: "Cart-tracking opportunities skipped because the system could not safely capture the activity.");

        RecoveryAttemptedTotal = _meter.CreateCounter<long>(
            "recovery_attempted_total",
            unit: "count",
            description: "Abandoned-cart recovery attempts sent to the orchestration pipeline.");

        RecoverySkippedNoContactTotal = _meter.CreateCounter<long>(
            "recovery_skipped_no_contact_total",
            unit: "count",
            description: "Abandoned carts skipped for recovery because no verified contact channel was available.");

        RecoveryCartRestoredTotal = _meter.CreateCounter<long>(
            "recovery_cart_restored_total",
            unit: "count",
            description: "Recovery-link requests that restored a valid cart snapshot into the storefront flow.");

        RecoveryCartRestoreFailedTotal = _meter.CreateCounter<long>(
            "recovery_cart_restore_failed_total",
            unit: "count",
            description: "Recovery-link requests rejected because the cart snapshot or owner context was invalid or stale.");

        OtpRequestTotal = _meter.CreateCounter<long>(
            "otp_request_total",
            unit: "count",
            description: "Checkout OTP request attempts processed by the backend.");

        OtpDispatchSuccessTotal = _meter.CreateCounter<long>(
            "otp_dispatch_success_total",
            unit: "count",
            description: "OTP messages successfully dispatched to the delivery provider.");

        OtpDispatchFailedTotal = _meter.CreateCounter<long>(
            "otp_dispatch_failed_total",
            unit: "count",
            description: "OTP dispatch attempts that failed before reaching the customer.");

        OtpChannelUnavailableTotal = _meter.CreateCounter<long>(
            "otp_channel_unavailable_total",
            unit: "count",
            description: "OTP requests or dispatch attempts rejected because no active delivery channel was available.");

        OtpVerificationFailedTotal = _meter.CreateCounter<long>(
            "otp_verification_failed_total",
            unit: "count",
            description: "OTP verification attempts rejected because the code, state, or expiry was invalid.");

        OtpRateLimitedTotal = _meter.CreateCounter<long>(
            "otp_rate_limited_total",
            unit: "count",
            description: "OTP request attempts limited by server-side abuse protection.");

        OtpVerificationBlockedTotal = _meter.CreateCounter<long>(
            "otp_verification_blocked_total",
            unit: "count",
            description: "OTP verification attempts blocked because the OTP was exhausted, invalidated, or otherwise unavailable.");

        OtpAbuseDetectedTotal = _meter.CreateCounter<long>(
            "otp_abuse_detected_total",
            unit: "count",
            description: "Potential OTP abuse signals detected by rate limiting or verification-attempt exhaustion.");

        RateLimitRecordFailureTotal = _meter.CreateCounter<long>(
            "rate_limit_record_failure_total",
            unit: "count",
            description: "Rate limit recording failures after successful message dispatch, indicating potential drift in rate limit tracking.");

        PaymentTransactionDeadlockTotal = _meter.CreateCounter<long>(
            "payment_transaction_deadlock_total",
            unit: "count",
            description: "Payment lifecycle transaction deadlocks detected, indicating lock contention.");

        PaymentTransactionRetryTotal = _meter.CreateCounter<long>(
            "payment_transaction_retry_total",
            unit: "count",
            description: "Payment lifecycle transaction retry attempts after transient failures.");

        PaymentAmountMismatchTotal = _meter.CreateCounter<long>(
            "payment_amount_mismatch_total",
            unit: "count",
            description: "Provider-confirmed payment amounts or currencies rejected because they did not match stored intent authority.");

        BackgroundJobExecutionTotal = _meter.CreateCounter<long>(
            "background_job_execution_total",
            unit: "count",
            description: "Background job executions grouped by job name and terminal outcome.");

        AccountingOutboxDeliveryTotal = _meter.CreateCounter<long>(
            "accounting_outbox_delivery_total",
            unit: "count",
            description: "Accounting outbox message processing grouped by event type and outcome.");

        ShipmentExternalCallAfterCommitTotal = _meter.CreateCounter<long>(
            "shipment_external_call_after_commit_total",
            unit: "count",
            description: "Post-commit shipment registration attempts executed against external carriers.");

        ShipmentExternalDuplicatePreventedTotal = _meter.CreateCounter<long>(
            "shipment_external_duplicate_prevented_total",
            unit: "count",
            description: "Shipment external create calls suppressed because a durable carrier request was already in progress or completed.");

        ShipmentReconciliationTotal = _meter.CreateCounter<long>(
            "shipment_reconciliation_total",
            unit: "count",
            description: "Shipment reconciliation actions that repaired or avoided external registration divergence.");

        MigrationConcurrentIndexAppliedTotal = _meter.CreateCounter<long>(
            "migration_concurrent_index_applied_total",
            unit: "count",
            description: "Production-safe concurrent index migration operations configured or applied.");

        GuestPaymentRecoverySuccessTotal = _meter.CreateCounter<long>(
            "guest_payment_recovery_success_total",
            unit: "count",
            description: "Guest payment recovery flows that successfully restored payment state after refresh.");

        SettlementBatchMismatchTotal = _meter.CreateCounter<long>(
            "settlement_batch_mismatch_total",
            unit: "count",
            description: "Gateway settlement requests rejected because the resolved order batch did not exactly match the request.");

        CodSettlementRejectedTotal = _meter.CreateCounter<long>(
            "cod_settlement_rejected_total",
            unit: "count",
            description: "COD settlement requests rejected before any order state mutation.");

        GuestRecoverySuccessTotal = _meter.CreateCounter<long>(
            "guest_recovery_success_total",
            unit: "count",
            description: "Guest recovery and order tracking requests completed with a valid renewed token.");

        TrackingTokenRemovedFromUrlTotal = _meter.CreateCounter<long>(
            "tracking_token_removed_from_url_total",
            unit: "count",
            description: "Inbound tracking tokens removed from storefront URLs before analytics or third-party tooling observed them.");

        TrackingTokenInvalidAccessTotal = _meter.CreateCounter<long>(
            "tracking_token_invalid_access_total",
            unit: "count",
            description: "Guest tracking requests denied because a tracking token was missing, invalid, expired, or mismatched.");

        GuestTrackingDeniedTotal = _meter.CreateCounter<long>(
            "guest_tracking_denied_total",
            unit: "count",
            description: "Guest order tracking and recovery requests denied without returning order data.");

        CheckoutStateLeakPreventedTotal = _meter.CreateCounter<long>(
            "checkout_state_leak_prevented_total",
            unit: "count",
            description: "Checkout state clear operations that prevented cross-user state reuse.");

        CartUnexpectedClearBlockedTotal = _meter.CreateCounter<long>(
            "cart_unexpected_clear_blocked_total",
            unit: "count",
            description: "Unsafe cart-clearing attempts blocked because the active checkout context did not match the paid order.");

        StockSyncDeliveryLagSeconds = _meter.CreateHistogram<double>(
            "stock_sync_delivery_lag_seconds",
            unit: "s",
            description: "Observed lag between stock mutation occurrence and successful outbox dispatch.");

        PaymentTransactionDurationSeconds = _meter.CreateHistogram<double>(
            "payment_transaction_duration_seconds",
            unit: "s",
            description: "Duration of payment lifecycle transactions for performance monitoring.");
    }

    public void RecordPaymentIntentRejected(string reason, string provider) =>
        PaymentIntentRejectedTotal.Add(
            1,
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)),
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)));

    public void RecordPaymentConfigReset(string provider) =>
        PaymentConfigResetTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)));

    public void RecordPaymentConfigCleared(string provider) =>
        PaymentConfigClearedTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)));

    public void RecordPaymentConfigInvalid(string provider, string operation, string reason) =>
        PaymentConfigInvalidTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)),
            new KeyValuePair<string, object?>("operation", NormalizeReasonForTelemetry(operation)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordPaymentMethodExposedInvalid(string provider, int count)
    {
        if (count <= 0)
        {
            return;
        }

        PaymentMethodExposedInvalidTotal.Add(
            count,
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)));
    }

    public void RecordRejectedDuplicateIntent(string provider, string existingStatus) =>
        RejectedDuplicateIntentTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)),
            new KeyValuePair<string, object?>("existing_status", NormalizeReasonForTelemetry(existingStatus)));

    public void RecordPaymentAfterExpiryDetected(string provider, string reason) =>
        PaymentAfterExpiryDetectedTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordPaymentIntentDuplicateAttempt(string provider, string reason) =>
        PaymentIntentDuplicateAttemptTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordWebhookLatePayment(string provider, string reason) =>
        WebhookLatePaymentTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordGuestTrackingUnauthorizedAttempt(Guid tenantId, string reason) =>
        GuestTrackingUnauthorizedAttemptTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCheckoutFinalizeFailure(string reason) =>
        CheckoutFinalizeFailureTotal.Add(
            1,
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCheckoutSessionAuthorizationFailure(string endpoint, string reason) =>
        CheckoutSessionAuthorizationFailureTotal.Add(
            1,
            new KeyValuePair<string, object?>("endpoint", NormalizeReasonForTelemetry(endpoint)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCheckoutSessionDuplicateDetected(string surface, int duplicateCount)
    {
        if (duplicateCount <= 0)
        {
            return;
        }

        CheckoutSessionDuplicateDetectedTotal.Add(
            duplicateCount,
            new KeyValuePair<string, object?>("surface", NormalizeReasonForTelemetry(surface)));
    }

    public void RecordCheckoutSessionReconciliation(string surface, string action, int affectedSessions)
    {
        if (affectedSessions <= 0)
        {
            return;
        }

        CheckoutSessionReconciliationTotal.Add(
            affectedSessions,
            new KeyValuePair<string, object?>("surface", NormalizeReasonForTelemetry(surface)),
            new KeyValuePair<string, object?>("action", NormalizeReasonForTelemetry(action)));
    }

    public void RecordCheckoutSessionRacePrevented(string surface, string mechanism) =>
        CheckoutSessionRacePreventedTotal.Add(
            1,
            new KeyValuePair<string, object?>("surface", NormalizeReasonForTelemetry(surface)),
            new KeyValuePair<string, object?>("mechanism", NormalizeReasonForTelemetry(mechanism)));

    public void RecordCheckoutEventPublishFailed(string operation, string stage, string dispatchMode) =>
        CheckoutEventPublishFailedTotal.Add(
            1,
            new KeyValuePair<string, object?>("operation", NormalizeReasonForTelemetry(operation)),
            new KeyValuePair<string, object?>("stage", NormalizeReasonForTelemetry(stage)),
            new KeyValuePair<string, object?>("dispatch_mode", NormalizeReasonForTelemetry(dispatchMode)));

    public void RecordCheckoutFalseFailure(string endpoint, string reason) =>
        CheckoutFalseFailureTotal.Add(
            1,
            new KeyValuePair<string, object?>("endpoint", NormalizeReasonForTelemetry(endpoint)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCheckoutPartialSuccess(string endpoint, string recovery) =>
        CheckoutPartialSuccessTotal.Add(
            1,
            new KeyValuePair<string, object?>("endpoint", NormalizeReasonForTelemetry(endpoint)),
            new KeyValuePair<string, object?>("recovery", NormalizeReasonForTelemetry(recovery)));

    public void RecordAuthStateDegraded(string source, string reason) =>
        AuthStateDegradedTotal.Add(
            1,
            new KeyValuePair<string, object?>("source", NormalizeReasonForTelemetry(source)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordAuthDegradedRecovery(string source, string outcome) =>
        AuthDegradedRecoveryTotal.Add(
            1,
            new KeyValuePair<string, object?>("source", NormalizeReasonForTelemetry(source)),
            new KeyValuePair<string, object?>("outcome", NormalizeReasonForTelemetry(outcome)));

    public void RecordCheckoutSelectionInvalidated(string selectionType, string reason) =>
        CheckoutSelectionInvalidatedTotal.Add(
            1,
            new KeyValuePair<string, object?>("selection_type", NormalizeReasonForTelemetry(selectionType)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCartStateMismatchDetected(string surface, string reason) =>
        CartStateMismatchDetectedTotal.Add(
            1,
            new KeyValuePair<string, object?>("surface", NormalizeReasonForTelemetry(surface)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCartConcurrencyConflict(string operation) =>
        CartConcurrencyConflictTotal.Add(
            1,
            new KeyValuePair<string, object?>("operation", NormalizeReasonForTelemetry(operation)));

    public void RecordCartUpdateLostPrevented(string operation) =>
        CartUpdateLostPreventedTotal.Add(
            1,
            new KeyValuePair<string, object?>("operation", NormalizeReasonForTelemetry(operation)));

    public void RecordCartMutationLockDuration(string operation, TimeSpan duration) =>
        CartMutationLockDurationSeconds.Record(
            duration.TotalSeconds,
            new KeyValuePair<string, object?>("operation", NormalizeReasonForTelemetry(operation)));

    public void RecordCheckoutIdempotencyReuse(string source) =>
        CheckoutIdempotencyReuseTotal.Add(
            1,
            new KeyValuePair<string, object?>("source", NormalizeReasonForTelemetry(source)));

    public void RecordCheckoutDuplicateOrderPrevented(string source) =>
        CheckoutDuplicateOrderPreventedTotal.Add(
            1,
            new KeyValuePair<string, object?>("source", NormalizeReasonForTelemetry(source)));

    public void RecordCheckoutProcessed(Guid tenantId, string paymentMethod, string outcome) =>
        CheckoutProcessedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("payment_method", NormalizeReasonForTelemetry(paymentMethod)),
            new KeyValuePair<string, object?>("outcome", NormalizeReasonForTelemetry(outcome)));

    public void RecordPromotionEngineFailure(string surface, string reason) =>
        PromotionEngineFailureTotal.Add(
            1,
            new KeyValuePair<string, object?>("surface", NormalizeReasonForTelemetry(surface)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordTenantAccessViolation(string violationType, string path) =>
        TenantAccessViolationTotal.Add(
            1,
            new KeyValuePair<string, object?>("violation_type", NormalizeReasonForTelemetry(violationType)),
            new KeyValuePair<string, object?>("path", NormalizePathForTelemetry(path)));

    public void RecordPromotionFailClosed(Guid tenantId, string surface, string reason) =>
        PromotionFailClosedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("surface", NormalizeReasonForTelemetry(surface)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordPromotionFailOpen(Guid tenantId, string surface, string reason) =>
        PromotionFailOpenTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("surface", NormalizeReasonForTelemetry(surface)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCouponUsageLimitReached(Guid tenantId, string scope) =>
        CouponUsageLimitReachedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("scope", NormalizeReasonForTelemetry(scope)));

    public void RecordCouponReservationCreated(Guid tenantId, string scope) =>
        CouponReservationCreatedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("scope", NormalizeReasonForTelemetry(scope)));

    public void RecordCouponReservationReleased(Guid tenantId, string scope, string reason) =>
        CouponReservationReleasedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("scope", NormalizeReasonForTelemetry(scope)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCouponUsageConfirmed(Guid tenantId, string scope) =>
        CouponUsageConfirmedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("scope", NormalizeReasonForTelemetry(scope)));

    public void RecordCouponSplitBrainDetected(Guid tenantId, string surface, string reason) =>
        CouponSplitBrainDetectedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("surface", NormalizeReasonForTelemetry(surface)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCouponConsumed(Guid tenantId, string scope)
    {
        RecordCouponUsageConfirmed(tenantId, scope);
        CouponConsumedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("scope", NormalizeReasonForTelemetry(scope)));
    }

    public void RecordCouponInvalidConfiguration(Guid tenantId, string surface, string reason) =>
        CouponInvalidConfigurationTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("surface", NormalizeReasonForTelemetry(surface)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCouponAbuseBlocked(string vector, string reason) =>
        CouponAbuseBlockedTotal.Add(
            1,
            new KeyValuePair<string, object?>("vector", NormalizeReasonForTelemetry(vector)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordSmartOfferAbuseBlocked(string vector, string reason) =>
        SmartOfferAbuseBlockedTotal.Add(
            1,
            new KeyValuePair<string, object?>("vector", NormalizeReasonForTelemetry(vector)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCheckoutTotalMismatch(Guid tenantId, string field) =>
        CheckoutTotalMismatchTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("field", NormalizeReasonForTelemetry(field)));

    public void RecordCouponEventPersistFailure(Guid tenantId, string reason) =>
        CouponEventPersistFailureTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCmsConflictDetected(string entityType) =>
        CmsConflictDetectedTotal.Add(
            1,
            new KeyValuePair<string, object?>("entity_type", NormalizeReasonForTelemetry(entityType)));

    public void RecordAdminConflictDetected(string resource, string reason) =>
        AdminConflictDetectedTotal.Add(
            1,
            new KeyValuePair<string, object?>("resource", NormalizeReasonForTelemetry(resource)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCurrencyProjectionMismatch(string projection, string reason) =>
        CurrencyProjectionMismatchTotal.Add(
            1,
            new KeyValuePair<string, object?>("projection", projection),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCurrencyMissing(Guid? tenantId, Guid? orderId, string source) =>
        CurrencyMissingTotal.Add(
            1,
            CreateCurrencyTags(tenantId, orderId, source));

    public void RecordCurrencyInvalid(Guid? tenantId, Guid? orderId, string source) =>
        CurrencyInvalidTotal.Add(
            1,
            CreateCurrencyTags(tenantId, orderId, source));

    public void RecordCurrencyMismatch(Guid? tenantId, Guid? orderId, string source) =>
        CurrencyMismatchTotal.Add(
            1,
            CreateCurrencyTags(tenantId, orderId, source));

    public void RecordCurrencyMissingEvent(Guid? tenantId, Guid? orderId, string source) =>
        CurrencyMissingEventTotal.Add(
            1,
            CreateCurrencyTags(tenantId, orderId, source));

    public void RecordCurrencyInvalidEvent(Guid? tenantId, Guid? orderId, string source) =>
        CurrencyInvalidEventTotal.Add(
            1,
            CreateCurrencyTags(tenantId, orderId, source));

    public void RecordProjectionSyncDispatched(string projection, int count)
    {
        if (count <= 0)
        {
            return;
        }

        ProjectionSyncDispatchedTotal.Add(
            count,
            new KeyValuePair<string, object?>("projection", NormalizeReasonForTelemetry(projection)));
    }

    public void RecordSearchFailure(string surface, string reason) =>
        SearchFailureTotal.Add(
            1,
            new KeyValuePair<string, object?>("surface", NormalizeReasonForTelemetry(surface)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordOrphanFilesDeleted(int count, string storageProvider)
    {
        if (count <= 0)
        {
            return;
        }

        OrphanFilesDeletedTotal.Add(
            count,
            new KeyValuePair<string, object?>("storage_provider", NormalizeReasonForTelemetry(storageProvider)));
    }

    public void RecordShippingWebhookValidationFailure(string carrierCode, string reason, string validationMethod) =>
        ShippingWebhookValidationFailureTotal.Add(
            1,
            new KeyValuePair<string, object?>("carrier", NormalizeReasonForTelemetry(carrierCode)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)),
            new KeyValuePair<string, object?>("validation_method", NormalizeReasonForTelemetry(validationMethod)));

    public void RecordShippingWebhookDuplicate(string carrierCode, string reason) =>
        ShippingWebhookDuplicateTotal.Add(
            1,
            new KeyValuePair<string, object?>("carrier", NormalizeReasonForTelemetry(carrierCode)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordShippingWebhookTenantResolved(string carrierCode, string resolutionSource) =>
        ShippingWebhookTenantResolvedTotal.Add(
            1,
            new KeyValuePair<string, object?>("carrier", NormalizeReasonForTelemetry(carrierCode)),
            new KeyValuePair<string, object?>("resolution_source", NormalizeReasonForTelemetry(resolutionSource)));

    public void RecordShippingWebhookTenantResolutionFailed(string carrierCode, string reason, string resolutionSource) =>
        ShippingWebhookTenantResolutionFailedTotal.Add(
            1,
            new KeyValuePair<string, object?>("carrier", NormalizeReasonForTelemetry(carrierCode)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)),
            new KeyValuePair<string, object?>("resolution_source", NormalizeReasonForTelemetry(resolutionSource)));

    public void RecordWebhookPayloadRejected(string provider, string reason, string webhookKind) =>
        WebhookPayloadRejectedTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)),
            new KeyValuePair<string, object?>("kind", NormalizeReasonForTelemetry(webhookKind)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordWebhookProcessing(string provider, string webhookKind, string outcome) =>
        WebhookProcessingTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)),
            new KeyValuePair<string, object?>("kind", NormalizeReasonForTelemetry(webhookKind)),
            new KeyValuePair<string, object?>("outcome", NormalizeReasonForTelemetry(outcome)));

    public void RecordWebhookRateLimited(string provider, string tenantPartition, string webhookKind) =>
        WebhookRateLimitedTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)),
            new KeyValuePair<string, object?>("kind", NormalizeReasonForTelemetry(webhookKind)),
            new KeyValuePair<string, object?>("tenant_partition", NormalizeReasonForTelemetry(tenantPartition)));

    public void RecordWebhookInvalidSignature(string provider, string reason, string webhookKind) =>
        WebhookInvalidSignatureTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)),
            new KeyValuePair<string, object?>("kind", NormalizeReasonForTelemetry(webhookKind)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordWebhookSizeLimitExceeded(string provider, string webhookKind) =>
        WebhookSizeLimitExceededTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)),
            new KeyValuePair<string, object?>("kind", NormalizeReasonForTelemetry(webhookKind)));

    public void RecordShippingSyncFailed(Guid tenantId, string reason) =>
        ShippingSyncFailedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordPaymentOutcome(Guid tenantId, string provider, string paymentMethod, string outcome) =>
        PaymentOutcomeTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)),
            new KeyValuePair<string, object?>("payment_method", NormalizeReasonForTelemetry(paymentMethod)),
            new KeyValuePair<string, object?>("outcome", NormalizeReasonForTelemetry(outcome)));

    public void RecordStockSyncFailure(string stage, string eventType, bool terminal) =>
        StockSyncFailureTotal.Add(
            1,
            new KeyValuePair<string, object?>("stage", NormalizeReasonForTelemetry(stage)),
            new KeyValuePair<string, object?>("event_type", NormalizeReasonForTelemetry(eventType)),
            new KeyValuePair<string, object?>("terminal", terminal));

    public void RecordStockSyncRetry(string eventType, int attemptCount) =>
        StockSyncRetryTotal.Add(
            1,
            new KeyValuePair<string, object?>("event_type", NormalizeReasonForTelemetry(eventType)),
            new KeyValuePair<string, object?>("attempt", attemptCount));

    public void RecordStockDivergenceDetected(string surface, string reason) =>
        StockDivergenceDetectedTotal.Add(
            1,
            new KeyValuePair<string, object?>("surface", NormalizeReasonForTelemetry(surface)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordAbandonedCartGuestTracked(Guid tenantId, string? contactChannel) =>
        AbandonedCartGuestTrackedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("contact_channel", NormalizeReasonForTelemetry(contactChannel)));

    public void RecordAbandonedCartSkipped(Guid tenantId, string surface, string reason) =>
        AbandonedCartSkippedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("surface", NormalizeReasonForTelemetry(surface)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordRecoveryAttempted(Guid tenantId, string? contactChannel) =>
        RecoveryAttemptedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("contact_channel", NormalizeReasonForTelemetry(contactChannel)));

    public void RecordRecoverySkippedNoContact(Guid tenantId, string reason) =>
        RecoverySkippedNoContactTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordRecoveryCartRestored(Guid tenantId, string source) =>
        RecoveryCartRestoredTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("source", NormalizeReasonForTelemetry(source)));

    public void RecordRecoveryCartRestoreFailed(Guid? tenantId, string reason) =>
        RecoveryCartRestoreFailedTotal.Add(
            1,
            CreateOptionalTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    private static KeyValuePair<string, object?>[] CreateCurrencyTags(
        Guid? tenantId,
        Guid? orderId,
        string source)
    {
        return
        [
            CreateOptionalTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("tenant_present", tenantId.HasValue),
            new KeyValuePair<string, object?>("order_present", orderId.HasValue),
            new KeyValuePair<string, object?>("source", NormalizeReasonForTelemetry(source))
        ];
    }

    /// <summary>
    /// Creates a bounded tenant bucket tag for hot-path metrics to prevent unbounded cardinality.
    /// Maps tenant GUIDs to a fixed set of 10 buckets (bucket_0 through bucket_9).
    /// Use this instead of CreateTenantIdTag for high-frequency counters.
    /// Keep raw tenant GUIDs in logs/traces/span attributes only.
    /// </summary>
    private static KeyValuePair<string, object?> CreateTenantBucketTag(Guid tenantId)
    {
        // Use deterministic hash from GUID bytes (stable across .NET versions and process restarts)
        var bytes = tenantId.ToByteArray();
        var hash = BitConverter.ToInt32(bytes, 0);
        var bucket = Math.Abs(hash % 10);
        return new("tenant_bucket", $"bucket_{bucket}");
    }

    private static KeyValuePair<string, object?> CreateOptionalTenantBucketTag(Guid? tenantId) =>
        OperationalTelemetryDimensions.CreateOptionalTenantBucketTag(tenantId);

    public void RecordOtpRequest(Guid tenantId, string channel, string outcome, string? reason = null) =>
        OtpRequestTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("channel", NormalizeReasonForTelemetry(channel)),
            new KeyValuePair<string, object?>("outcome", NormalizeReasonForTelemetry(outcome)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordOtpDispatchSuccess(Guid tenantId, string channel) =>
        OtpDispatchSuccessTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("channel", NormalizeReasonForTelemetry(channel)));

    public void RecordOtpDispatchFailed(Guid tenantId, string channel, string reason) =>
        OtpDispatchFailedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("channel", NormalizeReasonForTelemetry(channel)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordOtpChannelUnavailable(Guid tenantId, string channel, string reason) =>
        OtpChannelUnavailableTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("channel", NormalizeReasonForTelemetry(channel)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordOtpVerificationFailed(Guid tenantId, string reason) =>
        OtpVerificationFailedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordOtpRateLimited(Guid tenantId, string scope, string reason) =>
        OtpRateLimitedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("scope", NormalizeReasonForTelemetry(scope)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordOtpVerificationBlocked(Guid tenantId, string reason) =>
        OtpVerificationBlockedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordOtpAbuseDetected(Guid tenantId, string vector, string reason) =>
        OtpAbuseDetectedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("vector", NormalizeReasonForTelemetry(vector)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordRateLimitRecordFailure(Guid tenantId, string channel, string reason) =>
        RateLimitRecordFailureTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("channel", NormalizeReasonForTelemetry(channel)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordPaymentTransactionDeadlock(string operation, string isolationLevel) =>
        PaymentTransactionDeadlockTotal.Add(
            1,
            new KeyValuePair<string, object?>("operation", NormalizeReasonForTelemetry(operation)),
            new KeyValuePair<string, object?>("isolation_level", NormalizeReasonForTelemetry(isolationLevel)));

    public void RecordPaymentTransactionRetry(string operation, int attemptNumber) =>
        PaymentTransactionRetryTotal.Add(
            1,
            new KeyValuePair<string, object?>("operation", NormalizeReasonForTelemetry(operation)),
            new KeyValuePair<string, object?>("attempt", attemptNumber));

    public void RecordPaymentAmountMismatch(Guid tenantId, string provider, string mismatchType) =>
        PaymentAmountMismatchTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("provider", NormalizeReasonForTelemetry(provider)),
            new KeyValuePair<string, object?>("mismatch_type", NormalizeReasonForTelemetry(mismatchType)));

    public void RecordBackgroundJobExecution(string jobName, string outcome) =>
        BackgroundJobExecutionTotal.Add(
            1,
            new KeyValuePair<string, object?>("job", NormalizeReasonForTelemetry(jobName)),
            new KeyValuePair<string, object?>("outcome", NormalizeReasonForTelemetry(outcome)));

    public void RecordAccountingOutboxDelivery(Guid tenantId, string eventType, string outcome) =>
        AccountingOutboxDeliveryTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("event_type", NormalizeReasonForTelemetry(eventType)),
            new KeyValuePair<string, object?>("outcome", NormalizeReasonForTelemetry(outcome)));

    public void RecordShipmentExternalCallAfterCommit(Guid tenantId, string carrierCode, string outcome) =>
        ShipmentExternalCallAfterCommitTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("carrier", NormalizeReasonForTelemetry(carrierCode)),
            new KeyValuePair<string, object?>("outcome", NormalizeReasonForTelemetry(outcome)));

    public void RecordShipmentExternalDuplicatePrevented(Guid tenantId, string carrierCode, string reason) =>
        ShipmentExternalDuplicatePreventedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("carrier", NormalizeReasonForTelemetry(carrierCode)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordShipmentReconciliation(Guid tenantId, string action, string reason) =>
        ShipmentReconciliationTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("action", NormalizeReasonForTelemetry(action)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordMigrationConcurrentIndexApplied(string migration, string indexName) =>
        MigrationConcurrentIndexAppliedTotal.Add(
            1,
            new KeyValuePair<string, object?>("migration", NormalizeReasonForTelemetry(migration)),
            new KeyValuePair<string, object?>("index", NormalizeReasonForTelemetry(indexName)));

    public void RecordGuestPaymentRecoverySuccess(string source) =>
        GuestPaymentRecoverySuccessTotal.Add(
            1,
            new KeyValuePair<string, object?>("source", NormalizeReasonForTelemetry(source)));

    public void RecordSettlementBatchMismatch(Guid tenantId, string settlementType, string reason) =>
        SettlementBatchMismatchTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("settlement_type", NormalizeReasonForTelemetry(settlementType)),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCodSettlementRejected(Guid tenantId, string reason) =>
        CodSettlementRejectedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordGuestRecoverySuccess(Guid tenantId, string source) =>
        GuestRecoverySuccessTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("source", NormalizeReasonForTelemetry(source)));

    public void RecordTrackingTokenRemovedFromUrl(string source) =>
        TrackingTokenRemovedFromUrlTotal.Add(
            1,
            new KeyValuePair<string, object?>("source", NormalizeReasonForTelemetry(source)));

    public void RecordTrackingTokenInvalidAccess(Guid tenantId, string reason) =>
        TrackingTokenInvalidAccessTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordGuestTrackingDenied(Guid tenantId, string reason) =>
        GuestTrackingDeniedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordCheckoutStateLeakPrevented(string trigger) =>
        CheckoutStateLeakPreventedTotal.Add(
            1,
            new KeyValuePair<string, object?>("trigger", NormalizeReasonForTelemetry(trigger)));

    public void RecordCartUnexpectedClearBlocked(string reason) =>
        CartUnexpectedClearBlockedTotal.Add(
            1,
            new KeyValuePair<string, object?>("reason", NormalizeReasonForTelemetry(reason)));

    public void RecordPaymentTransactionDuration(string operation, string outcome, double durationSeconds)
    {
        if (durationSeconds < 0d)
        {
            durationSeconds = 0d;
        }

        PaymentTransactionDurationSeconds.Record(
            durationSeconds,
            new KeyValuePair<string, object?>("operation", NormalizeReasonForTelemetry(operation)),
            new KeyValuePair<string, object?>("outcome", NormalizeReasonForTelemetry(outcome)));
    }

    public void RecordStockSyncDeliveryLag(string stage, double lagSeconds)
    {
        if (lagSeconds < 0d)
        {
            lagSeconds = 0d;
        }

        StockSyncDeliveryLagSeconds.Record(
            lagSeconds,
            new KeyValuePair<string, object?>("stage", NormalizeReasonForTelemetry(stage)));
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
