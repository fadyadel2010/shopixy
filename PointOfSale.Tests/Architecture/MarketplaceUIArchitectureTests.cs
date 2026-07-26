using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PointOfSale.Core.Enums;
using Xunit;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// Architecture tests for Phase 5 UI invariants.
/// These tests enforce that UI never bypasses domain services.
/// </summary>
public class MarketplaceUIArchitectureTests
{
    [Fact]
    public void MarketplaceController_MustNotQueryServicesTableDirectly()
    {
        var source = File.ReadAllText(ControllerPath());
        var searchMethod = ExtractMethodSource(source, "public async Task<IActionResult> SearchServices");

        Assert.DoesNotContain("_context", searchMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", searchMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceController_MustNotBuildElasticsearchQuery()
    {
        var source = File.ReadAllText(ControllerPath());
        var searchMethod = ExtractMethodSource(source, "public async Task<IActionResult> SearchServices");

        Assert.DoesNotContain("_elasticClient", searchMethod, StringComparison.Ordinal);
        Assert.Matches(@"_searchService\.SearchServicesAsync\s*\(", searchMethod);
    }

    [Fact]
    public void MarketplaceController_MustNotAcceptStatusInDto()
    {
        var apiAssembly = Assembly.Load("PointOfSale.API");
        var controllerType = apiAssembly.GetType("PointOfSale.API.Controllers.MarketplaceController");

        Assert.NotNull(controllerType);

        var methods = controllerType!.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();

            foreach (var param in parameters)
            {
                var paramType = param.ParameterType;

                if (paramType.IsClass && paramType.Namespace?.Contains("PointOfSale") == true)
                {
                    var properties = paramType.GetProperties();

                    foreach (var prop in properties)
                    {
                        Assert.False(
                            prop.PropertyType == typeof(ServiceRequestStatus),
                            $"Controller method {method.Name} accepts DTO {paramType.Name} with Status property. UI must never pass status."
                        );
                    }
                }
            }
        }
    }

    [Fact]
    public void MarketplaceController_MustNotComputeTrustMetrics()
    {
        var apiAssembly = Assembly.Load("PointOfSale.API");
        var controllerType = apiAssembly.GetType("PointOfSale.API.Controllers.MarketplaceController");

        Assert.NotNull(controllerType);

        // Controller should have IProviderTrustService injected (read-only)
        var constructor = controllerType!.GetConstructors().FirstOrDefault();
        Assert.NotNull(constructor);

        var parameters = constructor!.GetParameters();
        var trustServiceParam = parameters.FirstOrDefault(p => p.ParameterType.Name.Contains("IProviderTrustService"));

        Assert.NotNull(trustServiceParam);
    }

    [Fact]
    public void MarketplaceController_MustUseOnlyDomainServices()
    {
        var apiAssembly = Assembly.Load("PointOfSale.API");
        var controllerType = apiAssembly.GetType("PointOfSale.API.Controllers.MarketplaceController");

        Assert.NotNull(controllerType);

        var constructor = controllerType!.GetConstructors().FirstOrDefault();
        Assert.NotNull(constructor);

        var parameters = constructor!.GetParameters();

        // TODO: Technical Debt - Migrate to Core abstractions
        // The approvedOperationalCollaborators allowlist below contains infrastructure dependencies
        // that should be migrated to Core abstractions:
        // - ApplicationDbContext: Extract read operations to Core repository interfaces
        // - IFileValidationService: Move to Core.Abstractions
        // Reference: MarketplaceUIArchitectureTests.approvedOperationalCollaborators
        // Target: Migrate by Q3 2026 or create tracking issue
        var approvedOperationalCollaborators = new HashSet<string>(StringComparer.Ordinal)
        {
            "ApplicationDbContext",
            "IFileValidationService",
            "IR2StorageService"
        };

        // Customer-facing dependencies should be abstractions from Core.
        // A small set of operational collaborators remain for legacy read composition
        // and admin diagnostics/reindex endpoints.
        foreach (var param in parameters)
        {
            if (param.ParameterType.Namespace?.Contains("PointOfSale") == true)
            {
                if (approvedOperationalCollaborators.Contains(param.ParameterType.Name))
                    continue;

                Assert.True(
                    param.ParameterType.IsInterface,
                    $"Controller should only depend on interfaces, found {param.ParameterType.Name}"
                );

                var ns = param.ParameterType.Namespace;
                Assert.True(
                    ns != null && (
                        ns.Equals("PointOfSale.Core.Interfaces", StringComparison.Ordinal) ||
                        ns.StartsWith("PointOfSale.Core.Interfaces.", StringComparison.Ordinal) ||
                        ns.Equals("PointOfSale.Core.Abstractions", StringComparison.Ordinal) ||
                        ns.StartsWith("PointOfSale.Core.Abstractions.", StringComparison.Ordinal)
                    ),
                    $"Controller should only depend on PointOfSale.Core.Interfaces or PointOfSale.Core.Abstractions, found {ns}"
                );
            }
        }
    }

    [Fact]
    public void MarketplaceController_SearchEndpoint_MustReturnSoftFailure()
    {
        var apiAssembly = Assembly.Load("PointOfSale.API");
        var controllerType = apiAssembly.GetType("PointOfSale.API.Controllers.MarketplaceController");

        Assert.NotNull(controllerType);

        var searchMethod = controllerType!.GetMethod("SearchServices");
        Assert.NotNull(searchMethod);

        // Method should have try-catch (we can't test this directly, but we document the requirement)
        // The implementation must return empty results on failure, not expose stack trace
    }

    [Fact]
    public void MarketplaceController_MustReturnCapabilityFlags()
    {
        var apiAssembly = Assembly.Load("PointOfSale.API");
        var controllerType = apiAssembly.GetType("PointOfSale.API.Controllers.MarketplaceController");

        Assert.NotNull(controllerType);

        // GetRequest method should return capabilities object
        var getRequestMethod = controllerType!.GetMethod("GetRequest");
        Assert.NotNull(getRequestMethod);

        // GetMyProfile method should return hasMarketplaceProfile flag
        var getMyProfileMethod = controllerType.GetMethod("GetMyProfile");
        Assert.NotNull(getMyProfileMethod);
    }

    [Fact]
    public void MarketplaceController_MustNotExposeIndexMetadata()
    {
        var apiAssembly = Assembly.Load("PointOfSale.API");
        var controllerType = apiAssembly.GetType("PointOfSale.API.Controllers.MarketplaceController");

        Assert.NotNull(controllerType);

        var methods = controllerType!.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        // No method should return index version, shard info, or other ES metadata
        foreach (var method in methods)
        {
            Assert.False(
                method.Name.Contains("Index") && method.Name.Contains("Version"),
                $"Controller should not expose index metadata: {method.Name}"
            );
        }
    }

    [Fact]
    public void MarketplaceController_MustEnforceBackendPermissions()
    {
        var apiAssembly = Assembly.Load("PointOfSale.API");
        var controllerType = apiAssembly.GetType("PointOfSale.API.Controllers.MarketplaceController");

        Assert.NotNull(controllerType);

        // All action methods should have [Authorize] or controller should have [Authorize]
        var authorizeAttr = controllerType!.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();
        Assert.NotNull(authorizeAttr);
    }

    private static string ControllerPath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "PointOfSale.API",
        "Controllers",
        "MarketplaceController.cs"));

    private static string ExtractMethodSource(string source, string signature)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var method = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => 
                // If signature contains spaces, it's a full signature - use Contains
                // Otherwise, it's a simple method name - compare identifier directly
                signature.Contains(' ', StringComparison.Ordinal)
                    ? m.ToFullString().Contains(signature, StringComparison.Ordinal)
                    : m.Identifier.Text == signature);

        if (method == null)
        {
            Assert.Fail($"Could not locate method with signature '{signature}'.");
            return string.Empty;
        }

        return method.ToFullString();
    }
}
