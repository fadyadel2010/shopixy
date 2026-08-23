using System.Security.Claims;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.API.Services.ProductAuthoring.Bulk;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkAuthorizationServiceTests
{
    private readonly BulkAuthorizationService _auth = new();

    [Theory]
    [InlineData(BulkOperationType.Activate)]
    [InlineData(BulkOperationType.Deactivate)]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    public void ActivateDeactivatePublishUnpublish_RequireOwnerAdminManager(BulkOperationType operation)
    {
        var roles = _auth.RequiredRolesFor(operation);

        Assert.Contains("Owner", roles);
        Assert.Contains("Admin", roles);
        Assert.Contains("Manager", roles);
    }

    [Fact]
    public void Delete_RequiresOwnerAdminOnly()
    {
        var roles = _auth.RequiredRolesFor(BulkOperationType.Delete);

        Assert.Contains("Owner", roles);
        Assert.Contains("Admin", roles);
        Assert.DoesNotContain("Manager", roles);
    }

    [Theory]
    [InlineData(BulkOperationType.Activate)]
    [InlineData(BulkOperationType.Deactivate)]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    public void OwnerAuthorized_ForActivateDeactivatePublishUnpublish(BulkOperationType operation)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Owner")]));
        Assert.True(_auth.IsAuthorized(operation, user));
    }

    [Theory]
    [InlineData(BulkOperationType.Activate)]
    [InlineData(BulkOperationType.Deactivate)]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    public void ManagerAuthorized_ForActivateDeactivatePublishUnpublish(BulkOperationType operation)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Manager")]));
        Assert.True(_auth.IsAuthorized(operation, user));
    }

    [Theory]
    [InlineData(BulkOperationType.Activate)]
    [InlineData(BulkOperationType.Deactivate)]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    public void NonManagerRejected_ForActivateDeactivatePublishUnpublish(BulkOperationType operation)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Customer")]));
        Assert.False(_auth.IsAuthorized(operation, user));
    }

    [Fact]
    public void OwnerAuthorized_ForDelete()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Owner")]));
        Assert.True(_auth.IsAuthorized(BulkOperationType.Delete, user));
    }

    [Fact]
    public void AdminAuthorized_ForDelete()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")]));
        Assert.True(_auth.IsAuthorized(BulkOperationType.Delete, user));
    }

    [Fact]
    public void ManagerRejected_ForDelete()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Manager")]));
        Assert.False(_auth.IsAuthorized(BulkOperationType.Delete, user));
    }

    [Theory]
    [InlineData(BulkOperationType.Activate)]
    [InlineData(BulkOperationType.Deactivate)]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    public void IsAuthorized_AcceptsRoleEnumeration(BulkOperationType operation)
    {
        Assert.True(_auth.IsAuthorized(operation, new[] { "Manager" }));
        Assert.True(_auth.IsAuthorized(operation, new[] { "Admin" }));
        Assert.True(_auth.IsAuthorized(operation, new[] { "Owner" }));
        Assert.False(_auth.IsAuthorized(operation, new[] { "Customer" }));
        Assert.False(_auth.IsAuthorized(operation, Array.Empty<string>()));
    }

    [Fact]
    public void IsAuthorized_DeleteOnlyAcceptsOwnerAdmin()
    {
        Assert.True(_auth.IsAuthorized(BulkOperationType.Delete, new[] { "Owner" }));
        Assert.True(_auth.IsAuthorized(BulkOperationType.Delete, new[] { "Admin" }));
        Assert.False(_auth.IsAuthorized(BulkOperationType.Delete, new[] { "Manager" }));
        Assert.False(_auth.IsAuthorized(BulkOperationType.Delete, new[] { "Cashier" }));
        Assert.False(_auth.IsAuthorized(BulkOperationType.Delete, new ClaimsPrincipal()));
    }
}
