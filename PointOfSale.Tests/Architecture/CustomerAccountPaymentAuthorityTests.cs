using System.Reflection;
using System.Text.Json;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.Architecture;

public sealed class CustomerAccountPaymentAuthorityTests
{
    [Fact]
    public void PosCustomerPayments_MustUseDedicatedIdempotentLedgerBoundary()
    {
        var customers = ReadRepoFile("PointOfSale.API/Controllers/CustomersController.cs");
        var ledger = ReadRepoFile("PointOfSale.API/Services/LedgerService.cs");

        Assert.Contains("[HttpPost(\"{id}/payments\")]", customers, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER_PAYMENT_CREATE", customers, StringComparison.Ordinal);
        Assert.Contains("ResolveFinancialIdempotencyKey", customers, StringComparison.Ordinal);
        Assert.Contains("ApplyCustomerTransaction", customers, StringComparison.Ordinal);
        Assert.Contains("TransactionType.PaymentReceived", customers, StringComparison.Ordinal);
        Assert.Contains("creditAmount: payment.Amount", customers, StringComparison.Ordinal);
        Assert.Contains("EnqueueCustomerPaymentAsync", customers, StringComparison.Ordinal);
        Assert.Contains("new JsonStringEnumConverter()", customers, StringComparison.Ordinal);
        Assert.Contains("transaction.ReferenceId == payment.Id", customers, StringComparison.Ordinal);
        Assert.Contains("LockPartyForUpdateAsync", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerPaymentPersistence_MustEnforceTenantIdempotencyAndExactAllocation()
    {
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260911135713_POS_CustomerAccountPayments.cs");

        Assert.Contains("IX_CustomerPayments_TenantId_IdempotencyKey", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CustomerPayments_Amount_Positive", migration, StringComparison.Ordinal);
        Assert.Contains("CK_CustomerPayments_Allocation_Consistent", migration, StringComparison.Ordinal);
        Assert.Contains("CashAmount", migration, StringComparison.Ordinal);
        Assert.Contains("CardAmount", migration, StringComparison.Ordinal);
        Assert.Contains("BankTransferAmount", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void PosPaymentUi_MustNotCallStorefrontPaymentIntentController()
    {
        var api = ReadRepoFile("PointOfSale.Web/src/services/soaApi.js");
        var storefrontPayments = ReadRepoFile("PointOfSale.API/Controllers/PaymentsController.cs");

        Assert.Contains("`/Customers/${data.customerId}/payments`", api, StringComparison.Ordinal);
        Assert.Contains("`/Suppliers/${data.supplierId}/payments`", api, StringComparison.Ordinal);
        Assert.Contains("'Idempotency-Key'", api, StringComparison.Ordinal);
        Assert.DoesNotContain("/Payments/receive", api, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[Authorize(AuthenticationSchemes = \"StorefrontJwt,CustomerJwt\")]", storefrontPayments, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpPost(\"receive\")", storefrontPayments, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerPaymentReplay_MustPreserveStringEnumContract()
    {
        var optionsField = typeof(CustomersController).GetField(
            "FinancialMutationResponseJsonOptions",
            BindingFlags.NonPublic | BindingFlags.Static);
        var options = Assert.IsType<JsonSerializerOptions>(optionsField?.GetValue(null));
        var json = JsonSerializer.Serialize(new CustomerPaymentResponse
        {
            PaymentMethod = PaymentMethod.Cash,
        }, options);

        Assert.Contains("\"paymentMethod\":\"Cash\"", json, StringComparison.Ordinal);

        var hashOptionsField = typeof(CustomersController).GetField(
            "FinancialMutationJsonOptions",
            BindingFlags.NonPublic | BindingFlags.Static);
        var hashOptions = Assert.IsType<JsonSerializerOptions>(hashOptionsField?.GetValue(null));
        var hashJson = JsonSerializer.Serialize(new { PaymentMethod = PaymentMethod.Cash }, hashOptions);
        Assert.Contains("\"paymentMethod\":0", hashJson, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            relativePath));

        return File.ReadAllText(path);
    }
}
