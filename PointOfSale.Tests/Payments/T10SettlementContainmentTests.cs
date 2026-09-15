using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;

namespace PointOfSale.Tests.Payments;

/// <summary>
/// T-10 containment tests: both payment settlement POST routes must fail closed
/// with HTTP 409 SETTLEMENT_EVIDENCE_REQUIRED because no authenticated provider
/// payout or carrier remittance evidence source exists. Neither route may call
/// the lifecycle settlement methods. Existing service mechanics stay dormant.
/// </summary>
public sealed class T10SettlementContainmentTests
{
    private const string ExpectedCode = "SETTLEMENT_EVIDENCE_REQUIRED";

    [Fact]
    public async Task RecordGatewaySettlement_WithPlausiblePayload_ReturnsEvidenceRequiredConflict()
    {
        var lifecycle = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        var controller = CreateController(lifecycle);

        var result = await controller.RecordGatewaySettlement(new GatewaySettlementApiRequest
        {
            SettlementId = "stripe-payout-001",
            Gateway = "stripe",
            PayoutBatchId = "po_123",
            GrossAmount = 100m,
            Fees = 3m,
            Currency = "EGP",
            BankReference = "bank-ref-001"
        });

        var payload = AssertConflictWithCode(result, ExpectedCode);
        Assert.Contains("provider payout evidence", payload.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

        lifecycle.Verify(
            service => service.RecordGatewaySettlementAsync(
                It.IsAny<GatewaySettlementRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        lifecycle.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecordCodSettlement_WithPlausiblePayload_ReturnsEvidenceRequiredConflict()
    {
        var lifecycle = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        var controller = CreateController(lifecycle);

        var result = await controller.RecordCodSettlement(new CodSettlementApiRequest
        {
            SettlementId = "cod-remittance-001",
            GrossAmount = 80m,
            Fees = 5m,
            Currency = "EGP",
            ShippingCompany = "Bosta",
            OrderIds = new List<Guid> { Guid.NewGuid() },
            BankReference = "bank-ref-002"
        });

        var payload = AssertConflictWithCode(result, ExpectedCode);
        Assert.Contains("carrier remittance evidence", payload.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

        lifecycle.Verify(
            service => service.RecordOfflineSettlementAsync(
                It.IsAny<OfflineSettlementRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        lifecycle.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecordGatewaySettlement_WithEmptyPayload_ReturnsSameContainmentResponse()
    {
        var lifecycle = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        var controller = CreateController(lifecycle);

        // Null body, default DTO, and invalid caller facts must all receive the
        // same containment response: rejection precedes caller-assertion validation.
        var nullBody = await controller.RecordGatewaySettlement(null);
        var defaultDto = await controller.RecordGatewaySettlement(new GatewaySettlementApiRequest());
        var invalidFacts = await controller.RecordGatewaySettlement(new GatewaySettlementApiRequest
        {
            SettlementId = string.Empty,
            Gateway = string.Empty,
            GrossAmount = -1m,
            Fees = -5m,
            Currency = string.Empty
        });

        foreach (var result in new[] { nullBody, defaultDto, invalidFacts })
        {
            AssertConflictWithCode(result, ExpectedCode);
        }

        lifecycle.Verify(
            service => service.RecordGatewaySettlementAsync(
                It.IsAny<GatewaySettlementRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        lifecycle.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecordCodSettlement_WithEmptyPayload_ReturnsSameContainmentResponse()
    {
        var lifecycle = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        var controller = CreateController(lifecycle);

        var nullBody = await controller.RecordCodSettlement(null);
        var defaultDto = await controller.RecordCodSettlement(new CodSettlementApiRequest());
        var invalidFacts = await controller.RecordCodSettlement(new CodSettlementApiRequest
        {
            SettlementId = string.Empty,
            GrossAmount = 0m,
            Fees = -1m,
            Currency = string.Empty,
            ShippingCompany = string.Empty
        });

        foreach (var result in new[] { nullBody, defaultDto, invalidFacts })
        {
            AssertConflictWithCode(result, ExpectedCode);
        }

        lifecycle.Verify(
            service => service.RecordOfflineSettlementAsync(
                It.IsAny<OfflineSettlementRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        lifecycle.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SettlementContainment_ReturnsNoSuccessOrJournalResult()
    {
        var lifecycle = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        var controller = CreateController(lifecycle);

        var gateway = await controller.RecordGatewaySettlement(new GatewaySettlementApiRequest
        {
            SettlementId = "stripe-payout-002",
            Gateway = "stripe",
            OrderIds = new List<Guid> { Guid.NewGuid() },
            GrossAmount = 50m,
            Fees = 1m,
            Currency = "EGP"
        });
        var cod = await controller.RecordCodSettlement(new CodSettlementApiRequest
        {
            SettlementId = "cod-remittance-002",
            GrossAmount = 50m,
            Fees = 1m,
            Currency = "EGP",
            ShippingCompany = "Aramex",
            OrderIds = new List<Guid> { Guid.NewGuid() }
        });

        foreach (var result in new[] { gateway, cod })
        {
            var conflict = Assert.IsType<ConflictObjectResult>(result);
            var serialized = JsonSerializer.Serialize(conflict.Value);
            Assert.DoesNotContain("journal", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"success\":true", serialized, StringComparison.OrdinalIgnoreCase);
        }

        lifecycle.VerifyNoOtherCalls();
    }

    [Fact]
    public void SettlementControllerSource_MustContainWithoutInvokingLifecycleSettlement()
    {
        var source = ReadRepoFile("PointOfSale.API/Controllers/PaymentLifecycleController.cs");

        // Routes, authorization, and containment contract stay in place.
        Assert.Contains("[HttpPost(\"settlements/gateway\")]", source, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"settlements/cod\")]", source, StringComparison.Ordinal);
        Assert.Contains("SETTLEMENT_EVIDENCE_REQUIRED", source, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", source, StringComparison.Ordinal);

        // Containment: the controller must never invoke the lifecycle settlement methods.
        Assert.DoesNotContain("_lifecycleService.RecordGatewaySettlementAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_lifecycleService.RecordOfflineSettlementAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void T04ContainmentSources_MustRemainUnchanged()
    {
        var adminAccounting = ReadRepoFile("PointOfSale.API/Controllers/AdminAccountingController.cs");
        var clearingAging = ReadRepoFile("PointOfSale.Web/src/pages/treasury/TreasuryClearingAging.jsx");

        Assert.Contains("SETTLEMENT_AUTHORITY_REQUIRED", adminAccounting, StringComparison.Ordinal);
        Assert.Contains("Status410Gone", adminAccounting, StringComparison.Ordinal);
        Assert.Contains("settlementRequiresEvidence", clearingAging, StringComparison.Ordinal);
    }

    private static PaymentLifecycleController CreateController(Mock<IPaymentLifecycleService> lifecycle)
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(context => context.TenantId).Returns(Guid.NewGuid());
        return new PaymentLifecycleController(lifecycle.Object, tenantContext.Object);
    }

    private static JsonElement AssertConflictWithCode(IActionResult result, string code)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflict.StatusCode);
        var payload = JsonSerializer.SerializeToElement(conflict.Value);
        Assert.Equal(code, payload.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("error").GetString()));
        return payload;
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
