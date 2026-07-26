namespace PointOfSale.Core.Enums;

/// <summary>
/// User roles in the system.
/// Owner is the highest privilege level (tenant creator).
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Tenant owner - created during registration. Highest privilege.
    /// </summary>
    Owner,
    
    /// <summary>
    /// Full admin access
    /// </summary>
    Admin,
    
    /// <summary>
    /// Managerial access
    /// </summary>
    Manager,
    
    /// <summary>
    /// POS cashier access
    /// </summary>
    Cashier,
    
    /// <summary>
    /// Warehouse operations access
    /// </summary>
    Warehouse
}
