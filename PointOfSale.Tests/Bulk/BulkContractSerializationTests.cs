using System.Text.Json;
using System.Text.Json.Nodes;
using PointOfSale.Core.DTOs.Bulk;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkContractSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void BulkItemStatus_SerializesAsLowercase()
    {
        var json = JsonSerializer.Serialize(BulkItemStatus.Processed, JsonOptions);
        Assert.Equal("\"processed\"", json);

        json = JsonSerializer.Serialize(BulkItemStatus.Unchanged, JsonOptions);
        Assert.Equal("\"unchanged\"", json);

        json = JsonSerializer.Serialize(BulkItemStatus.Failed, JsonOptions);
        Assert.Equal("\"failed\"", json);
    }

    [Fact]
    public void BulkItemStatus_DeserializesFromLowercase()
    {
        Assert.Equal(BulkItemStatus.Processed, JsonSerializer.Deserialize<BulkItemStatus>("\"processed\"", JsonOptions));
        Assert.Equal(BulkItemStatus.Unchanged, JsonSerializer.Deserialize<BulkItemStatus>("\"unchanged\"", JsonOptions));
        Assert.Equal(BulkItemStatus.Failed, JsonSerializer.Deserialize<BulkItemStatus>("\"failed\"", JsonOptions));
    }

    [Fact]
    public void BulkItemResult_SerializesStatusAsLowercase()
    {
        var result = new BulkItemResult(
            Guid.NewGuid(),
            BulkItemStatus.Failed,
            "NOT_FOUND",
            "Product was not found.",
            "1",
            null);

        var json = JsonSerializer.Serialize(result, JsonOptions);
        var node = JsonNode.Parse(json)!;

        Assert.Equal("failed", node["status"]?.ToString(), ignoreCase: false);
        Assert.Equal("NOT_FOUND", node["code"]?.ToString());
        Assert.DoesNotContain("ProductId", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BulkOperationResult_HasRequiredTopLevelFields()
    {
        var productId = Guid.NewGuid();
        var result = new BulkOperationResult(
            Requested: 3,
            Accepted: 2,
            Processed: 1,
            Unchanged: 0,
            Failed: 1,
            CorrelationId: "corr-123",
            Results: new[]
            {
                new BulkItemResult(productId, BulkItemStatus.Processed, null, null, "1", "2"),
                new BulkItemResult(productId, BulkItemStatus.Failed, "NOT_FOUND", "Product was not found.", "2", null)
            });

        var json = JsonSerializer.Serialize(result, JsonOptions);
        var node = JsonNode.Parse(json)!;

        Assert.Equal(3, (int)node["requested"]!);
        Assert.Equal(2, (int)node["accepted"]!);
        Assert.Equal(1, (int)node["processed"]!);
        Assert.Equal(0, (int)node["unchanged"]!);
        Assert.Equal(1, (int)node["failed"]!);
        Assert.Equal("corr-123", node["correlationId"]!.ToString());
        Assert.Equal(2, node["results"]!.AsArray().Count);
    }

    [Fact]
    public void BulkOperationResult_DoesNotSerializeNestedSummaryOrItems()
    {
        var result = new BulkOperationResult(
            Requested: 2,
            Accepted: 2,
            Processed: 0,
            Unchanged: 0,
            Failed: 0,
            CorrelationId: "corr",
            Results: Array.Empty<BulkItemResult>());

        var json = JsonSerializer.Serialize(result, JsonOptions);
        var node = JsonNode.Parse(json)!;

        Assert.DoesNotContain("summary", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("items", node.AsObject().Select(p => p.Key), StringComparer.Ordinal);
    }

    [Fact]
    public void BulkProductRequest_HasNoTenantUserOrActionProperty()
    {
        var request = new BulkProductRequest(
            [new BulkProductRequestItem(Guid.NewGuid().ToString("N"), "1")]);

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var node = JsonNode.Parse(json)!;

        var propertyNames = node.AsObject().Select(p => p.Key).ToArray();

        Assert.Contains("items", propertyNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(propertyNames, name =>
            name.Equals("tenantId", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("tenant", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("userId", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("user", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("action", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("operation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BulkItemResult_NullCodeMessageVersionAreOmitted()
    {
        var result = new BulkItemResult(
            Guid.NewGuid(),
            BulkItemStatus.Unchanged,
            null,
            null,
            "5",
            null);

        var json = JsonSerializer.Serialize(result, JsonOptions);
        var node = JsonNode.Parse(json)!;

        Assert.False(node.AsObject().ContainsKey("code"));
        Assert.False(node.AsObject().ContainsKey("message"));
        Assert.False(node.AsObject().ContainsKey("newVersion"));
    }

    [Fact]
    public void BulkItemResult_NotFoundMessage_IsNonDisclosing()
    {
        var productId = Guid.NewGuid();
        var result = BulkItemResult.NotFound(productId, 5u);

        Assert.DoesNotContain(productId.ToString(), result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BulkItemResult_ConcurrencyConflictMessage_IsNonDisclosing()
    {
        var productId = Guid.NewGuid();
        var result = BulkItemResult.ConcurrencyConflict(productId, 5u, 6u);

        Assert.DoesNotContain(productId.ToString(), result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BulkItemResult_VersionStrings_UseInvariantCulture()
    {
        var result = BulkItemResult.Unchanged(Guid.NewGuid(), 42u, 43u);

        Assert.Equal("42", result.PreviousVersion);
        Assert.Equal("43", result.NewVersion);
    }
}
