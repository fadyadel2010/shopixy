namespace PointOfSale.Tests.Architecture;

public sealed class PurchaseReturnReliabilityHardeningTests
{
    [Fact]
    public void PurchaseReturnsController_MustLockHistoricalInvoice_AndAdvanceMutationVersion()
    {
        var controllerPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers",
            "PurchaseReturnsController.cs");

        var source = File.ReadAllText(Path.GetFullPath(controllerPath));

        Assert.Contains("LoadOriginalPurchaseInvoiceForReturnMutationAsync", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", source, StringComparison.Ordinal);
        Assert.Contains("originalInvoice.AccountingMutationVersion++", source, StringComparison.Ordinal);
        Assert.Contains("purchaseReturnMutationVersion = originalInvoice.AccountingMutationVersion", source, StringComparison.Ordinal);
        Assert.Contains("ResolveOriginalInvoiceBaseQuantity", source, StringComparison.Ordinal);
        Assert.Contains("Cannot return more than received quantity", source, StringComparison.Ordinal);
        Assert.Contains("EnqueuePurchaseReturnAsync(", source, StringComparison.Ordinal);
        Assert.Contains("originalInvoice.Id", source, StringComparison.Ordinal);
        Assert.Contains("OriginalPurchaseInvoiceId = originalInvoice.Id", source, StringComparison.Ordinal);
        Assert.Contains("purchaseReturnMutationVersion", source, StringComparison.Ordinal);
        Assert.Contains("candidate.AggregateVersion == mutationVersion", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PurchaseReturnAccountingProcessor_MustRejectStaleMutationVersions()
    {
        var processorPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Accounting",
            "AccountingOutboxProcessor.cs");

        var source = File.ReadAllText(Path.GetFullPath(processorPath));
        var methodSource = ExtractMethodSource(source, "private async Task<PostingResult> ProcessPurchaseReturnAsync(");

        Assert.Contains("OriginalInvoiceId", methodSource, StringComparison.Ordinal);
        Assert.Contains("MutationVersion", methodSource, StringComparison.Ordinal);
        Assert.Contains("LoadPurchaseInvoiceForAccountingPostAsync", methodSource, StringComparison.Ordinal);
        Assert.Contains("Skipping stale purchase return accounting message", methodSource, StringComparison.Ordinal);
        Assert.Contains("Skipping legacy unversioned purchase return accounting message", methodSource, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync(ct);", methodSource, StringComparison.Ordinal);
    }

    private static string ExtractMethodSource(string source, string signature)
    {
        var startIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not locate method signature '{signature}'.");

        var braceIndex = source.IndexOf('{', startIndex);
        if (braceIndex < 0)
        {
            return source[startIndex..];
        }

        var depth = 1;
        var index = braceIndex + 1;
        while (index < source.Length && depth > 0)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
            }

            index++;
        }

        return source[startIndex..index];
    }
}
