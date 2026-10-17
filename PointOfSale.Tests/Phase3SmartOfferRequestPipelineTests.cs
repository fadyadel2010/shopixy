using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.Mvc;

namespace PointOfSale.Tests;

public sealed class Phase3SmartOfferRequestPipelineTests
{
    [Theory]
    [InlineData(null, "synthetic-protected-token", true)]
    [InlineData("", "synthetic-protected-token", true)]
    [InlineData(" SAVE10 ", null, true)]
    [InlineData(null, null, false)]
    [InlineData(" ", " ", false)]
    public async Task ActualRequestNormalizer_AdmitsCodeOrTokenButNeverRequiresBoth(string? code, string? token, bool accepted)
    {
        var method = typeof(Probe).GetMethod(nameof(Probe.Apply))!;
        var descriptor = new ControllerActionDescriptor { ControllerTypeInfo = typeof(Probe).GetTypeInfo(), MethodInfo = method,
            Parameters = [new ControllerParameterDescriptor { Name = "request", ParameterInfo = method.GetParameters()[0], ParameterType = typeof(ApplyCouponRequest) }] };
        var context = new ActionExecutingContext(new ActionContext(new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() }, new RouteData(), descriptor, new ModelStateDictionary()),
            [], new Dictionary<string, object?> { ["request"] = new ApplyCouponRequest { Code = code!, SmartOfferRedeemToken = token } }, new Probe());
        var invoked = false;
        await new ApiRequestNormalizationFilter().OnActionExecutionAsync(context, () => { invoked = true; return Task.FromResult(new ActionExecutedContext(context, [], context.Controller)); });
        Assert.Equal(accepted, invoked);
        if (accepted) Assert.Null(context.Result); else Assert.IsType<BadRequestObjectResult>(context.Result);
    }
    private sealed class Probe : ControllerBase { public IActionResult Apply(ApplyCouponRequest request) => NoContent(); }
}
