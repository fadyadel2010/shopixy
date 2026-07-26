namespace PointOfSale.API.Configuration;

public sealed class CommerceHubEmergencyStopOptions
{
    public const string SectionName = "CommerceHub:EmergencyStops";

    public bool CommerceIntakeStopped { get; set; }

    public bool PaymentIntakeStopped { get; set; }

    public bool OutwardMoneyMovementStopped { get; set; }

    public bool PayoutsStopped { get; set; }

    public bool RefundsStopped { get; set; }

    public string? Reason { get; set; }
}
