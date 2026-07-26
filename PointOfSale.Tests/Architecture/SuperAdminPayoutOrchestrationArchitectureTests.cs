using System.Reflection;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.API.SuperAdmin.Controllers;
using PointOfSale.API.SuperAdmin.Services;

namespace PointOfSale.Tests.Architecture;

public sealed class SuperAdminPayoutOrchestrationArchitectureTests
{
    [Fact]
    public void Controller_MustDependOnSuperAdminPayoutOrchestrationForExecution()
    {
        var constructorParameterTypes = typeof(SuperAdminPayoutsController)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToList();

        Assert.Contains(typeof(ISuperAdminPayoutOrchestrationService), constructorParameterTypes);
        Assert.DoesNotContain(typeof(ICommerceHubPayoutService), constructorParameterTypes);
        Assert.DoesNotContain(typeof(ICommerceHubFinancialLedgerService), constructorParameterTypes);

        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminPayoutsController.cs");
        Assert.Contains("_payoutOrchestrationService.ExecuteAsync", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ICommerceHubPayoutService", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ICommerceHubFinancialLedgerService", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordPayoutAsync", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Orchestration_MustDeclareCanonicalPayoutBoundaryAndNotLedgerBoundary()
    {
        var constructorParameterTypes = typeof(SuperAdminPayoutOrchestrationService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToList();

        Assert.Contains(typeof(ICommerceHubPayoutService), constructorParameterTypes);
        Assert.DoesNotContain(typeof(ICommerceHubFinancialLedgerService), constructorParameterTypes);
        Assert.DoesNotContain(typeof(ISuperAdminPayoutsService), constructorParameterTypes);

        var orchestration = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutOrchestrationService.cs");
        Assert.Contains("ICommerceHubPayoutService", orchestration, StringComparison.Ordinal);
        Assert.Contains("CreatePayoutAsync", orchestration, StringComparison.Ordinal);
        Assert.Contains("MarkProcessingAsync", orchestration, StringComparison.Ordinal);
        Assert.Contains("MarkCompletedAsync", orchestration, StringComparison.Ordinal);
        Assert.Contains("PayoutExecutionStarted", orchestration, StringComparison.Ordinal);
        Assert.Contains("PayoutExecutionSucceeded", orchestration, StringComparison.Ordinal);
        Assert.Contains("PayoutExecutionFailed", orchestration, StringComparison.Ordinal);
        Assert.DoesNotContain("ISuperAdminPayoutsService", orchestration, StringComparison.Ordinal);
        Assert.DoesNotContain("ICommerceHubFinancialLedgerService", orchestration, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordPayoutAsync", orchestration, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubFinancialTransactions", orchestration, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminExecution_MustUseCanonicalPayoutLifecycleInOrder()
    {
        var orchestration = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutOrchestrationService.cs");
        var createIndex = orchestration.IndexOf("CreatePayoutAsync", StringComparison.Ordinal);
        var processingIndex = orchestration.IndexOf("MarkProcessingAsync", StringComparison.Ordinal);
        var completedIndex = orchestration.IndexOf("MarkCompletedAsync", StringComparison.Ordinal);

        Assert.True(createIndex >= 0, "Super Admin payout orchestration must create the canonical payout first.");
        Assert.True(processingIndex > createIndex, "Super Admin payout orchestration must mark Processing after creation.");
        Assert.True(completedIndex > processingIndex, "Super Admin payout orchestration must complete only after Processing.");
        Assert.Contains("EnsureCommittedExternalHandoff(processing)", orchestration, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminServices_MustNotContainLegacyDirectCompletedRuntime()
    {
        var orchestration = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutOrchestrationService.cs");
        var payoutsService = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs");
        var superAdminRuntime = orchestration + payoutsService;

        Assert.DoesNotContain("new CommerceHubPayout", superAdminRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("Status = CommerceHubPayoutStatus.Completed", superAdminRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordPayoutAsync", superAdminRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("_payoutsService.ExecuteAsync", orchestration, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyInjection_MustRegisterOrchestrationAsScopedFinancialBoundary()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains(
            "AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminPayoutOrchestrationService, PointOfSale.API.SuperAdmin.Services.SuperAdminPayoutOrchestrationService>",
            program,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddSingleton<PointOfSale.API.SuperAdmin.Services.ISuperAdminPayoutOrchestrationService",
            program,
            StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
