namespace PointOfSale.Core.Entities;

/// <summary>
/// Immutable spam report record used to preserve reporting integrity.
/// One tenant can report spam only once per order.
/// </summary>
public class PhoneSpamReport
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid? ReportedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
