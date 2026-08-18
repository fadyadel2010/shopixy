namespace PointOfSale.API.Security;

/// <summary>
/// Marks binary upload endpoints where a server-issued upload lease is the
/// request authority, matching external presigned object-storage semantics.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LeaseTokenAuthorizedUploadAttribute : Attribute;
