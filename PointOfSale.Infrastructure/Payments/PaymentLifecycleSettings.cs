using System.Data;

namespace PointOfSale.Infrastructure.Payments;

/// <summary>
/// Configuration settings for PaymentLifecycleService transaction behavior.
/// Bound from appsettings.json section "PaymentLifecycle".
/// </summary>
public class PaymentLifecycleSettings
{
    public const string SectionName = "PaymentLifecycle";

    /// <summary>
    /// Transaction isolation level for payment lifecycle operations.
    /// Default: Serializable (maximum consistency and concurrency safety).
    /// Use ReadCommitted for better performance if deadlocks and contention are acceptable trade-offs.
    /// </summary>
    public IsolationLevel TransactionIsolationLevel { get; set; } = IsolationLevel.Serializable;

    /// <summary>
    /// Enable detailed transaction telemetry (latency, deadlock tracking).
    /// Default: true for production observability.
    /// </summary>
    public bool EnableTransactionTelemetry { get; set; } = true;

    /// <summary>
    /// Require row-level locking support from the database provider.
    /// When true, throws InvalidOperationException if provider doesn't support row-level locking.
    /// When false, logs a warning and continues (useful for SQLite-backed tests).
    /// Default: true for production safety.
    /// </summary>
    public bool RequireRowLevelLocking { get; set; } = true;
}
