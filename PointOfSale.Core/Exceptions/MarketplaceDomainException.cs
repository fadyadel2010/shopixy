namespace PointOfSale.Core.Exceptions;

public class MarketplaceDomainException : Exception
{
    public string ErrorCode { get; }

    public MarketplaceDomainException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public static MarketplaceDomainException ProfileAlreadyExists()
        => new("PROFILE_EXISTS", "User already has a marketplace profile");

    public static MarketplaceDomainException ProfileNotFound()
        => new("PROFILE_NOT_FOUND", "Marketplace profile not found");

    public static MarketplaceDomainException ServiceNotFound()
        => new("SERVICE_NOT_FOUND", "Service not found");

    public static MarketplaceDomainException Unauthorized(string action)
        => new("UNAUTHORIZED", $"User is not authorized to {action}");

    public static MarketplaceDomainException InvalidServiceRequestStatus(string action, string currentStatus)
        => new("INVALID_STATUS", $"Cannot {action} in status {currentStatus}");

    public static MarketplaceDomainException ReviewAlreadyExists()
        => new("REVIEW_EXISTS", "A review already exists for this service request");

    public static MarketplaceDomainException ReviewNotAllowed()
        => new("REVIEW_NOT_ALLOWED", "Cannot create review: ServiceRequest must be CompletedByBuyer");

    public static MarketplaceDomainException ServiceRequestNotFound()
        => new("REQUEST_NOT_FOUND", "Service request not found");

    public static MarketplaceDomainException ActiveServiceRequestExists()
        => new("ACTIVE_REQUEST_EXISTS", "An active request already exists for this service");

    public static MarketplaceDomainException SelfServiceRequestForbidden()
        => new("SELF_REQUEST_FORBIDDEN", "Providers cannot request their own services");

    public static MarketplaceDomainException InvalidRequestMessage()
        => new("INVALID_REQUEST_MESSAGE", "Request message must not exceed 2000 characters");

    public static MarketplaceDomainException MarketingEvidenceRequired()
        => new("EVIDENCE_REQUIRED", "Marketing service reviews must include campaign evidence");

    public static MarketplaceDomainException MarketingEvidenceForbidden()
        => new("EVIDENCE_FORBIDDEN", "Non-marketing service reviews must not include campaign evidence");
}
