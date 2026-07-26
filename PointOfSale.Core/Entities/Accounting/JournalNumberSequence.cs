using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Accounting;

/// <summary>
/// Tracks per-tenant journal number sequences.
/// Used to generate sequential journal numbers: JE-YYYY-000001
/// </summary>
public class JournalNumberSequence : ITenantOwned
{
    /// <summary>
    /// Tenant ID - Primary Key.
    /// One sequence per tenant per year.
    /// </summary>
    [Key]
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Current year for this sequence.
    /// Resets NextNumber when year changes.
    /// </summary>
    public int CurrentYear { get; set; }
    
    /// <summary>
    /// Next number to allocate.
    /// </summary>
    public int NextNumber { get; set; } = 1;
    
    /// <summary>
    /// Allocates the next journal number and increments the counter.
    /// Should be called within a transaction with row lock.
    /// </summary>
    public string AllocateNext(int year)
    {
        // Reset counter if year changed
        if (year != CurrentYear)
        {
            CurrentYear = year;
            NextNumber = 1;
        }
        
        var number = NextNumber++;
        return $"JE-{year}-{number:D6}";
    }
}
