using System.Security.Claims;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Interfaces.Bulk;

namespace PointOfSale.API.Services.ProductAuthoring.Bulk;

public sealed class BulkAuthorizationService : IBulkAuthorizationService
{
    private static readonly IReadOnlyDictionary<BulkOperationType, string[]> RoleMatrix =
        new Dictionary<BulkOperationType, string[]>
        {
            [BulkOperationType.Activate] = new[] { "Owner", "Admin", "Manager" },
            [BulkOperationType.Deactivate] = new[] { "Owner", "Admin", "Manager" },
            [BulkOperationType.Publish] = new[] { "Owner", "Admin", "Manager" },
            [BulkOperationType.Unpublish] = new[] { "Owner", "Admin", "Manager" },
            [BulkOperationType.Delete] = new[] { "Owner", "Admin" }
        };

    public IReadOnlySet<string> RequiredRolesFor(BulkOperationType operation)
    {
        if (!RoleMatrix.TryGetValue(operation, out var roles))
        {
            throw new NotSupportedException($"Unsupported bulk operation: {operation}");
        }

        return new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAuthorized(BulkOperationType operation, ClaimsPrincipal user)
    {
        if (user is null)
        {
            return false;
        }

        var required = RequiredRolesFor(operation);
        return required.Any(user.IsInRole);
    }

    public bool IsAuthorized(BulkOperationType operation, IEnumerable<string> userRoles)
    {
        var required = RequiredRolesFor(operation);
        return userRoles?.Any(role => required.Contains(role)) ?? false;
    }
}
