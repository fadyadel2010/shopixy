using System.Globalization;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkItemResultTests
{
    [Fact]
    public void Processed_WithMutationVersion_SetsPreviousAndNewVersion()
    {
        var productId = Guid.NewGuid();
        var result = BulkItemResult.Processed(productId, 5, 7u);

        Assert.Equal(productId, result.ProductId);
        Assert.Equal(BulkItemStatus.Processed, result.Status);
        Assert.Null(result.Code);
        Assert.Equal("5", result.PreviousVersion);
        Assert.Equal("7", result.NewVersion);
    }

    [Fact]
    public void Processed_WithoutMutationVersion_SetsPreviousVersionOnly()
    {
        var productId = Guid.NewGuid();
        var result = BulkItemResult.Processed(productId, 3);

        Assert.Equal(BulkItemStatus.Processed, result.Status);
        Assert.Equal("3", result.PreviousVersion);
        Assert.Null(result.NewVersion);
    }

    [Fact]
    public void Unchanged_SetsBothVersionsToCurrentRowVersion()
    {
        var productId = Guid.NewGuid();
        var result = BulkItemResult.Unchanged(productId, 4, 6);

        Assert.Equal(BulkItemStatus.Unchanged, result.Status);
        Assert.Equal("4", result.PreviousVersion);
        Assert.Equal("6", result.NewVersion);
    }

    [Fact]
    public void AlreadyActive_UsesUnchangedStatusAndCorrectCode()
    {
        var productId = Guid.NewGuid();
        var result = BulkItemResult.AlreadyActive(productId, 2);

        Assert.Equal(BulkItemStatus.Unchanged, result.Status);
        Assert.Equal(BulkErrorCodes.AlreadyActive, result.Code);
        Assert.Equal("Product is already active.", result.Message);
        Assert.Equal("2", result.PreviousVersion);
        Assert.Equal("2", result.NewVersion);
    }

    [Fact]
    public void AlreadyInactive_UsesUnchangedStatusAndCorrectCode()
    {
        var productId = Guid.NewGuid();
        var result = BulkItemResult.AlreadyInactive(productId, 8);

        Assert.Equal(BulkItemStatus.Unchanged, result.Status);
        Assert.Equal(BulkErrorCodes.AlreadyInactive, result.Code);
        Assert.Equal("Product is already inactive.", result.Message);
        Assert.Equal("8", result.PreviousVersion);
        Assert.Equal("8", result.NewVersion);
    }

    [Fact]
    public void NotFound_SetsFailedStatusAndCorrectCode()
    {
        var productId = Guid.NewGuid();
        var result = BulkItemResult.NotFound(productId, 1);

        Assert.Equal(BulkItemStatus.Failed, result.Status);
        Assert.Equal(BulkErrorCodes.NotFound, result.Code);
        Assert.Equal("Product was not found.", result.Message);
        Assert.Equal("1", result.PreviousVersion);
        Assert.Null(result.NewVersion);
    }

    [Fact]
    public void ConcurrencyConflict_SetsFailedStatusAndCorrectCode()
    {
        var productId = Guid.NewGuid();
        var result = BulkItemResult.ConcurrencyConflict(productId, 3, 5);

        Assert.Equal(BulkItemStatus.Failed, result.Status);
        Assert.Equal(BulkErrorCodes.ConcurrencyConflict, result.Code);
        Assert.Equal("Product has been modified by another operation.", result.Message);
        Assert.Equal("3", result.PreviousVersion);
        Assert.Equal("5", result.NewVersion);
    }

    [Fact]
    public void ValidationError_SetsFailedStatusAndCorrectCode()
    {
        var productId = Guid.NewGuid();
        var result = BulkItemResult.ValidationError(productId, 7, "Invalid category");

        Assert.Equal(BulkItemStatus.Failed, result.Status);
        Assert.Equal(BulkErrorCodes.ValidationFailed, result.Code);
        Assert.Equal("Invalid category", result.Message);
        Assert.Equal("7", result.PreviousVersion);
        Assert.Null(result.NewVersion);
    }

    [Fact]
    public void Forbidden_SetsFailedStatusAndCorrectCode()
    {
        var productId = Guid.NewGuid();
        var result = BulkItemResult.Forbidden(productId, 9);

        Assert.Equal(BulkItemStatus.Failed, result.Status);
        Assert.Equal(BulkErrorCodes.Forbidden, result.Code);
        Assert.Equal("Action is forbidden for this product.", result.Message);
        Assert.Equal("9", result.PreviousVersion);
        Assert.Null(result.NewVersion);
    }

    [Fact]
    public void DependencyBlocked_SetsFailedStatusAndCorrectCode()
    {
        var productId = Guid.NewGuid();
        var result = BulkItemResult.DependencyBlocked(productId, 11);

        Assert.Equal(BulkItemStatus.Failed, result.Status);
        Assert.Equal(BulkErrorCodes.DependencyBlocked, result.Code);
        Assert.Equal("A dependency is currently blocked.", result.Message);
        Assert.Equal("11", result.PreviousVersion);
        Assert.Null(result.NewVersion);
    }
}
