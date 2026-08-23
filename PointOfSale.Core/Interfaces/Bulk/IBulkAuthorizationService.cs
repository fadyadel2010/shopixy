using System.Security.Claims;
using PointOfSale.Core.DTOs.Bulk;

namespace PointOfSale.Core.Interfaces.Bulk;

public interface IBulkAuthorizationService
{
    IReadOnlySet<string> RequiredRolesFor(BulkOperationType operation);

    bool IsAuthorized(BulkOperationType operation, ClaimsPrincipal user);

    bool IsAuthorized(BulkOperationType operation, IEnumerable<string> userRoles);
}
