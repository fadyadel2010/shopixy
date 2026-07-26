namespace PointOfSale.API.Services.PhoneRisk;

public interface IOrderVelocityService
{
    Task<OrderVelocityAssessment> TrackOrderAsync(
        string? ipAddress,
        string? normalizedPhoneNumber,
        CancellationToken cancellationToken = default);

    Task<OrderVelocityAssessment> GetCurrentAssessmentAsync(
        string? ipAddress,
        string? normalizedPhoneNumber,
        CancellationToken cancellationToken = default);
}

public class OrderVelocityAssessment
{
    public int IpOrderCount { get; set; }
    public int PhoneOrderCount { get; set; }
    public bool IpVelocityTriggered { get; set; }
    public bool PhoneVelocityTriggered { get; set; }
}
