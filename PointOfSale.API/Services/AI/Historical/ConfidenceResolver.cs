using Microsoft.Extensions.Logging;
using PointOfSale.API.Services.AI.Models;

namespace PointOfSale.API.Services.AI.Historical;

/// <summary>
/// Confidence resolver implementation.
/// PHASE 5: Historical Consistency & Confidence Layer.
/// 
/// Determines data confidence based on:
/// - Order creation date vs feature enablement dates
/// - Event completeness (actual vs expected)
/// - Integration availability at time of order
/// </summary>
public class ConfidenceResolver : IConfidenceResolver
{
    private readonly ILogger<ConfidenceResolver> _logger;

    // Minimum event count thresholds
    private const int MinEventsForHighConfidence = 3;
    private const double EventRatioForMediumConfidence = 0.5;

    public ConfidenceResolver(ILogger<ConfidenceResolver> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ConfidenceResult ResolveConfidence(
        DateTime orderCreatedAt,
        FeatureTimeline featureTimeline,
        int actualEventCount,
        int expectedEventCount)
    {
        var reasons = new List<string>();

        // Check if order was created before integrations were enabled
        bool isLegacyShipping = featureTimeline.ShippingTrackingEnabledAt.HasValue &&
                                orderCreatedAt < featureTimeline.ShippingTrackingEnabledAt.Value;
        
        bool isLegacyPayment = featureTimeline.PaymentTrackingEnabledAt.HasValue &&
                               orderCreatedAt < featureTimeline.PaymentTrackingEnabledAt.Value;
        
        bool isLegacyEvents = featureTimeline.EventLoggingEnabledAt.HasValue &&
                              orderCreatedAt < featureTimeline.EventLoggingEnabledAt.Value;

        // If order is from before event logging, return LOW confidence
        if (isLegacyEvents)
        {
            reasons.Add("Order was created before event logging was enabled");
            return ConfidenceResult.Low(
                reasons,
                "This order was created before automated event tracking was enabled. The data shown reflects what was recorded at that time, which was the standard process then."
            );
        }

        // If order is from before shipping integration, note it
        if (isLegacyShipping)
        {
            reasons.Add("Order was created before shipping tracking integration");
        }

        // Check event completeness
        double eventRatio = expectedEventCount > 0 
            ? (double)actualEventCount / expectedEventCount 
            : 1.0;

        // Determine confidence based on event completeness
        if (actualEventCount >= MinEventsForHighConfidence && eventRatio >= 0.8)
        {
            // High confidence - full event trail
            return ConfidenceResult.High();
        }
        else if (eventRatio >= EventRatioForMediumConfidence)
        {
            // Medium confidence - partial events
            if (isLegacyShipping || isLegacyPayment)
            {
                return ConfidenceResult.Medium(
                    "Partial event history with legacy context",
                    "Some tracking details may not be available because this order was created during a period when automated tracking was being introduced."
                );
            }
            else
            {
                return ConfidenceResult.Medium(
                    "Partial event history",
                    "Some tracking events may still be pending or not yet recorded."
                );
            }
        }
        else
        {
            // Low confidence - minimal events
            if (isLegacyShipping)
            {
                reasons.Add("Limited shipping tracking data");
                return ConfidenceResult.Low(
                    reasons,
                    "This order was processed before automated shipment tracking was fully enabled. The delivery was handled successfully through our standard process at that time."
                );
            }
            else
            {
                reasons.Add("Minimal event data recorded");
                return ConfidenceResult.Low(
                    reasons,
                    "Limited tracking information is available for this order. This may be due to the order type or processing method used."
                );
            }
        }
    }

    /// <inheritdoc />
    public HistoricalContext BuildHistoricalContext(
        DateTime orderCreatedAt,
        FeatureTimeline featureTimeline,
        int actualEventCount,
        int expectedEventCount)
    {
        var confidenceResult = ResolveConfidence(
            orderCreatedAt, 
            featureTimeline, 
            actualEventCount, 
            expectedEventCount);

        bool isLegacyShipping = featureTimeline.ShippingTrackingEnabledAt.HasValue &&
                                orderCreatedAt < featureTimeline.ShippingTrackingEnabledAt.Value;
        
        bool isLegacyPayment = featureTimeline.PaymentTrackingEnabledAt.HasValue &&
                               orderCreatedAt < featureTimeline.PaymentTrackingEnabledAt.Value;
        
        bool isLegacyEvents = featureTimeline.EventLoggingEnabledAt.HasValue &&
                              orderCreatedAt < featureTimeline.EventLoggingEnabledAt.Value;

        return new HistoricalContext
        {
            IsLegacyOrder = isLegacyShipping || isLegacyPayment || isLegacyEvents,
            ShippingIntegrationEnabledAtCreation = !isLegacyShipping,
            PaymentIntegrationEnabledAtCreation = !isLegacyPayment,
            EventTrackingEnabledAtCreation = !isLegacyEvents,
            EventsIncomplete = actualEventCount < expectedEventCount,
            ExpectedEventCount = expectedEventCount,
            ActualEventCount = actualEventCount,
            Confidence = confidenceResult.Confidence,
            ConfidenceNote = confidenceResult.Note,
            OrderCreatedAt = orderCreatedAt,
            ShippingIntegrationEnabledDate = featureTimeline.ShippingTrackingEnabledAt,
            PaymentIntegrationEnabledDate = featureTimeline.PaymentTrackingEnabledAt
        };
    }
}

/// <summary>
/// Feature timeline provider interface.
/// Retrieves when features were enabled for a tenant.
/// </summary>
public interface IFeatureTimelineProvider
{
    /// <summary>
    /// Get feature timeline for a tenant.
    /// </summary>
    Task<FeatureTimeline> GetFeatureTimelineAsync(Guid tenantId);
}

/// <summary>
/// Default feature timeline provider.
/// Returns default timeline (all features enabled from the beginning).
/// In production, this would read from tenant configuration.
/// </summary>
public class DefaultFeatureTimelineProvider : IFeatureTimelineProvider
{
    public Task<FeatureTimeline> GetFeatureTimelineAsync(Guid tenantId)
    {
        // Default: all features enabled from the beginning
        // In production, this would be tenant-specific
        return Task.FromResult(FeatureTimeline.Default);
    }
}
