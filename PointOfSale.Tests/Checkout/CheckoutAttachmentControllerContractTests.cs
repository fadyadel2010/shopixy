namespace PointOfSale.Tests.Checkout;

public sealed class CheckoutAttachmentControllerContractTests
{
    [Fact]
    public void RemoveAttachment_UnexpectedFailuresUseServiceUnavailableContract()
    {
        var source = File.ReadAllText(RepositoryPath(
            "PointOfSale.API",
            "Controllers",
            "CheckoutController.cs"));
        var methodStart = source.IndexOf(
            "public async Task<IActionResult> RemoveAttachment(",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf("#region Header Validation", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        Assert.Contains("catch (Exception ex) when (!cancellationToken.IsCancellationRequested)", method, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status503ServiceUnavailable", method, StringComparison.Ordinal);
        Assert.Contains("ApiErrorResponse.ServiceUnavailable(traceId)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutAttachmentDevelopmentUpload_StaysDevelopmentOnlyAndR2RegistrationRemainsAuthoritative()
    {
        var controller = File.ReadAllText(RepositoryPath(
            "PointOfSale.API",
            "Controllers",
            "CheckoutController.cs"));
        var program = File.ReadAllText(RepositoryPath(
            "PointOfSale.API",
            "Program.cs"));
        var developmentStorage = File.ReadAllText(RepositoryPath(
            "PointOfSale.API",
            "Services",
            "DevelopmentFileStorageService.cs"));

        Assert.Contains("[HttpPut(\"attachments/development-upload\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[LeaseTokenAuthorizedUpload]", controller, StringComparison.Ordinal);
        Assert.Contains("environment.IsDevelopment()", controller, StringComparison.Ordinal);
        Assert.Contains("storage is not PointOfSale.API.Services.IDevelopmentPresignedUploadStorage", controller, StringComparison.Ordinal);
        Assert.Contains("builder.Environment.IsDevelopment()", program, StringComparison.Ordinal);
        Assert.Contains("useDevelopmentFileStorage && !r2Options.IsConfigured", program, StringComparison.Ordinal);
        Assert.Contains("CreateInstance<PointOfSale.API.Services.R2StorageService>", program, StringComparison.Ordinal);
        Assert.Contains("IDevelopmentPresignedUploadStorage", developmentStorage, StringComparison.Ordinal);
        Assert.DoesNotContain("wwwroot", developmentStorage, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
    }
}
