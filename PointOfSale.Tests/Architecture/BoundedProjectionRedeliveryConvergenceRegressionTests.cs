using System.Text.RegularExpressions;

namespace PointOfSale.Tests.Architecture;

public sealed class BoundedProjectionRedeliveryConvergenceRegressionTests
{
    [Fact]
    public void ProductMediaRedelivery_WithMatchingMongoEvent_ResumesSharedSideEffectsWithoutMongoRewrite()
    {
        var consumer = ReadConsumer("ProductMediaChangedEventConsumer.cs");
        var matchingEventBranch = Regex.Match(
            consumer,
            @"if \(existingEventId == @event\.EventId\)\s*\{(?<body>[\s\S]*?)\}\s*else",
            RegexOptions.CultureInvariant);

        Assert.True(matchingEventBranch.Success);
        Assert.Contains(
            "retry resuming downstream side effects without overwriting MongoDB",
            matchingEventBranch.Groups["body"].Value,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateOneAsync", matchingEventBranch.Groups["body"].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("CompleteAsync", matchingEventBranch.Groups["body"].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("return;", matchingEventBranch.Groups["body"].Value, StringComparison.Ordinal);

        Assert.Matches(
            @"if \(existingEventId != null && existingEventId != Guid\.Empty\)[\s\S]*existingEventId == @event\.EventId[\s\S]*\}\s*else\s*\{\s*// Product doesn't exist[\s\S]*throw new InvalidOperationException[\s\S]*ProductSearchSqlAuthorityOverlay\.ApplyAsync[\s\S]*(DeleteProductAsync|IndexProductAsync)[\s\S]*InvalidateProductAsync[\s\S]*fence\.CompleteAsync",
            consumer);
    }

    [Fact]
    public void ProductMediaRedelivery_WithDifferentMongoEvent_RemainsStaleAndStopsSideEffects()
    {
        var consumer = ReadConsumer("ProductMediaChangedEventConsumer.cs");

        Assert.Contains("filter.Ne(p => p.LastMediaEventId, eventId)", consumer, StringComparison.Ordinal);
        Assert.Matches(
            @"else\s*\{\s*// Different EventId[\s\S]*?projection already has newer media[\s\S]*?fence\.CompleteAsync[\s\S]*?return;\s*\}",
            consumer);
    }

    [Fact]
    public void CommerceHubProductUpdatedRedelivery_RetriesOnlyBoundedPendingOrFailedSearchSync()
    {
        var consumer = ReadConsumer("CommerceHubProjectionConsumer.cs");
        var productUpdatedConsume = ExtractMethod(
            consumer,
            "public async Task Consume(ConsumeContext<ProductUpdatedEvent> context)");

        Assert.Matches(
            @"if \(listing\.LastEventId == message\.EventId\)\s*\{\s*if \(RequiresSearchSyncRetry\(listing\)\)\s*\{\s*await SyncSearchAsync\(listing, context\.CancellationToken\);\s*\}\s*continue;\s*\}",
            productUpdatedConsume);
        Assert.Contains(
            "document.SearchSyncStatus is CommerceHubProductDocument.SearchSyncFailed or CommerceHubProductDocument.SearchSyncPending",
            consumer,
            StringComparison.Ordinal);
        Assert.Contains(
            "document.SearchRetryCount < CommerceHubProductDocument.MaxSearchRetries",
            consumer,
            StringComparison.Ordinal);
    }

    private static string ReadConsumer(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(AppContext.BaseDirectory), "PointOfSale.Infrastructure", "Consumers", fileName));

    private static string ExtractMethod(string source, string signature)
    {
        var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Method signature '{signature}' was not found.");

        var bodyStart = source.IndexOf('{', signatureIndex);
        Assert.True(bodyStart >= 0, $"Method body for '{signature}' was not found.");

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
