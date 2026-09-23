using Microsoft.AspNetCore.Authorization;

namespace PointOfSale.API.Security;

public sealed record PosPermissionRequirement(string Permission) : IAuthorizationRequirement;
