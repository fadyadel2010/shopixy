using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.Mvc;

namespace PointOfSale.Tests.Infrastructure;

public class ApiRequestNormalizationFilterTests
{
    [Fact]
    public async Task OnActionExecutionAsync_InvalidPayload_ReturnsStructuredValidationContract()
    {
        var filter = new ApiRequestNormalizationFilter();
        var request = new ValidationProbeDto
        {
            Name = "   ",
            Alias = "toolong",
            Description = "bad",
            Quantity = 0,
            Mode = (ValidationProbeMode)99,
            CustomerId = Guid.Empty,
            TotalAmount = -5m,
            Nested = new ValidationProbeNestedDto
            {
                Value = "   "
            }
        };

        var context = CreateActionExecutingContext(
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["request"] = request
            },
            traceIdentifier: "corr-validation-1");

        var nextInvoked = false;

        await filter.OnActionExecutionAsync(
            context,
            () =>
            {
                nextInvoked = true;
                return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
            });

        Assert.False(nextInvoked);

        var badRequest = Assert.IsType<BadRequestObjectResult>(context.Result);
        var response = Assert.IsType<ApiErrorResponse>(badRequest.Value);

        Assert.Equal("VALIDATION_ERROR", response.Code);
        Assert.Equal("The request payload is invalid.", response.Message);
        Assert.Equal("corr-validation-1", response.CorrelationId);

        var details = Assert.IsType<Dictionary<string, string[]>>(response.Details);
        Assert.True(details.ContainsKey("request"));
        Assert.True(details.ContainsKey("request.Name"));
        Assert.True(details.ContainsKey("request.Alias"));
        Assert.True(details.ContainsKey("request.Quantity"));
        Assert.True(details.ContainsKey("request.Mode"));
        Assert.True(details.ContainsKey("request.CustomerId"));
        Assert.True(details.ContainsKey("request.TotalAmount"));
        Assert.True(details.ContainsKey("request.Nested.Value"));

        Assert.Contains(details["request"], message => message.Contains("Description cannot be bad.", StringComparison.Ordinal));
        Assert.Contains(details["request.Mode"], message => message.Contains("supported option", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(details["request.TotalAmount"], message => message.Contains("negative", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OnActionExecutionAsync_MissingRequiredParameter_Returns400WithoutExecutingAction()
    {
        var filter = new ApiRequestNormalizationFilter();
        var context = CreateActionExecutingContext(
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            traceIdentifier: "corr-validation-2");

        var nextInvoked = false;

        await filter.OnActionExecutionAsync(
            context,
            () =>
            {
                nextInvoked = true;
                return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
            });

        Assert.False(nextInvoked);

        var badRequest = Assert.IsType<BadRequestObjectResult>(context.Result);
        var response = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        var details = Assert.IsType<Dictionary<string, string[]>>(response.Details);

        Assert.Equal("corr-validation-2", response.CorrelationId);
        Assert.True(details.ContainsKey("request"));
        Assert.Contains(details["request"], message => message.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OnActionExecutionAsync_OmittedOptionalParameterWithDefaultValue_DoesNotReturnValidationError()
    {
        var filter = new ApiRequestNormalizationFilter();
        var controller = new OptionalParameterProbeController();
        var methodInfo = typeof(OptionalParameterProbeController).GetMethod(nameof(OptionalParameterProbeController.Get))!;
        var parameterInfo = methodInfo.GetParameters().Single();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
            TraceIdentifier = "corr-validation-3"
        };

        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerName = nameof(OptionalParameterProbeController),
            ActionName = nameof(OptionalParameterProbeController.Get),
            ControllerTypeInfo = typeof(OptionalParameterProbeController).GetTypeInfo(),
            MethodInfo = methodInfo,
            Parameters =
            [
                new ControllerParameterDescriptor
                {
                    Name = parameterInfo.Name!,
                    ParameterInfo = parameterInfo,
                    ParameterType = parameterInfo.ParameterType
                }
            ]
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), actionDescriptor);
        var context = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller);
        var nextInvoked = false;

        await filter.OnActionExecutionAsync(
            context,
            () =>
            {
                nextInvoked = true;
                return Task.FromResult(new ActionExecutedContext(context, [], controller));
            });

        Assert.True(nextInvoked);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task OnActionExecutionAsync_ThemeActivationRequest_DoesNotRequireCommerceAiGraph()
    {
        var filter = new ApiRequestNormalizationFilter();
        var controller = new ThemeActivationProbeController();
        var context = CreateActionExecutingContext(
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["request"] = new AdminThemeActivationRequest
                {
                    ThemeId = "minimal"
                }
            },
            traceIdentifier: "corr-theme-activation",
            controller,
            nameof(ThemeActivationProbeController.Post));

        var nextInvoked = false;

        await filter.OnActionExecutionAsync(
            context,
            () =>
            {
                nextInvoked = true;
                return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
            });

        Assert.True(nextInvoked);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task OnActionExecutionAsync_FullStoreConfigRequest_StillRequiresCommerceAiAutoGreetingMessage()
    {
        var filter = new ApiRequestNormalizationFilter();
        var controller = new StoreConfigProbeController();
        var context = CreateActionExecutingContext(
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["request"] = new AdminStoreConfigRequest
                {
                    StoreName = "Validation Store",
                    ThemeId = "default",
                    Theme = new AdminThemeConfigDto(),
                    Behavior = new AdminBehaviorConfigDto(),
                    CommerceAi = new AdminCommerceAiConfigDto
                    {
                        Enabled = true,
                        WidgetStyle = "minimal",
                        WidgetPosition = "bottom-right",
                        AutoGreetingEnabled = true,
                        AutoGreetingMessage = string.Empty,
                        Personality = "friendly"
                    },
                    StorefrontStatus = "live"
                }
            },
            traceIdentifier: "corr-store-config-commerce-ai",
            controller,
            nameof(StoreConfigProbeController.Put));

        var nextInvoked = false;

        await filter.OnActionExecutionAsync(
            context,
            () =>
            {
                nextInvoked = true;
                return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
            });

        Assert.False(nextInvoked);

        var badRequest = Assert.IsType<BadRequestObjectResult>(context.Result);
        var response = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        var details = Assert.IsType<Dictionary<string, string[]>>(response.Details);

        Assert.Equal("VALIDATION_ERROR", response.Code);
        Assert.True(details.ContainsKey("request.CommerceAi.AutoGreetingMessage"));
        Assert.Contains(details["request.CommerceAi.AutoGreetingMessage"], message =>
            message.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    private static ActionExecutingContext CreateActionExecutingContext(
        IDictionary<string, object?> actionArguments,
        string traceIdentifier)
    {
        return CreateActionExecutingContext(
            actionArguments,
            traceIdentifier,
            new ValidationProbeController(),
            nameof(ValidationProbeController.Post));
    }

    private static ActionExecutingContext CreateActionExecutingContext<TController>(
        IDictionary<string, object?> actionArguments,
        string traceIdentifier,
        TController controller,
        string actionName)
        where TController : ControllerBase
    {
        var methodInfo = typeof(TController).GetMethod(actionName, BindingFlags.Instance | BindingFlags.Public)!;
        var parameterInfo = methodInfo.GetParameters().Single();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
            TraceIdentifier = traceIdentifier
        };

        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerName = typeof(TController).Name,
            ActionName = actionName,
            ControllerTypeInfo = typeof(TController).GetTypeInfo(),
            MethodInfo = methodInfo,
            Parameters =
            [
                new ControllerParameterDescriptor
                {
                    Name = parameterInfo.Name!,
                    ParameterInfo = parameterInfo,
                    ParameterType = parameterInfo.ParameterType
                }
            ]
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), actionDescriptor);
        return new ActionExecutingContext(actionContext, [], actionArguments, controller);
    }

    private sealed class ValidationProbeController : ControllerBase
    {
        public IActionResult Post(ValidationProbeDto request) => Ok(request);
    }

    private sealed class ThemeActivationProbeController : ControllerBase
    {
        public IActionResult Post(AdminThemeActivationRequest request) => Ok(request);
    }

    private sealed class StoreConfigProbeController : ControllerBase
    {
        public IActionResult Put(AdminStoreConfigRequest request) => Ok(request);
    }

    private sealed class OptionalParameterProbeController : ControllerBase
    {
        public IActionResult Get(string period = "month") => Ok(period);
    }

    private sealed class ValidationProbeDto
    {
        [Required(AllowEmptyStrings = false)]
        [StringLength(5)]
        public string Name { get; set; } = string.Empty;

        [StringLength(3)]
        public string Alias { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [Range(1, 10)]
        public int Quantity { get; set; }

        public ValidationProbeMode Mode { get; set; }

        public Guid CustomerId { get; set; }

        public decimal TotalAmount { get; set; }

        [Required]
        public ValidationProbeNestedDto Nested { get; set; } = new();

        public (string? ErrorCode, string? ErrorMessage) Validate()
        {
            return string.Equals(Description, "bad", StringComparison.OrdinalIgnoreCase)
                ? ("INVALID_DESCRIPTION", "Description cannot be bad.")
                : (null, null);
        }
    }

    private sealed class ValidationProbeNestedDto
    {
        [Required(AllowEmptyStrings = false)]
        public string Value { get; set; } = string.Empty;
    }

    private enum ValidationProbeMode
    {
        Standard = 1,
        Express = 2
    }
}
