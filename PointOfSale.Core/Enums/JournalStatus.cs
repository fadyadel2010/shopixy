namespace PointOfSale.Core.Enums;

/// <summary>
/// Status of a journal entry. Immutable once posted except for reversal marking.
/// </summary>
public enum JournalStatus
{
    /// <summary>
    /// Journal is active and affects balances.
    /// </summary>
    Posted = 1,
    
    /// <summary>
    /// Journal has been reversed by another journal.
    /// Effects are negated but journal remains for audit trail.
    /// </summary>
    Reversed = 2
}
