namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPayoutIdentityCertificationTests
{
    [Fact]
    public void MerchantPayoutMethods_MustBeOwnedByTrustedTenantContextAndVendorScope()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/VendorPayoutMethodsController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutMethodsService.cs");
        var entity = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubVendorPayoutMethod.cs");
        var dto = ReadRepoFile("PointOfSale.API/DTOs/CommerceHub/CommerceHubVendorPayoutDtos.cs");

        Assert.Contains("ITenantContext", controller, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", controller, StringComparison.Ordinal);
        Assert.Contains("RequiredTenantId", controller, StringComparison.Ordinal);
        Assert.Contains("Store tenant context is required.", controller, StringComparison.Ordinal);
        Assert.Contains("_payoutMethodsService.GetAsync(TenantId", controller, StringComparison.Ordinal);
        Assert.Contains("var tenantId = TenantId;", controller, StringComparison.Ordinal);
        Assert.Contains("_payoutMethodsService.CreateAsync", controller, StringComparison.Ordinal);
        Assert.Contains("ResolveActorId()", controller, StringComparison.Ordinal);

        Assert.Contains("ValidateVendorId(vendorId)", service, StringComparison.Ordinal);
        Assert.Contains("tenant.Id == vendorId", service, StringComparison.Ordinal);
        Assert.Contains("method.VendorId == vendorId", service, StringComparison.Ordinal);
        Assert.Contains("existingVendorMethods", service, StringComparison.Ordinal);
        Assert.Contains("CreatedBy = actorId", service, StringComparison.Ordinal);
        Assert.Contains("MethodType = request.MethodType", service, StringComparison.Ordinal);
        Assert.Contains("DestinationValue = destinationValue", service, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_METHOD_TYPE_INVALID", service, StringComparison.Ordinal);
        Assert.Contains("DESTINATION_VALUE_REQUIRED", service, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateAsync(", service, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteAsync(", service, StringComparison.Ordinal);

        Assert.Contains("public Guid VendorId", entity, StringComparison.Ordinal);
        Assert.Contains("DestinationValue", entity, StringComparison.Ordinal);
        Assert.Contains("IsDefault", entity, StringComparison.Ordinal);
        Assert.Contains("IsVerified", entity, StringComparison.Ordinal);
        Assert.Contains("VendorId", dto, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminPayoutIdentityVisibility_MustRemainReadOnlyPlatformExecutionInput()
    {
        var readService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs");
        var superAdminService = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs");
        var superAdminDto = ReadRepoFile("PointOfSale.API/SuperAdmin/DTOs/SuperAdminPayoutDtos.cs");

        Assert.Contains("GetVendorQueueSnapshotsAsync", readService, StringComparison.Ordinal);
        Assert.Contains("DefaultPayoutMethodId", readService, StringComparison.Ordinal);
        Assert.Contains("DefaultPayoutDestinationValue", readService, StringComparison.Ordinal);
        Assert.Contains("NoDefaultPayoutMethod", readService, StringComparison.Ordinal);
        Assert.Contains("PayoutBlockedByAuthorityConflict", readService, StringComparison.Ordinal);

        Assert.Contains("var payoutMethods = await _dbContext.CommerceHubVendorPayoutMethods", superAdminService, StringComparison.Ordinal);
        Assert.Contains("vendorIds.Contains(method.VendorId)", superAdminService, StringComparison.Ordinal);
        Assert.Contains("method.Id == request.PayoutMethodId", superAdminService, StringComparison.Ordinal);
        Assert.Contains("method.VendorId == vendorId", superAdminService, StringComparison.Ordinal);
        Assert.Contains("DestinationValueSnapshot = payoutMethod.DestinationValue", superAdminService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_METHOD_NOT_FOUND", superAdminService, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubVendorPayoutMethods.Add", superAdminService, StringComparison.Ordinal);

        Assert.Contains("PayoutMethods", superAdminDto, StringComparison.Ordinal);
        Assert.Contains("DestinationValue", superAdminDto, StringComparison.Ordinal);
        Assert.Contains("SuperAdminExecuteVendorPayoutRequestDto", superAdminDto, StringComparison.Ordinal);
        Assert.Contains("PayoutMethodId", superAdminDto, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
