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
