namespace PointOfSale.Tests.Checkout;

public sealed class CheckoutAttachmentDownloadSecurityArchitectureTests
{
    [Fact]
    public void AdminDownloadEndpoint_IsUnderAdminOrdersAuthorityAndRejectsManagerRole()
    {
        var source = ReadRepositoryFile("PointOfSale.API", "Controllers", "AdminOrdersController.cs");

        Assert.Contains("checkout-attachments/{attachmentId:guid}/download", source, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", source, StringComparison.Ordinal);
        Assert.Contains("OpenCheckoutAttachmentDownloadAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminDownloadService_VerifiesOrderOwnershipBeforeResolvingAttachment()
    {
        var source = ReadRepositoryFile("PointOfSale.API", "Services", "Admin", "AdminOrderService.cs");

        var ownershipIndex = source.IndexOf("VerifyOrderOwnershipAsync(tenantId, orderId", StringComparison.Ordinal);
        var attachmentQueryIndex = source.IndexOf("_context.CheckoutAttachments", StringComparison.Ordinal);

        Assert.True(ownershipIndex >= 0, "Download flow must call canonical admin order ownership verification.");
        Assert.True(attachmentQueryIndex > ownershipIndex, "Attachment lookup must happen only after order ownership is verified.");
        Assert.Contains("item.Id == attachmentId", source, StringComparison.Ordinal);
        Assert.Contains("item.OrderId == orderId", source, StringComparison.Ordinal);
        Assert.Contains("item.TenantId == tenantId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminDownloadService_StreamsFinalObjectWithoutExposingStorageKey()
    {
        var source = ReadRepositoryFile("PointOfSale.API", "Services", "Admin", "AdminOrderService.cs");

        Assert.Contains("attachment.Status != CheckoutAttachmentStatus.Attached", source, StringComparison.Ordinal);
        Assert.Contains("download.ContentLength != attachment.SizeBytes", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeAttachmentContentType(download.ContentType)", source, StringComparison.Ordinal);
        Assert.Contains("attachment.FinalObjectKey", source, StringComparison.Ordinal);
        Assert.Contains("DownloadObjectAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateUploadUrlAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminOrderPresentation_UsesAuthorizedDownloadApiInsteadOfStorageUrls()
    {
        var apiSource = ReadRepositoryFile("PointOfSale.Web", "src", "services", "adminOrdersApi.js");
        var pageSource = ReadRepositoryFile("PointOfSale.Web", "src", "pages", "Orders.jsx");
        var previewSource = ReadRepositoryFile("PointOfSale.Web", "src", "pages", "OrderCheckoutAttachmentPreview.jsx");
        var controllerSource = ReadRepositoryFile("PointOfSale.API", "Controllers", "AdminOrdersController.cs");

        Assert.Contains("checkout-attachments/${attachmentId}/download", apiSource, StringComparison.Ordinal);
        Assert.Contains("responseType: 'blob'", apiSource, StringComparison.Ordinal);
        Assert.Contains("OrderCheckoutAttachmentPreview", pageSource, StringComparison.Ordinal);
        Assert.Contains("downloadAdminOrderCheckoutAttachment", previewSource, StringComparison.Ordinal);
        Assert.Contains("getAdminOrderCheckoutAttachmentBlob", previewSource, StringComparison.Ordinal);
        Assert.Contains("X-Content-Type-Options", controllerSource, StringComparison.Ordinal);
        Assert.Contains("private, no-store", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("objectKey", pageSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("objectKey", previewSource, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var solutionRoot = Infrastructure.TestConfiguration.GetSolutionRoot();
        var path = Path.Combine(new[] { solutionRoot }.Concat(relativeParts).ToArray());
        return File.ReadAllText(path);
    }
}
