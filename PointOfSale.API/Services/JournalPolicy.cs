namespace PointOfSale.API.Services;

/// <summary>
/// Defines the policy for handling existing journal entries during financial operations.
/// </summary>
public enum JournalPolicy
{
    /// <summary>
    /// Reject the operation if journal entries already exist.
    /// This is the safer default to prevent duplicate financial effects.
    /// </summary>
    RejectExisting = 0,

    /// <summary>
    /// Allow the operation to proceed even if journal entries already exist.
    /// Use with caution as this may indicate duplicate processing.
    /// </summary>
    AllowExisting = 1
}
