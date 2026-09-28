namespace PointOfSale.API.DTOs.Admin;

public class AdminBlockPhoneRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public class AdminUnblockPhoneRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class AdminReportSpamPhoneRequest
{
    // Retained for request compatibility; the backend always resolves the phone from OrderId.
    public string? PhoneNumber { get; set; }
    public Guid OrderId { get; set; }
}

public class AdminBlockOrderPhoneRequest
{
    public string? Reason { get; set; }
}

public class AdminBlockedPhoneQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }

    public string? Validate()
    {
        if (Page < 1)
        {
            return "Page must be at least 1";
        }

        if (PageSize < 1 || PageSize > 100)
        {
            return "PageSize must be between 1 and 100";
        }

        return null;
    }
}

public class AdminPhoneRiskStatusDto
{
    public string PhoneNumber { get; set; } = string.Empty;
    public int SpamReports { get; set; }
    public int GlobalSpamReports { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockedReason { get; set; }
    public Guid? BlockedByUserId { get; set; }
    public string? BlockedByUser { get; set; }
    public DateTime? BlockedAtUtc { get; set; }
}

public class AdminBlockedPhoneListItemDto : AdminPhoneRiskStatusDto
{
    public Guid Id { get; set; }
}

public class AdminOrderCustomerTrustDto
{
    public Guid OrderId { get; set; }
    public string? PhoneNumber { get; set; }
    public bool HasPhone => !string.IsNullOrWhiteSpace(PhoneNumber);
    public int PreviousOrdersCount { get; set; }
    public int CancelledOrdersCount { get; set; }
    public bool HasReportedSpam { get; set; }
    public string RiskLevel { get; set; } = "UNAVAILABLE";
    public List<string> RiskSignals { get; set; } = new();
    public AdminPhoneRiskStatusDto? PhoneRisk { get; set; }
}
