using System.Text.RegularExpressions;

namespace PointOfSale.Tests.Architecture;

public sealed class ProductVariantBoundedRetrySourceRegressionTests
{
    [Fact]
    public void CreateRedelivery_WithMatchingVariantEvent_ResumesSideEffectsWithoutMongoRewrite()
    {
        var source = ReadConsumer();
        var method = ExtractMethod(source, "public async Task Consume(ConsumeContext<ProductVariantCreatedEvent> context)");

        Assert.Contains("LastEventId = @event.EventId", source, StringComparison.Ordinal);
        Assert.Matches(
            @"if \(existingVariant\.LastEventId == @event\.EventId \|\|\s*existingProduct!\.LastEventId == @event\.EventId\)\s*\{(?<retry>[\s\S]*?)\}\s*else\s*\{(?<stale>[\s\S]*?)\}",
            method);

        var retry = MatchGroup(
            method,
            @"if \(existingVariant\.LastEventId == @event\.EventId \|\|\s*existingProduct!\.LastEventId == @event\.EventId\)\s*\{(?<body>[\s\S]*?)\}\s*else",
            "body");
        Assert.Contains("retry resuming downstream side effects without overwriting MongoDB", retry, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateOneAsync", retry, StringComparison.Ordinal);
        Assert.DoesNotContain("CompleteAsync", retry, StringComparison.Ordinal);
        Assert.DoesNotContain("return;", retry, StringComparison.Ordinal);
        AssertCommonSideEffectsRunBeforeFence(method, includeSearchSync: true);
    }

    [Fact]
    public void StockRedelivery_WithMatchingStockWatermark_ResumesButStaleEventsReturn()
    {
        var method = ExtractMethod(
            ReadConsumer(),
            "public async Task Consume(ConsumeContext<ProductVariantStockChangedEvent> context)");
        var retry = MatchGroup(
            method,
            @"if \(existingVariant\?\.LastStockEventId == @event\.EventId \|\|\s*product\.LastEventId == @event\.EventId\)\s*\{(?<body>[\s\S]*?)\}\s*else if \(IsStaleStockProjection",
            "body");

        Assert.Contains("retry resuming downstream side effects without overwriting MongoDB", retry, StringComparison.Ordinal);
        Assert.Contains("v.LastStockEventId == @event.EventId", method, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateOneAsync", retry, StringComparison.Ordinal);
        Assert.DoesNotContain("CompleteAsync", retry, StringComparison.Ordinal);
        Assert.DoesNotContain("return;", retry, StringComparison.Ordinal);
        Assert.Matches(@"else if \(IsStaleStockProjection[\s\S]*?fence\.CompleteAsync[\s\S]*?return;", method);
        AssertCommonSideEffectsRunBeforeFence(method, includeSearchSync: false);
    }

    [Fact]
    public void UpdateRedelivery_UsesEmbeddedEventIdAsAnAtomicMongoDuplicateGuard()
    {
        var method = ExtractMethod(
            ReadConsumer(),
            "public async Task Consume(ConsumeContext<ProductVariantUpdatedEvent> context)");

        Assert.Contains("v.LastEventId != @event.EventId", method, StringComparison.Ordinal);
        Assert.Contains("existingVariant?.LastEventId == @event.EventId", method, StringComparison.Ordinal);
        Assert.Contains("mongoUpdateApplied && @event.IsDefault == true", method, StringComparison.Ordinal);
        Assert.Contains("retry resuming downstream side effects without overwriting MongoDB", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteRedelivery_WithMatchingTombstone_ResumesButDifferentDeletesRemainRejected()
    {
        var method = ExtractMethod(
            ReadConsumer(),
            "public async Task Consume(ConsumeContext<ProductVariantDeletedEvent> context)");
        var retry = MatchGroup(
            method,
            @"if \(mongoDeleteAlreadyApplied\)\s*\{(?<body>[\s\S]*?)\}\s*else if",
            "body");

        Assert.Contains("tombstone.LastEventId == @event.EventId", method, StringComparison.Ordinal);
        Assert.Contains("variantToDelete == null", method, StringComparison.Ordinal);
        Assert.Contains("retry resuming downstream side effects without overwriting MongoDB", retry, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateOneAsync", retry, StringComparison.Ordinal);
        Assert.DoesNotContain("CompleteAsync", retry, StringComparison.Ordinal);
        Assert.DoesNotContain("return;", retry, StringComparison.Ordinal);
        Assert.Matches(@"if \(!mongoDeleteAlreadyApplied\)[\s\S]*?UpdateOneAsync\(deleteFilter[\s\S]*?stale or different delete ignored[\s\S]*?fence\.CompleteAsync[\s\S]*?return;", method);
        AssertCommonSideEffectsRunBeforeFence(method, includeSearchSync: true);
    }

    private static void AssertCommonSideEffectsRunBeforeFence(string method, bool includeSearchSync)
    {
        var invalidation = method.LastIndexOf("await InvalidateProductCacheAsync", StringComparison.Ordinal);
        var recomposition = method.LastIndexOf("await RecomposeParentProductProjectionAsync", StringComparison.Ordinal);
        var completion = method.LastIndexOf("await fence.CompleteAsync", StringComparison.Ordinal);

        Assert.True(invalidation >= 0 && invalidation < recomposition);
        Assert.True(recomposition < completion);
        if (includeSearchSync)
        {
            var searchSync = method.LastIndexOf("await SyncHasVariantsToElasticsearchAsync", StringComparison.Ordinal);
            Assert.True(recomposition < searchSync && searchSync < completion);
        }
    }

    private static string MatchGroup(string source, string pattern, string groupName)
    {
        var match = Regex.Match(source, pattern, RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Pattern was not found: {pattern}");
        return match.Groups[groupName].Value;
    }

    private static string ReadConsumer() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(AppContext.BaseDirectory), "PointOfSale.Infrastructure", "Consumers", "ProductVariantProjectionConsumer.cs"));

    private static string ExtractMethod(string source, string signature)
    {
        var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Method signature '{signature}' was not found.");

        var bodyStart = source.IndexOf('{', signatureIndex);
        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            depth += source[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0
            };

            if (depth == 0)
            {
                return source[signatureIndex..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Method body for '{signature}' was not balanced.");
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
