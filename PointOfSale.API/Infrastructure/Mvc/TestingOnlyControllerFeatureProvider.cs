using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using PointOfSale.API.Attributes;

namespace PointOfSale.API.Infrastructure.Mvc;

/// <summary>
/// Removes all controllers marked with <see cref="TestingOnlyControllerAttribute"/>
/// when the application is NOT running in the "Test"/"Testing" environments.
///
/// This provider is registered in <c>Program.cs</c> unconditionally so that the
/// feature filter is always active. Controllers are removed for every environment
/// except "Test"/"Testing", ensuring they can never reach Production.
/// </summary>
public sealed class TestingOnlyControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    private readonly bool _isTestingEnvironment;

    public TestingOnlyControllerFeatureProvider(bool isTestingEnvironment)
    {
        _isTestingEnvironment = isTestingEnvironment;
    }

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        if (_isTestingEnvironment)
        {
            // Testing or Development environments: allow all TestingOnly controllers to remain.
            // The TestAuthController itself performs a secondary environment check so it will
            // return 404 in Development unless explicitly allowed by operator intent.
            return;
        }

        // Non-Testing/Non-Development environments: strip every controller decorated with [TestingOnlyController].
        for (var index = feature.Controllers.Count - 1; index >= 0; index--)
        {
            var controller = feature.Controllers[index];
            if (controller.IsDefined(typeof(TestingOnlyControllerAttribute), inherit: false))
            {
                feature.Controllers.Remove(controller);
            }
        }
    }
}
