using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.Mvc;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ApiRequestNormalizationFilterConcurrencyTests
{
    private const int ParallelWorkers = 64;
    private const int IterationsPerWorker = 50;

    [Fact]
    public async Task ConcurrentValidRequests_InvokeNextDeterministically()
    {
        var errors = new ConcurrentBag<Exception>();
        var passed = 0;

        await RunConcurrentAsync(async iteration =>
        {
            var (context, nextInvoked) = await ExecutePostAsync(CreateValidDto());
            if (nextInvoked && context.Result == null)
            {
                Interlocked.Increment(ref passed);
            }
        }, errors);

        Assert.Empty(errors);
        Assert.Equal(ParallelWorkers * IterationsPerWorker, passed);
    }

    [Fact]
    public async Task ConcurrentInvalidRequests_ReturnIdenticalRequiredErrors()
    {
        var errors = new ConcurrentBag<Exception>();
        var matched = 0;

        await RunConcurrentAsync(async iteration =>
        {
            var (context, nextInvoked) = await ExecutePostAsync(CreateInvalidDto());
            var badRequest = context.Result as BadRequestObjectResult;
            var response = badRequest?.Value as ApiErrorResponse;
            var details = response?.Details as Dictionary<string, string[]>;
            if (!nextInvoked &&
                details?.TryGetValue("request.RequiredByNullability", out var messages) == true &&
                messages.Any(message => message.Contains("required", StringComparison.OrdinalIgnoreCase)))
            {
                Interlocked.Increment(ref matched);
            }
        }, errors);

        Assert.Empty(errors);
        Assert.Equal(ParallelWorkers * IterationsPerWorker, matched);
    }

    [Fact]
    public async Task ConcurrentMixedNullabilityParameters_AreDeterministic()
    {
        var errors = new ConcurrentBag<Exception>();
        var matched = 0;

        await RunConcurrentAsync(async iteration =>
        {
            var valid = iteration % 2 == 0;
            var (context, nextInvoked) = await ExecuteSearchAsync(valid ? "shoes" : "   ", null);
            if ((valid && nextInvoked && context.Result == null) ||
                (!valid && !nextInvoked && context.Result is BadRequestObjectResult))
            {
                Interlocked.Increment(ref matched);
            }
        }, errors);

        Assert.Empty(errors);
        Assert.Equal(ParallelWorkers * IterationsPerWorker, matched);
    }

    [Fact]
    public async Task ConcurrentNullOptionalProperties_DoNotFailIntermittently()
    {
        var errors = new ConcurrentBag<Exception>();
        var passed = 0;

        await RunConcurrentAsync(async iteration =>
        {
            var dto = CreateValidDto();
            dto.OptionalByNullability = null;
            dto.Nested.NestedOptional = null;
            var (context, nextInvoked) = await ExecutePostAsync(dto);
            if (nextInvoked && context.Result == null)
            {
                Interlocked.Increment(ref passed);
            }
        }, errors);

        Assert.Empty(errors);
        Assert.Equal(ParallelWorkers * IterationsPerWorker, passed);
    }

    private static async Task RunConcurrentAsync(
        Func<int, Task> action,
        ConcurrentBag<Exception> errors)
    {
        var tasks = Enumerable.Range(0, ParallelWorkers).Select(worker => Task.Run(async () =>
        {
            try
            {
                for (var iteration = 0; iteration < IterationsPerWorker; iteration++)
                {
                    await action(iteration);
                }
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }));

        await Task.WhenAll(tasks);
    }

    private static NullabilityProbeDto CreateValidDto() => new()
    {
        RequiredByNullability = "ok",
        OptionalByNullability = "optional",
        Nested = new NullabilityProbeNestedDto
        {
            NestedRequired = "ok",
            NestedOptional = "optional"
        }
    };

    private static NullabilityProbeDto CreateInvalidDto() => new()
    {
        RequiredByNullability = "   ",
        OptionalByNullability = "optional",
        Nested = new NullabilityProbeNestedDto
        {
            NestedRequired = "ok"
        }
    };

    private static async Task<(ActionExecutingContext Context, bool NextInvoked)> ExecutePostAsync(
        NullabilityProbeDto dto)
    {
        var method = typeof(NullabilityProbeController).GetMethod(nameof(NullabilityProbeController.Post))!;
        return await ExecuteAsync(method, new Dictionary<string, object?> { ["request"] = dto });
    }

    private static async Task<(ActionExecutingContext Context, bool NextInvoked)> ExecuteSearchAsync(
        string query,
        string? hint)
    {
        var method = typeof(NullabilityProbeController).GetMethod(nameof(NullabilityProbeController.Search))!;
        return await ExecuteAsync(
            method,
            new Dictionary<string, object?> { ["query"] = query, ["hint"] = hint });
    }

    private static async Task<(ActionExecutingContext Context, bool NextInvoked)> ExecuteAsync(
        MethodInfo method,
        Dictionary<string, object?> arguments)
    {
        var controller = new NullabilityProbeController();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
            TraceIdentifier = Guid.NewGuid().ToString("N")
        };
        var descriptor = new ControllerActionDescriptor
        {
            ControllerName = nameof(NullabilityProbeController),
            ActionName = method.Name,
            ControllerTypeInfo = typeof(NullabilityProbeController).GetTypeInfo(),
            MethodInfo = method,
            Parameters = method.GetParameters().Select(parameter => (ParameterDescriptor)new ControllerParameterDescriptor
            {
                Name = parameter.Name!,
                ParameterInfo = parameter,
                ParameterType = parameter.ParameterType
            }).ToList()
        };
        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
        var context = new ActionExecutingContext(actionContext, [], arguments, controller);
        var nextInvoked = false;

        await new ApiRequestNormalizationFilter().OnActionExecutionAsync(
            context,
            () =>
            {
                nextInvoked = true;
                return Task.FromResult(new ActionExecutedContext(context, [], controller));
            });

        return (context, nextInvoked);
    }

    private sealed class NullabilityProbeController : ControllerBase
    {
        public IActionResult Post(NullabilityProbeDto request) => Ok(request);

        public IActionResult Search(string query, string? hint) => Ok(new { query, hint });
    }

#pragma warning disable CS8618
    private sealed class NullabilityProbeDto
    {
        public string RequiredByNullability { get; set; }
        public string? OptionalByNullability { get; set; }
        public NullabilityProbeNestedDto Nested { get; set; }
    }

    private sealed class NullabilityProbeNestedDto
    {
        public string NestedRequired { get; set; }
        public string? NestedOptional { get; set; }
    }
#pragma warning restore CS8618
}
