namespace PointOfSale.Tests.Architecture;

public sealed class VerifiedBacklogRegressionTests
{
    [Fact]
    public void DevOnlyBaileysAllTenantsEndpoint_MustBeRuntimeDevelopmentGatedAndAudited()
    {
        var source = File.ReadAllText(RepoFile("PointOfSale.API", "Controllers", "TenantsController.cs"));
        var method = SliceMethod(source, "public async Task<ActionResult> EnableBaileysForAllTenants()");

        Assert.Contains("_environment.IsDevelopment()", method, StringComparison.Ordinal);
        Assert.Contains("return NotFound()", method, StringComparison.Ordinal);
        Assert.Contains("_auditService.LogAction", method, StringComparison.Ordinal);
        Assert.Contains("EnableBaileysAllTenants", method, StringComparison.Ordinal);
    }

    [Fact]
    public void StatementOfAccountAdjustmentUi_MustSendIdempotencyKey()
    {
        var source = File.ReadAllText(RepoFile("PointOfSale.Web", "src", "services", "soaApi.js"));
        var page = File.ReadAllText(RepoFile("PointOfSale.Web", "src", "pages", "Adjustments.jsx"));
        var method = source[source.IndexOf("createAdjustment:", StringComparison.Ordinal)..];

        Assert.Contains("'Idempotency-Key'", method, StringComparison.Ordinal);
        Assert.Contains("buildMutationKey('soa-adjustment'", method, StringComparison.Ordinal);
        Assert.Contains("stableStringify(payload)", page, StringComparison.Ordinal);
        Assert.Contains("lastSubmissionRef.current?.fingerprint === fingerprint", page, StringComparison.Ordinal);
        Assert.Contains("soaApi.createAdjustment(payload, { idempotencyKey })", page, StringComparison.Ordinal);
    }

    [Fact]
    public void CashboxClose_MustUseConditionalCloseAndAtomicAudit()
    {
        var source = File.ReadAllText(RepoFile("PointOfSale.API", "Controllers", "CashboxController.cs"));

        Assert.Contains("ExecuteUpdateAsync", source, StringComparison.Ordinal);
        Assert.Contains("c.IsActive", source, StringComparison.Ordinal);
        Assert.Contains("affectedRows == 0", source, StringComparison.Ordinal);
        Assert.Contains("AppendCashboxAuditLog", source, StringComparison.Ordinal);
        Assert.Contains("_context.AuditLogs.Add", source, StringComparison.Ordinal);

        var closeStart = source.IndexOf("public async Task<ActionResult> CloseCashbox", StringComparison.Ordinal);
        var closeEnd = source.IndexOf("/// <summary>", closeStart + 1, StringComparison.Ordinal);
        Assert.True(closeStart >= 0);
        Assert.True(closeEnd > closeStart);
        var closeMethod = source[closeStart..closeEnd];
        Assert.True(
            closeMethod.IndexOf("AppendCashboxAuditLog", StringComparison.Ordinal) <
            closeMethod.IndexOf("transaction.CommitAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void CustomerOpeningBalance_MustUseAccountingOutboxAndProcessor()
    {
        var customersController = File.ReadAllText(RepoFile("PointOfSale.API", "Controllers", "CustomersController.cs"));
        var soaController = File.ReadAllText(RepoFile("PointOfSale.API", "Controllers", "StatementOfAccountController.cs"));
        var outbox = File.ReadAllText(RepoFile("PointOfSale.Infrastructure", "Accounting", "AccountingOutboxService.cs"));
        var processor = File.ReadAllText(RepoFile("PointOfSale.Infrastructure", "Accounting", "AccountingOutboxProcessor.cs"));

        Assert.Contains("EnqueueCustomerOpeningBalanceAsync", customersController, StringComparison.Ordinal);
        Assert.Contains("EnqueueCustomerOpeningBalanceAsync", soaController, StringComparison.Ordinal);
        Assert.Contains("\"CustomerOpeningBalance\"", outbox, StringComparison.Ordinal);
        Assert.Contains("\"CustomerOpeningBalance\"", processor, StringComparison.Ordinal);
        Assert.Contains("PostCustomerOpeningBalanceAsync", processor, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerOpeningBalanceControllerQueries_MustTenantScopeFinancialAdmission()
    {
        var customersController = File.ReadAllText(RepoFile("PointOfSale.API", "Controllers", "CustomersController.cs"));
        var readMethod = SliceMethod(
            customersController,
            "public async Task<ActionResult<IEnumerable<CustomerOpeningBalanceResponse>>> GetCustomerOpeningBalances()");
        var saveMethod = SliceMethod(
            customersController,
            "public async Task<IActionResult> SaveCustomerOpeningBalances(");

        Assert.Contains("c.TenantId == _tenantContext.RequiredTenantId", readMethod, StringComparison.Ordinal);
        Assert.Contains("customer.TenantId == _tenantContext.RequiredTenantId", saveMethod, StringComparison.Ordinal);
        Assert.True(
            saveMethod.IndexOf("customer.TenantId == _tenantContext.RequiredTenantId", StringComparison.Ordinal) <
            saveMethod.IndexOf("ApplyCustomerTransaction", StringComparison.Ordinal),
            "Customer opening-balance admission must reject cross-tenant customers before ledger/outbox mutation.");
    }

    private static string SliceMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method signature: {signature}");
        var openBrace = source.IndexOf('{', start);
        Assert.True(openBrace > start, $"Could not find method body: {signature}");

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            depth += source[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0
            };

            if (depth == 0)
            {
                return source[start..(index + 1)];
            }
        }

        Assert.Fail($"Could not find method end: {signature}");
        return string.Empty;
    }

    private static string RepoFile(params string[] relativeSegments)
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            Path.Combine(relativeSegments)));
    }
}
