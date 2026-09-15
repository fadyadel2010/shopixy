namespace PointOfSale.Tests.Architecture;

public sealed class TreasuryInitialCapitalizationAuthorityCertificationTests
{
    [Fact]
    public void HttpBoundary_MustRemainOwnerOnlyRateLimitedAndTenantDerived()
    {
        var controller = ReadRepoFile(
            "PointOfSale.API",
            "Controllers",
            "TreasuryInitialCapitalizationsController.cs");

        Assert.Contains("[Authorize(Roles = \"Owner\")]", controller);
        Assert.Equal(2, Count(controller, "[EnableRateLimiting(\"FinancialWrite\")]"));
        Assert.Contains("_tenantContext.RequiredTenantId", controller);
        Assert.DoesNotContain("Guid TenantId", controller);
        Assert.Contains("AUTHORIZATION_REQUIRED", controller);
    }

    [Fact]
    public void PostingBoundary_MustReloadExactCurrentSqlSourceAndFixedOpeningEquity()
    {
        var posting = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Accounting",
            "FinancialPostingService.cs");

        Assert.Contains("TreasuryInitialCapitalization.AccountingSourceType", posting);
        Assert.Contains("ValidateInitialCapitalizationAuthorityAsync", posting);
        Assert.Contains("item.Id == request.SourceId", posting);
        Assert.Contains("item.TenantId == request.TenantId", posting);
        Assert.Contains("request.UserId != document.CreatedByUserId", posting);
        Assert.Contains("document.OpeningEquityAccountCodeSnapshot != AccountCodes.OpeningBalanceEquity", posting);
        Assert.Contains("line.Debit != allocation.Amount || line.Credit != 0m", posting);
        Assert.Contains("previouslyPostedIncrease + netCashIncrease", posting);
    }

    [Fact]
    public void Lifecycle_MustRemainAtomicIdempotentAndReversalOnly()
    {
        var service = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Accounting",
            "TreasuryInitialCapitalizationService.cs");
        var dbContext = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Data",
            "ApplicationDbContext.cs");

        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", service);
        Assert.Contains("EnsureOwnerAsync", service);
        Assert.Contains("LockTenantAsync", service);
        Assert.Contains("ResolveCreateReplayAsync", service);
        Assert.Contains("ReverseJournalAsync", service);
        Assert.Contains("outbox.MarkProcessed", service);
        Assert.Contains("Treasury initial capitalization allocations are immutable", dbContext);
        Assert.Contains("Treasury initial capitalization documents cannot be deleted", dbContext);
    }

    [Fact]
    public void SchemaAndLegacyBoundaries_MustKeepOneTenantAuthorityAndRetiredInflows()
    {
        var documentConfiguration = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Data",
            "Configurations",
            "TreasuryInitialCapitalizationConfiguration.cs");
        var allocationConfiguration = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Data",
            "Configurations",
            "TreasuryInitialCapitalizationAllocationConfiguration.cs");
        var cashboxController = ReadRepoFile("PointOfSale.API", "Controllers", "CashboxController.cs");
        var bankController = ReadRepoFile("PointOfSale.API", "Controllers", "BanksController.cs");
        var treasuryController = ReadRepoFile("PointOfSale.API", "Controllers", "TreasuryController.cs");

        Assert.Contains("IX_TreasuryInitialCapitalization_OneActivePerTenant", documentConfiguration);
        Assert.Contains("IX_TreasuryInitialCapitalization_Tenant_Operation", documentConfiguration);
        Assert.Contains("CK_TreasuryInitialCapitalization_OpeningEquity", documentConfiguration);
        Assert.Contains("'3010'", documentConfiguration);
        Assert.Contains("HasPrincipalKey(account => new { account.TenantId, account.Id })", allocationConfiguration);
        Assert.Contains("CK_TreasuryInitialCapitalizationAllocation_Amount", allocationConfiguration);
        Assert.Contains("OPENING_BALANCE_AUTHORITY_REQUIRED", cashboxController);
        Assert.Contains("StatusCodes.Status410Gone", bankController);
        Assert.Contains("TREASURY_INFLOW_WORKFLOW_REQUIRED", treasuryController);
    }

    private static int Count(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static string ReadRepoFile(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. relativePath]));
    }
}
