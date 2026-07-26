using System.Reflection;
using PointOfSale.Core.Entities;
using Xunit;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PHASE CR-1B: Architecture tests for surgical fixes.
/// Ensures invariant-safe fixes remain in place.
/// </summary>
public class CR1BKillListArchitectureTests
{
    // =========================================================================
    // FIX 1 VERIFICATION: ProviderTrustAggregate Concurrency Token
    // =========================================================================

    [Fact]
    public void ProviderTrustAggregate_Version_MustExist_And_Increment()
    {
        // Verify Version property exists and has private setter
        var versionProp = typeof(ProviderTrustAggregate).GetProperty(
            "Version", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(versionProp);
        Assert.True(versionProp!.CanRead, "Version must be readable");

        var publicSetter = versionProp.GetSetMethod(nonPublic: false);
        Assert.Null(publicSetter); // Version must NOT have a public setter

        // Verify ApplyReview increments Version
        var aggregate = ProviderTrustAggregate.Create(Guid.NewGuid(), Guid.NewGuid());
        var initialVersion = aggregate.Version;

        aggregate.ApplyReview(5);

        Assert.Equal(initialVersion + 1, aggregate.Version);
    }

    [Fact]
    public void ProviderTrustAggregate_ConcurrencyToken_IsConfigured()
    {
        // Verify via EF configuration file that .IsConcurrencyToken() is present
        var configFilePath = FindFile(
            "ProviderTrustAggregateConfiguration.cs",
            Path.Combine(GetSolutionRoot(), "PointOfSale.Infrastructure"));

        Assert.NotNull(configFilePath);

        var content = File.ReadAllText(configFilePath!);

        Assert.True(content.Contains(".IsConcurrencyToken()"),
            "ProviderTrustAggregateConfiguration must configure Version as IsConcurrencyToken(). " +
            "Without this, DbUpdateConcurrencyException will never fire and concurrent reviews can silently overwrite trust data.");
    }

    // =========================================================================
    // FIX 2 VERIFICATION: CancelRequestAsync must use IgnoreQueryFilters
    // =========================================================================

    [Fact]
    public void CancelRequestAsync_MustUseIgnoreQueryFilters()
    {
        // All ServiceRequest query methods in ServiceRequestService MUST use IgnoreQueryFilters
        // because marketplace requests are cross-tenant (buyer TenantA, provider TenantB)
        var serviceFilePath = FindFile(
            "ServiceRequestService.cs",
            Path.Combine(GetSolutionRoot(), "PointOfSale.Infrastructure"));

        Assert.NotNull(serviceFilePath);

        var content = File.ReadAllText(serviceFilePath!);
        var lines = content.Split('\n');

        // Find CancelRequestAsync method
        int cancelMethodStart = -1;
        int cancelMethodEnd = -1;
        int braceCount = 0;
        bool insideMethod = false;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("CancelRequestAsync"))
            {
                cancelMethodStart = i;
                insideMethod = true;
            }

            if (insideMethod)
            {
                braceCount += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');
                if (braceCount == 0 && cancelMethodStart != i)
                {
                    cancelMethodEnd = i;
                    break;
                }
            }
        }

        Assert.True(cancelMethodStart >= 0, "CancelRequestAsync method must exist in ServiceRequestService");
        Assert.True(cancelMethodEnd > cancelMethodStart, "CancelRequestAsync must have a body");

        var methodBody = string.Join('\n', lines[cancelMethodStart..cancelMethodEnd]);

        Assert.True(methodBody.Contains("IgnoreQueryFilters()"),
            "CancelRequestAsync MUST use .IgnoreQueryFilters() because marketplace service requests " +
            "are cross-tenant. Without this, a buyer in TenantA cannot cancel a request with a provider in TenantB.");
    }

    [Fact]
    public void AllServiceRequestQueryMethods_MustUseIgnoreQueryFilters()
    {
        // Every method in ServiceRequestService that queries ServiceRequest MUST use IgnoreQueryFilters
        var serviceFilePath = FindFile(
            "ServiceRequestService.cs",
            Path.Combine(GetSolutionRoot(), "PointOfSale.Infrastructure"));

        Assert.NotNull(serviceFilePath);

        var content = File.ReadAllText(serviceFilePath!);
        var lines = content.Split('\n');

        // Find all Set<ServiceRequest>() calls and verify each has IgnoreQueryFilters
        var violations = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("Set<ServiceRequest>()"))
            {
                // Check if IgnoreQueryFilters follows (within next 2 lines)
                bool hasFilter = false;
                for (int j = i; j < Math.Min(i + 3, lines.Length); j++)
                {
                    if (lines[j].Contains("IgnoreQueryFilters()"))
                    {
                        hasFilter = true;
                        break;
                    }
                }

                // Skip .Add() calls - those don't need IgnoreQueryFilters
                if (lines[i].Contains(".Add(")) continue;
                bool isAddCall = false;
                for (int j = i; j < Math.Min(i + 2, lines.Length); j++)
                {
                    if (lines[j].Contains(".Add("))
                    {
                        isAddCall = true;
                        break;
                    }
                }
                if (isAddCall) continue;

                if (!hasFilter)
                {
                    violations.Add($"Line {i + 1}: Set<ServiceRequest>() without IgnoreQueryFilters()");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"All ServiceRequest queries MUST use IgnoreQueryFilters() for cross-tenant marketplace support. " +
            $"Violations:\n{string.Join("\n", violations)}");
    }

    // =========================================================================
    // FIX 3 VERIFICATION: Event publishing must happen AFTER SaveChangesAsync
    // =========================================================================

    [Fact]
    public void ServiceCatalogService_EventPublish_MustBeAfterSaveChanges()
    {
        var serviceFilePath = FindFile(
            "ServiceCatalogService.cs",
            Path.Combine(GetSolutionRoot(), "PointOfSale.Infrastructure"));

        Assert.NotNull(serviceFilePath);

        var content = File.ReadAllText(serviceFilePath!);
        var lines = content.Split('\n');

        // Find all methods that contain both Publish and SaveChangesAsync
        var methodNames = new[] { "CreateServiceAsync", "UpdateServiceAsync", "DeactivateServiceAsync", "ActivateServiceAsync" };
        var violations = new List<string>();

        foreach (var methodName in methodNames)
        {
            int methodStart = -1;
            int braceCount = 0;
            bool insideMethod = false;
            int firstPublishLine = -1;
            int firstSaveChangesLine = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(methodName) && !insideMethod)
                {
                    methodStart = i;
                    insideMethod = true;
                    braceCount = 0;
                    firstPublishLine = -1;
                    firstSaveChangesLine = -1;
                }

                if (insideMethod)
                {
                    braceCount += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');

                    if (lines[i].Contains("SaveChangesAsync") && firstSaveChangesLine == -1)
                        firstSaveChangesLine = i;

                    if (lines[i].Contains("_publishEndpoint.Publish") && firstPublishLine == -1)
                        firstPublishLine = i;

                    if (braceCount == 0 && i > methodStart)
                    {
                        // End of method
                        if (firstPublishLine >= 0 && firstSaveChangesLine >= 0)
                        {
                            if (firstPublishLine < firstSaveChangesLine)
                            {
                                violations.Add(
                                    $"{methodName}: Publish (line {firstPublishLine + 1}) occurs BEFORE " +
                                    $"SaveChangesAsync (line {firstSaveChangesLine + 1}). " +
                                    "This can produce ghost records in Elasticsearch if DB save fails.");
                            }
                        }
                        insideMethod = false;
                        break;
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Event publishing MUST occur AFTER SaveChangesAsync to prevent ghost records. " +
            $"Violations:\n{string.Join("\n", violations)}");
    }

    // =========================================================================
    // FIX 4 VERIFICATION: ChatPage must not use hardcoded user ID
    // =========================================================================

    [Fact]
    public void ChatPage_MustNotContainHardcodedUserId()
    {
        var chatPagePath = FindFile(
            "ChatPage.tsx",
            Path.Combine(GetSolutionRoot(), "PointOfSale.Web"));

        Assert.NotNull(chatPagePath);

        var content = File.ReadAllText(chatPagePath!);

        Assert.False(content.Contains("current-user-id"),
            "ChatPage.tsx must NOT use hardcoded 'current-user-id'. " +
            "Use the authenticated user's ID from auth store instead.");

        // Verify it imports auth store
        Assert.True(
            content.Contains("useAuthStore") || content.Contains("useAuth"),
            "ChatPage.tsx must import auth store/hook to get current user ID for ownership detection.");
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static string GetSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.GetFiles(dir, "*.sln").Any())
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        return dir ?? throw new InvalidOperationException("Could not find solution root");
    }

    private static string? FindFile(string fileName, string searchDir)
    {
        if (!Directory.Exists(searchDir)) return null;

        var files = Directory.GetFiles(searchDir, fileName, SearchOption.AllDirectories);
        return files.FirstOrDefault();
    }

    // =========================================================================
    // FIX 5 VERIFICATION: ServiceReviewService must use IgnoreQueryFilters
    // =========================================================================

    [Fact]
    public void ServiceReviewService_ReadMethods_MustUseIgnoreQueryFilters()
    {
        var serviceFilePath = FindFile(
            "ServiceReviewService.cs",
            Path.Combine(GetSolutionRoot(), "PointOfSale.Infrastructure"));

        Assert.NotNull(serviceFilePath);
        var content = File.ReadAllText(serviceFilePath!);
        var lines = content.Split('\n');

        var methods = new[] { 
            "GetReviewByRequestIdAsync", 
            "GetReviewsByServiceIdAsync", 
            "GetReviewsByProviderIdAsync" 
        };

        foreach (var method in methods)
        {
            CheckMethodForIgnoreQueryFilters(lines, method);
        }
    }

    // =========================================================================
    // FIX 6 VERIFICATION: MarketplaceProfileService must use IgnoreQueryFilters
    // =========================================================================

    [Fact]
    public void MarketplaceProfileService_ReadMethods_MustUseIgnoreQueryFilters()
    {
        var serviceFilePath = FindFile(
            "MarketplaceProfileService.cs",
            Path.Combine(GetSolutionRoot(), "PointOfSale.Infrastructure"));

        Assert.NotNull(serviceFilePath);
        var content = File.ReadAllText(serviceFilePath!);
        var lines = content.Split('\n');

        var methods = new[] { "GetProfileByUserIdAsync", "GetProfileByIdAsync" };

        foreach (var method in methods)
        {
            CheckMethodForIgnoreQueryFilters(lines, method);
        }
    }

    // =========================================================================
    // FIX 7 VERIFICATION: ProviderTrustService must use IgnoreQueryFilters
    // =========================================================================

    [Fact]
    public void ProviderTrustService_ReadMethods_MustUseIgnoreQueryFilters()
    {
        var serviceFilePath = FindFile(
            "ProviderTrustService.cs",
            Path.Combine(GetSolutionRoot(), "PointOfSale.Infrastructure"));

        Assert.NotNull(serviceFilePath);
        var content = File.ReadAllText(serviceFilePath!);
        var lines = content.Split('\n');

        var methods = new[] { "GetTrustAggregateAsync", "GetTopRatedProvidersAsync" };

        foreach (var method in methods)
        {
            CheckMethodForIgnoreQueryFilters(lines, method);
        }
    }

    private void CheckMethodForIgnoreQueryFilters(string[] lines, string methodName)
    {
        bool methodFound = false;
        bool filterFound = false;
        int braceCount = 0;
        bool enteredMethod = false;
        
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(methodName) && lines[i].Contains("public async Task"))
            {
                methodFound = true;
                filterFound = false;
                braceCount = 0; 
                enteredMethod = false;
                
                for (int j = i; j < lines.Length; j++)
                {
                    braceCount += lines[j].Count(c => c == '{') - lines[j].Count(c => c == '}');
                    
                    if (braceCount > 0) enteredMethod = true;

                    if (lines[j].Contains("IgnoreQueryFilters()"))
                    {
                        filterFound = true;
                    }

                    // Check for end of method
                    if (enteredMethod && braceCount == 0) 
                    {
                        break;
                    }
                }

                Assert.True(filterFound, $"{methodName} must use .IgnoreQueryFilters() for cross-tenant visibility.");
                return; 
            }
        }
        Assert.True(methodFound, $"Method {methodName} not found.");
    }
}
