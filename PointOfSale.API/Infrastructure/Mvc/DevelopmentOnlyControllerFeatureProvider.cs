using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using PointOfSale.API.Attributes;

namespace PointOfSale.API.Infrastructure.Mvc;

public sealed class DevelopmentOnlyControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        for (var index = feature.Controllers.Count - 1; index >= 0; index--)
        {
            var controller = feature.Controllers[index];
            if (controller.IsDefined(typeof(DevelopmentOnlyControllerAttribute), inherit: false))
            {
                feature.Controllers.Remove(controller);
            }
        }
    }
}
