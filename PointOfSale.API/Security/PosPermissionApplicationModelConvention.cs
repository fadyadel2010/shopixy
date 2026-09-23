using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using PointOfSale.API.SuperAdmin.Models;

namespace PointOfSale.API.Security;

public sealed class PosPermissionApplicationModelConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            foreach (var action in controller.Actions)
            {
                if (IsAnonymous(controller, action) || HasSeparateAuthenticationAuthority(controller, action))
                {
                    continue;
                }

                var httpMethods = action.Attributes
                    .OfType<IActionHttpMethodProvider>()
                    .SelectMany(attribute => attribute.HttpMethods)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(method => method.ToUpperInvariant())
                    .ToArray();

                if (PosEndpointPermissionCatalog.TryResolve(
                        controller.ControllerName,
                        action.ActionName,
                        httpMethods,
                        out var permission))
                {
                    action.Filters.Add(new AuthorizeFilter(permission));
                }
            }
        }
    }

    private static bool IsAnonymous(ControllerModel controller, ActionModel action) =>
        controller.Attributes.OfType<IAllowAnonymous>().Any() ||
        action.Attributes.OfType<IAllowAnonymous>().Any();

    private static bool HasSeparateAuthenticationAuthority(ControllerModel controller, ActionModel action) =>
        controller.Attributes.Concat(action.Attributes)
            .OfType<IAuthorizeData>()
            .Any(attribute =>
                !string.IsNullOrWhiteSpace(attribute.AuthenticationSchemes) ||
                string.Equals(attribute.Policy, SuperAdminSecurityConstants.PolicyName, StringComparison.Ordinal) ||
                string.Equals(attribute.Policy, "AffiliateOnly", StringComparison.Ordinal));
}
