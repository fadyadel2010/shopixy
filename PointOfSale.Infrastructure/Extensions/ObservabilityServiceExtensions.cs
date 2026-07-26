using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PointOfSale.Infrastructure.HealthChecks;
using PointOfSale.Infrastructure.Identity;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Observability;

namespace PointOfSale.Infrastructure.Extensions;

/// <summary>
/// PRODUCTION HARDENING (Phase 12) — OBSERVABILITY SERVICE REGISTRATION
/// 
/// Extension methods to register production health checks and observability services.
/// Call AddProductionObservability() in Program.cs to enable all monitoring.
/// </summary>
public static class ObservabilityServiceExtensions
{
    /// <summary>
    /// Registers all production health checks and observability services.
    /// 
    /// Health checks registered:
    /// - ProjectionDriftHealthCheck: Detects PostgreSQL vs MongoDB drift
    /// - RedisLatencyHealthCheck: Monitors Redis response times
    /// - StockReservationHealthCheck: Detects stock integrity issues
    /// 
    /// Observability services:
    /// - IEventLagTracker: Tracks event processing latency
    /// </summary>
    public static IServiceCollection AddProductionObservability(this IServiceCollection services)
    {
        services.TryAddScoped<CanonicalIdentityReconciliationService>();

        // Register health checks
        services.AddHealthChecks()
            .AddCheck<ProjectionDriftHealthCheck>("projection_drift", tags: new[] { "database", "mongodb", "postgresql" })
            .AddCheck<ProjectionOutboxHealthCheck>("projection_outbox", tags: new[] { "ready", "projection", "critical" })
            .AddCheck<PaymentCancellationOutboxReadinessHealthCheck>("payment_cancellation_outbox_readiness", tags: new[] { "ready", "payments", "critical" })
            .AddCheck<ProviderDeliveryOutboxHealthCheck>("provider_delivery_outbox_readiness", tags: new[] { "ready", "analytics", "providers", "critical" })
            .AddCheck<MassTransitBusOutboxReadinessHealthCheck>("mass_transit_bus_outbox_readiness", tags: new[] { "ready", "events", "critical" })
            .AddCheck<AccountingOutboxReadinessHealthCheck>("accounting_outbox_readiness", tags: new[] { "ready", "accounting", "critical" })
            .AddCheck<CanonicalIdentityAuthorityHealthCheck>("canonical_identity_authority", tags: new[] { "ready", "identity", "critical" })
            .AddCheck<PayoutExternalTruthReadinessHealthCheck>("payout_external_truth_readiness", tags: new[] { "ready", "commercehub", "finance", "critical" })
            .AddCheck<CommerceHubExternalOutcomeRecoveryHealthCheck>("commercehub_external_outcome_recovery", tags: new[] { "ready", "commercehub", "shipping", "finance", "critical" })
            .AddCheck<RedisLatencyHealthCheck>("redis_latency", tags: new[] { "cache", "redis" })
            .AddCheck<StockReservationHealthCheck>("stock_reservation", tags: new[] { "inventory", "critical" })
            // P2-10: Marketplace health checks
            .AddCheck<MarketplaceElasticsearchHealthCheck>("marketplace_elasticsearch", tags: new[] { "search", "marketplace" })
            .AddCheck<ProviderTrustProjectionHealthCheck>("provider_trust_projections", tags: new[] { "mongodb", "marketplace" });
        
        // Register observability services
        services.AddSingleton<IEventLagTracker, EventLagTracker>();
        services.AddSingleton<InventoryReservationTelemetry>();
        services.AddScoped<IInventoryReservationMetricsService, InventoryReservationMetricsService>();
        
        return services;
    }
    
    /// <summary>
    /// Registers health checks only (without other observability services).
    /// Use this if you only need health checks.
    /// 
    /// NOTE: StockReservationHealthCheck requires additional dependencies:
    /// - IInventoryReservationMetricsService
    /// - InventoryReservationTelemetry
    /// - IOptions&lt;InventoryReservationMonitoringOptions&gt;
    /// 
    /// These are registered automatically by this method.
    /// </summary>
    public static IHealthChecksBuilder AddProductionHealthChecks(this IHealthChecksBuilder builder)
    {
        builder.Services.TryAddScoped<CanonicalIdentityReconciliationService>();

        // Register dependencies required by StockReservationHealthCheck
        builder.Services.TryAddSingleton<InventoryReservationTelemetry>();
        builder.Services.TryAddScoped<IInventoryReservationMetricsService, InventoryReservationMetricsService>();
        
        // Note: IOptions<InventoryReservationMonitoringOptions> must be configured separately
        // in Program.cs using:
        // services.AddOptions<InventoryReservationMonitoringOptions>()
        //     .BindConfiguration(InventoryReservationMonitoringOptions.SectionName)
        //     .ValidateOnStart();
        
        return builder
            .AddCheck<ProjectionDriftHealthCheck>("projection_drift", tags: new[] { "database", "mongodb", "postgresql" })
            .AddCheck<ProjectionOutboxHealthCheck>("projection_outbox", tags: new[] { "ready", "projection", "critical" })
            .AddCheck<PaymentCancellationOutboxReadinessHealthCheck>("payment_cancellation_outbox_readiness", tags: new[] { "ready", "payments", "critical" })
            .AddCheck<ProviderDeliveryOutboxHealthCheck>("provider_delivery_outbox_readiness", tags: new[] { "ready", "analytics", "providers", "critical" })
            .AddCheck<MassTransitBusOutboxReadinessHealthCheck>("mass_transit_bus_outbox_readiness", tags: new[] { "ready", "events", "critical" })
            .AddCheck<AccountingOutboxReadinessHealthCheck>("accounting_outbox_readiness", tags: new[] { "ready", "accounting", "critical" })
            .AddCheck<CanonicalIdentityAuthorityHealthCheck>("canonical_identity_authority", tags: new[] { "ready", "identity", "critical" })
            .AddCheck<PayoutExternalTruthReadinessHealthCheck>("payout_external_truth_readiness", tags: new[] { "ready", "commercehub", "finance", "critical" })
            .AddCheck<CommerceHubExternalOutcomeRecoveryHealthCheck>("commercehub_external_outcome_recovery", tags: new[] { "ready", "commercehub", "shipping", "finance", "critical" })
            .AddCheck<RedisLatencyHealthCheck>("redis_latency", tags: new[] { "cache", "redis" })
            .AddCheck<StockReservationHealthCheck>("stock_reservation", tags: new[] { "inventory", "critical" });
    }
}
