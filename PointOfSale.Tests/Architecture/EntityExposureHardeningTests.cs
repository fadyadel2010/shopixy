using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Infrastructure.Mvc;
using PointOfSale.Core.Entities;

namespace PointOfSale.Tests.Architecture;

public sealed class EntityExposureHardeningTests
{
    [Fact]
    public void Controllers_MustNotAdvertise_EfEntities_InActionResultSignatures()
    {
        var entityNames = GetEntityNames();
        var violations = new List<string>();

        foreach (var controllerPath in EnumerateControllerFiles())
        {
            var text = File.ReadAllText(controllerPath);
            var tree = CSharpSyntaxTree.ParseText(text, path: controllerPath);
            var root = tree.GetRoot();

            var actionResultGenerics = root.DescendantNodes()
                .OfType<GenericNameSyntax>()
                .Where(gns => gns.Identifier.Text == "ActionResult");

            foreach (var genericNode in actionResultGenerics)
            {
                if (genericNode.TypeArgumentList.Arguments.Count != 1)
                {
                    continue;
                }

                var typeArg = genericNode.TypeArgumentList.Arguments[0];
                var typeText = typeArg.ToString();

                if (entityNames.Any(entityName => Regex.IsMatch(typeText, $@"\b{Regex.Escape(entityName)}\b")))
                {
                    var lineSpan = tree.GetLineSpan(genericNode.Span);
                    var lineNumber = lineSpan.StartLinePosition.Line + 1;
                    violations.Add($"{Path.GetFileName(controllerPath)}:{lineNumber} => ActionResult<{typeText}>");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Controller action signatures must use DTOs instead of EF entities." + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Controllers_MustNotReturn_DbBackedIdentifiers_Directly()
    {
        var violations = new List<string>();

        var directContextReturnPattern = new Regex(
            @"var\s+(?<name>\w+)\s*=\s*await\s+_context\.[^;]*(FirstOrDefaultAsync|SingleOrDefaultAsync|ToListAsync|FindAsync)\([^;]*\);(?:(?!return).)*return\s+(Ok\(\k<name>\)|\k<name>\s*;|CreatedAtAction\([^\)]*,\s*\k<name>\))",
            RegexOptions.Singleline | RegexOptions.Multiline);

        var queryMaterializationReturnPattern = new Regex(
            @"var\s+(?<query>\w+)\s*=\s*_context\.[^;]+;(?:(?!return).)*var\s+(?<name>\w+)\s*=\s*await\s+\k<query>\.[^;]*ToListAsync\([^;]*\);(?:(?!return).)*return\s+Ok\(\k<name>\);",
            RegexOptions.Singleline | RegexOptions.Multiline);

        foreach (var controllerPath in EnumerateControllerFiles())
        {
            var source = File.ReadAllText(controllerPath);

            foreach (Match match in directContextReturnPattern.Matches(source))
            {
                if (!match.Value.Contains("Select(", StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetFileName(controllerPath)} => {CollapseWhitespace(match.Value)}");
                }
            }

            foreach (Match match in queryMaterializationReturnPattern.Matches(source))
            {
                if (!match.Value.Contains("Select(", StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetFileName(controllerPath)} => {CollapseWhitespace(match.Value)}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Controllers must not directly return DbContext-backed entities or collections." + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Controllers_MustNotReturn_DeferredSequences_FromActionPayloads()
    {
        var violations = new List<string>();

        foreach (var controllerPath in EnumerateControllerFiles())
        {
            var text = File.ReadAllText(controllerPath);
            var tree = CSharpSyntaxTree.ParseText(text, path: controllerPath);
            var root = tree.GetRoot();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var localInitializers = method.Body?
                    .DescendantNodes()
                    .OfType<VariableDeclaratorSyntax>()
                    .Where(variable => variable.Initializer != null)
                    .GroupBy(variable => variable.Identifier.ValueText, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Last().Initializer!.Value,
                        StringComparer.Ordinal)
                    ?? new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);

                void AddDeferredSequenceViolation(ExpressionSyntax? expression)
                {
                    if (expression == null)
                    {
                        return;
                    }

                    var payload = TryGetObjectResultPayload(expression);
                    if (payload == null)
                    {
                        return;
                    }

                    if (!ContainsDeferredSequence(payload, localInitializers, new HashSet<string>(StringComparer.Ordinal)))
                    {
                        return;
                    }

                    var lineSpan = tree.GetLineSpan(payload.Span);
                    var lineNumber = lineSpan.StartLinePosition.Line + 1;
                    violations.Add($"{Path.GetFileName(controllerPath)}:{lineNumber} => {CollapseWhitespace(payload.ToString())}");
                }

                AddDeferredSequenceViolation(method.ExpressionBody?.Expression);

                foreach (var returnStatement in method.DescendantNodes().OfType<ReturnStatementSyntax>())
                {
                    AddDeferredSequenceViolation(returnStatement.Expression);
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Controllers must fully materialize response payloads before returning them." + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public async Task PreventEntityExposureFilter_MustBlock_DirectEntityPayloads()
    {
        var result = await ExecuteFilterAsync(new ObjectResult(new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Name = "Sensitive supplier",
            Email = "supplier@example.com"
        }));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
        Assert.Equal("ApiErrorResponse", objectResult.Value?.GetType().Name);
    }

    [Fact]
    public async Task PreventEntityExposureFilter_MustBlock_IQueryableEntityPayloads()
    {
        var payload = new List<Supplier>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                Name = "Queryable supplier"
            }
        }.AsQueryable();

        var result = await ExecuteFilterAsync(new ObjectResult(payload));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    [Fact]
    public async Task PreventEntityExposureFilter_MustBlock_ProxyLikeNestedEntityPayloads()
    {
        var payload = new DeepWrapper
        {
            Child = new DeepWrapper
            {
                Child = new DeepWrapper
                {
                    Child = new DeepWrapper
                    {
                        Child = new DeepWrapper
                        {
                            Child = new SupplierProxy
                            {
                                Id = Guid.NewGuid(),
                                TenantId = Guid.NewGuid(),
                                Name = "Proxy supplier"
                            }
                        }
                    }
                }
            }
        };

        var result = await ExecuteFilterAsync(new ObjectResult(payload));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    [Fact]
    public async Task PreventEntityExposureFilter_MustAllow_DtoPayloads()
    {
        var payload = new SafePayloadDto
        {
            Id = Guid.NewGuid(),
            Name = "Safe",
            Items =
            [
                new SafePayloadItemDto
                {
                    Label = "Visible"
                }
            ]
        };

        var originalResult = new ObjectResult(payload);
        var filteredResult = await ExecuteFilterAsync(originalResult);

        Assert.Same(originalResult, filteredResult);
    }

    private static async Task<IActionResult> ExecuteFilterAsync(IActionResult result)
    {
        var filter = new PreventEntityExposureFilter(
            NullLogger<PreventEntityExposureFilter>.Instance,
            Options.Create(new JsonOptions()));

        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
        var filters = new List<IFilterMetadata>();
        var context = new ResultExecutingContext(actionContext, filters, result, controller: new object());

        await filter.OnResultExecutionAsync(
            context,
            () => Task.FromResult(new ResultExecutedContext(actionContext, filters, context.Result, controller: new object())));

        return context.Result;
    }

    private static HashSet<string> GetEntityNames()
    {
        var entitiesRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Core",
            "Entities"));

        return Directory
            .EnumerateFiles(entitiesRoot, "*.cs", SearchOption.AllDirectories)
            .Select(filePath => Path.GetFileNameWithoutExtension(filePath)!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumerateControllerFiles()
    {
        var apiControllersRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers"));

        var superAdminControllersRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "SuperAdmin",
            "Controllers"));

        return Directory
            .EnumerateFiles(apiControllersRoot, "*Controller.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(superAdminControllersRoot, "*Controller.cs", SearchOption.AllDirectories));
    }

    private static ExpressionSyntax? TryGetObjectResultPayload(ExpressionSyntax expression)
    {
        expression = Unwrap(expression);

        if (expression is InvocationExpressionSyntax invocation)
        {
            var methodName = GetInvocationMethodName(invocation);
            if (methodName is "Ok" or "Json" or "BadRequest" or "Conflict")
            {
                return invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            }

            if (methodName is "Created" or "CreatedAtAction" or "CreatedAtRoute" or "Accepted" or "AcceptedAtAction" or "AcceptedAtRoute")
            {
                return invocation.ArgumentList.Arguments.LastOrDefault()?.Expression;
            }

            if (methodName == "StatusCode")
            {
                return invocation.ArgumentList.Arguments.ElementAtOrDefault(1)?.Expression;
            }
        }

        if (expression is ObjectCreationExpressionSyntax objectCreation)
        {
            var typeName = GetTypeName(objectCreation.Type);
            if (typeName is "OkObjectResult" or "ObjectResult")
            {
                return objectCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
            }
        }

        return null;
    }

    private static bool ContainsDeferredSequence(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, ExpressionSyntax> localInitializers,
        ISet<string> visiting)
    {
        expression = Unwrap(expression);

        switch (expression)
        {
            case IdentifierNameSyntax identifier:
                return ResolveIdentifier(identifier, localInitializers, visiting);

            case AssignmentExpressionSyntax assignment:
                return ContainsDeferredSequence(assignment.Right, localInitializers, visiting);

            case ConditionalExpressionSyntax conditional:
                return ContainsDeferredSequence(conditional.WhenTrue, localInitializers, visiting)
                    || ContainsDeferredSequence(conditional.WhenFalse, localInitializers, visiting);

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression):
                return ContainsDeferredSequence(binary.Left, localInitializers, visiting)
                    || ContainsDeferredSequence(binary.Right, localInitializers, visiting);

            case AnonymousObjectCreationExpressionSyntax anonymousObject:
                return anonymousObject.Initializers.Any(initializer =>
                    ContainsDeferredSequence(initializer.Expression, localInitializers, visiting));

            case ObjectCreationExpressionSyntax objectCreation:
                if (IsSafeMaterializingCollectionCreation(objectCreation))
                {
                    return false;
                }

                return (objectCreation.ArgumentList?.Arguments.Any(argument =>
                        ContainsDeferredSequence(argument.Expression, localInitializers, visiting)) ?? false)
                    || (objectCreation.Initializer?.Expressions.Any(initializer =>
                        initializer is AssignmentExpressionSyntax assignmentExpression
                            ? ContainsDeferredSequence(assignmentExpression.Right, localInitializers, visiting)
                            : ContainsDeferredSequence(initializer, localInitializers, visiting)) ?? false);

            case InitializerExpressionSyntax initializer:
                return initializer.Expressions.Any(item =>
                    item is AssignmentExpressionSyntax assignmentExpression
                        ? ContainsDeferredSequence(assignmentExpression.Right, localInitializers, visiting)
                        : ContainsDeferredSequence(item, localInitializers, visiting));

            case ConditionalAccessExpressionSyntax conditionalAccess:
                return ContainsDeferredSequence(conditionalAccess.WhenNotNull, localInitializers, visiting);

            case InvocationExpressionSyntax invocation:
                if (IsSafeImmediateConsumer(invocation))
                {
                    return false;
                }

                if (LooksLikeDeferredSequence(invocation))
                {
                    return true;
                }

                return invocation.ArgumentList.Arguments.Any(argument =>
                    ContainsDeferredSequence(argument.Expression, localInitializers, visiting));

            default:
                return false;
        }
    }

    private static bool ResolveIdentifier(
        IdentifierNameSyntax identifier,
        IReadOnlyDictionary<string, ExpressionSyntax> localInitializers,
        ISet<string> visiting)
    {
        var name = identifier.Identifier.ValueText;
        if (!localInitializers.TryGetValue(name, out var initializer))
        {
            return false;
        }

        if (!visiting.Add(name))
        {
            return false;
        }

        try
        {
            return ContainsDeferredSequence(initializer, localInitializers, visiting);
        }
        finally
        {
            visiting.Remove(name);
        }
    }

    private static bool LooksLikeDeferredSequence(InvocationExpressionSyntax invocation)
    {
        var methodName = GetInvocationMethodName(invocation);
        return methodName is
            "Where" or
            "Select" or
            "SelectMany" or
            "OrderBy" or
            "OrderByDescending" or
            "ThenBy" or
            "ThenByDescending" or
            "Skip" or
            "Take" or
            "Distinct" or
            "DistinctBy" or
            "GroupBy" or
            "Append" or
            "Prepend" or
            "Concat" or
            "Union" or
            "Intersect" or
            "Except" or
            "Reverse" or
            "DefaultIfEmpty" or
            "OfType" or
            "Cast" or
            "TakeWhile" or
            "SkipWhile" or
            "AsEnumerable" or
            "AsQueryable";
    }

    private static bool IsSafeImmediateConsumer(InvocationExpressionSyntax invocation)
    {
        var methodName = GetInvocationMethodName(invocation);

        if (methodName is
            "ToList" or
            "ToListAsync" or
            "ToArray" or
            "ToArrayAsync" or
            "ToDictionary" or
            "ToDictionaryAsync" or
            "ToHashSet" or
            "First" or
            "FirstAsync" or
            "FirstOrDefault" or
            "FirstOrDefaultAsync" or
            "Single" or
            "SingleAsync" or
            "SingleOrDefault" or
            "SingleOrDefaultAsync" or
            "Last" or
            "LastAsync" or
            "LastOrDefault" or
            "LastOrDefaultAsync" or
            "Count" or
            "CountAsync" or
            "LongCount" or
            "LongCountAsync" or
            "Any" or
            "AnyAsync" or
            "All" or
            "Contains" or
            "ContainsAsync" or
            "Sum" or
            "SumAsync" or
            "Average" or
            "AverageAsync" or
            "Min" or
            "MinAsync" or
            "Max" or
            "MaxAsync" or
            "ElementAt" or
            "ElementAtOrDefault")
        {
            return true;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.ValueText is "string" or "String" &&
            methodName is "Join" or "Concat")
        {
            return true;
        }

        return false;
    }

    private static bool IsSafeMaterializingCollectionCreation(ObjectCreationExpressionSyntax objectCreation)
    {
        var typeName = GetTypeName(objectCreation.Type);
        return typeName is "List" or "HashSet" or "Dictionary" or "SortedSet" or "Queue" or "Stack";
    }

    private static string GetInvocationMethodName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => GetSimpleName(memberAccess.Name),
            MemberBindingExpressionSyntax memberBinding => GetSimpleName(memberBinding.Name),
            _ => string.Empty
        };

    private static string GetTypeName(TypeSyntax typeSyntax) =>
        typeSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            QualifiedNameSyntax qualifiedName => GetTypeName(qualifiedName.Right),
            AliasQualifiedNameSyntax aliasQualifiedName => GetTypeName(aliasQualifiedName.Name),
            NullableTypeSyntax nullableType => GetTypeName(nullableType.ElementType),
            _ => typeSyntax.ToString()
        };

    private static string GetSimpleName(SimpleNameSyntax nameSyntax) =>
        nameSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            _ => nameSyntax.Identifier.ValueText
        };

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case AwaitExpressionSyntax awaitExpression:
                    expression = awaitExpression.Expression;
                    continue;
                case CastExpressionSyntax castExpression:
                    expression = castExpression.Expression;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static string CollapseWhitespace(string source) =>
        Regex.Replace(source, @"\s+", " ").Trim();

    private sealed class DeepWrapper
    {
        public object? Child { get; set; }
    }

    private sealed class SupplierProxy : Supplier
    {
    }

    private sealed class SafePayloadDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<SafePayloadItemDto> Items { get; set; } = Array.Empty<SafePayloadItemDto>();
    }

    private sealed class SafePayloadItemDto
    {
        public string Label { get; set; } = string.Empty;
    }
}
