using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.API.Services.ProductAuthoring.Bulk;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkProductRequestValidatorTests
{
    private readonly BulkProductRequestValidator _validator = new();

    [Fact]
    public async Task NullRequest_IsRejected()
    {
        var result = await _validator.ValidateAsync(null!);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(BulkErrorCodes.BulkRequestRequired, result.Errors[0].Code);
        Assert.Empty(result.NormalizedItems);
    }

    [Fact]
    public async Task NullItems_IsRejected()
    {
        var request = new BulkProductRequest(null);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(BulkErrorCodes.ProductsRequired, result.Errors[0].Code);
    }

    [Fact]
    public async Task EmptyItems_IsRejected()
    {
        var request = new BulkProductRequest(Array.Empty<BulkProductRequestItem>());

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(BulkErrorCodes.ProductsRequired, result.Errors[0].Code);
    }

    [Fact]
    public async Task MoreThanFiftyItems_IsRejected()
    {
        var items = Enumerable.Range(0, 51)
            .Select(i => new BulkProductRequestItem(Guid.NewGuid().ToString("N"), "1"))
            .ToArray();
        var request = new BulkProductRequest(items);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(BulkErrorCodes.BatchTooLarge, result.Errors[0].Code);
    }

    [Fact]
    public async Task NullItem_IsRejected()
    {
        var items = new BulkProductRequestItem?[] { null };
        var request = new BulkProductRequest(items);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(BulkErrorCodes.ProductsRequired, result.Errors[0].Code);
        Assert.Equal("items[0]", result.Errors[0].Field);
    }

    [Fact]
    public async Task MalformedProductId_IsRejected()
    {
        var request = new BulkProductRequest(
            [new BulkProductRequestItem("not-a-guid", "1")]);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(BulkErrorCodes.InvalidProductId, result.Errors[0].Code);
    }

    [Fact]
    public async Task EmptyProductId_IsRejected()
    {
        var request = new BulkProductRequest(
            [new BulkProductRequestItem("", "1")]);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(BulkErrorCodes.InvalidProductId, result.Errors[0].Code);
    }

    [Fact]
    public async Task GuidEmptyProductId_IsRejected()
    {
        var request = new BulkProductRequest(
            [new BulkProductRequestItem(Guid.Empty.ToString("N"), "1")]);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(BulkErrorCodes.InvalidProductId, result.Errors[0].Code);
    }

    [Fact]
    public async Task MissingVersion_IsRejected()
    {
        var request = new BulkProductRequest(
            [new BulkProductRequestItem(Guid.NewGuid().ToString("N"), null)]);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(BulkErrorCodes.InvalidExpectedVersion, result.Errors[0].Code);
    }

    [Fact]
    public async Task MalformedVersion_IsRejected()
    {
        var request = new BulkProductRequest(
            [new BulkProductRequestItem(Guid.NewGuid().ToString("N"), "abc")]);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(BulkErrorCodes.InvalidExpectedVersion, result.Errors[0].Code);
    }

    [Fact]
    public async Task NegativeVersion_IsRejected()
    {
        var request = new BulkProductRequest(
            [new BulkProductRequestItem(Guid.NewGuid().ToString("N"), "-1")]);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(BulkErrorCodes.InvalidExpectedVersion, result.Errors[0].Code);
    }

    [Fact]
    public async Task OutOfRangeVersion_IsRejected()
    {
        var request = new BulkProductRequest(
            [new BulkProductRequestItem(Guid.NewGuid().ToString("N"), ((long)uint.MaxValue + 1).ToString())]);

        var result = await _validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(BulkErrorCodes.InvalidExpectedVersion, result.Errors[0].Code);
    }

    [Fact]
    public async Task QuotedValidVersion_IsAccepted()
    {
        var productId = Guid.NewGuid();
        var request = new BulkProductRequest(
            [new BulkProductRequestItem(productId.ToString("N"), "\"42\"")]);

        var result = await _validator.ValidateAsync(request);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Single(result.NormalizedItems);
        Assert.Equal(productId, result.NormalizedItems[0].ProductId);
        Assert.Equal(42u, result.NormalizedItems[0].ExpectedVersion);
    }

    [Fact]
    public async Task DuplicateSameVersion_CollapsesToFirstOccurrenceInOrder()
    {
        var productId = Guid.NewGuid();
        var items = new[]
        {
            new BulkProductRequestItem(productId.ToString("N"), "1"),
            new BulkProductRequestItem(productId.ToString("N"), "1")
        };

        var result = await _validator.ValidateAsync(new BulkProductRequest(items));

        Assert.True(result.IsValid);
        Assert.Single(result.NormalizedItems);
        Assert.Equal(productId, result.NormalizedItems[0].ProductId);
        Assert.Equal(1u, result.NormalizedItems[0].ExpectedVersion);
    }

    [Fact]
    public async Task DuplicateConflictingVersions_IsRejected()
    {
        var productId = Guid.NewGuid();
        var items = new[]
        {
            new BulkProductRequestItem(productId.ToString("N"), "1"),
            new BulkProductRequestItem(productId.ToString("N"), "2")
        };

        var result = await _validator.ValidateAsync(new BulkProductRequest(items));

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(BulkErrorCodes.DuplicateProductConflict, result.Errors[0].Code);
    }

    [Fact]
    public async Task ValidRequest_NormalizesWithInvariantCulture()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var request = new BulkProductRequest(
            [
                new BulkProductRequestItem(id1.ToString("N"), "100"),
                new BulkProductRequestItem(id2.ToString(), "200")
            ]);

        var result = await _validator.ValidateAsync(request);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.NormalizedItems.Count);
        Assert.Equal(id1, result.NormalizedItems[0].ProductId);
        Assert.Equal(100u, result.NormalizedItems[0].ExpectedVersion);
        Assert.Equal(id2, result.NormalizedItems[1].ProductId);
        Assert.Equal(200u, result.NormalizedItems[1].ExpectedVersion);
    }
}
