namespace PointOfSale.Tests.Checkout;

public sealed class DynamicCheckoutObservabilityTests
{
    [Fact]
    public void CheckoutService_Logs_Dynamic_Field_Loads_And_Rejections_With_Safe_Structure()
    {
        var source = ReadRepositoryFile("PointOfSale.API", "Services", "Checkout", "CheckoutService.cs");

        Assert.Contains("Checkout dynamic field configuration loaded", source, StringComparison.Ordinal);
        Assert.Contains("Checkout dynamic field rejected", source, StringComparison.Ordinal);
        Assert.Contains("ReasonCategory", source, StringComparison.Ordinal);
        Assert.Contains("FieldId", source, StringComparison.Ordinal);
        Assert.Contains("FieldType", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueJson: {", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmittedValue: {", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutAttachment_Observability_Classifies_Lifecycle_And_Does_Not_Log_Object_Keys()
    {
        var service = ReadRepositoryFile("PointOfSale.API", "Services", "Checkout", "CheckoutAttachmentService.cs");
        var cleanup = ReadRepositoryFile("PointOfSale.API", "Jobs", "CheckoutAttachmentCleanupJob.cs");
        var admin = ReadRepositoryFile("PointOfSale.API", "Services", "Admin", "AdminOrderService.cs");
        var combined = service + cleanup + admin;

        Assert.Contains("Checkout attachment lease created", combined, StringComparison.Ordinal);
        Assert.Contains("Checkout attachment finalized", combined, StringComparison.Ordinal);
        Assert.Contains("Checkout attachment attach rejected", combined, StringComparison.Ordinal);
        Assert.Contains("Checkout attachment cleanup completed", combined, StringComparison.Ordinal);
        Assert.Contains("OpenCheckoutAttachmentDownload", combined, StringComparison.Ordinal);
        Assert.Contains("ErrorCategory: storage", combined, StringComparison.Ordinal);
        Assert.Contains("ErrorCategory: authorization", combined, StringComparison.Ordinal);
        Assert.Contains("ErrorCategory: invalid_state", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ObjectKey: {ObjectKey}", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("FinalObjectKey: {FinalObjectKey}", combined, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(pathParts).ToArray()));
    }
}
