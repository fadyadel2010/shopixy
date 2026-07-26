namespace PointOfSale.Core.Interfaces.Accounting;

/// <summary>
/// Seeds the baseline Chart of Accounts for a tenant.
/// 
/// BEHAVIOR:
/// - Idempotent: Can be called multiple times safely
/// - Creates all system accounts if they don't exist
/// - Creates default payment methods
/// - Creates default treasury accounts
/// </summary>
public interface ITenantAccountSeeder
{
    /// <summary>
    /// Seeds all baseline accounts, payment methods, and treasury accounts.
    /// Idempotent - safe to call multiple times.
    /// </summary>
    Task SeedAsync(Guid tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Seeds only the chart of accounts (without payment methods/treasury).
    /// </summary>
    Task SeedAccountsAsync(Guid tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Seeds payment methods for a tenant.
    /// Requires accounts to be seeded first.
    /// </summary>
    Task SeedPaymentMethodsAsync(Guid tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Seeds treasury accounts for a tenant.
    /// Requires accounts to be seeded first.
    /// </summary>
    Task SeedTreasuryAccountsAsync(Guid tenantId, CancellationToken ct = default);
}
