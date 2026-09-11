using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Attributes;
using PointOfSale.API.Controllers;
using PointOfSale.API.SuperAdmin.Models;

namespace PointOfSale.Tests.Architecture;

public sealed class Gate3RDevelopmentOnlineSuccessCertificationTests
{
    [Fact]
    public void DevelopmentSuccessEndpoint_MustRemainDevelopmentOnlyHiddenAndSuperAdminProtected()
    {
        var attributes = typeof(DevelopmentCommerceHubController).GetCustomAttributes(true);

        Assert.Contains(attributes, attribute => attribute is DevelopmentOnlyControllerAttribute);
        Assert.Contains(attributes, attribute => attribute is ApiExplorerSettingsAttribute api && api.IgnoreApi);
        Assert.DoesNotContain(attributes, attribute => attribute is AllowAnonymousAttribute);
        Assert.Contains(attributes, attribute =>
            attribute is AuthorizeAttribute authorization &&
            authorization.Policy == SuperAdminSecurityConstants.PolicyName);
    }

    [Fact]
    public void DevelopmentSuccessMechanism_MustEnterCanonicalCollectionWithoutForgingCallbackEvidence()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/DevelopmentCommerceHubController.cs");
        var paymentService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var payoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var events = ReadRepoFile("PointOfSale.Core/Events/CommerceHubPaymentCompletionEvents.cs");

        Assert.Contains("payments/{paymentId:guid}/simulate-success", controller, StringComparison.Ordinal);
        Assert.Contains("AdministratorConfirmation", controller, StringComparison.Ordinal);
        Assert.Contains("DEVELOPMENT_ONLINE_PAYMENT_SUCCESS_SIMULATED", controller, StringComparison.Ordinal);
        Assert.Contains("ExternalMoneyMovementConfirmed = false", controller, StringComparison.Ordinal);

        Assert.Contains("ExecuteDevelopmentSuccessAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("providerAmount: payment.Amount", paymentService, StringComparison.Ordinal);
        Assert.Contains("providerCurrency: payment.Currency", paymentService, StringComparison.Ordinal);
        Assert.Contains("ApplyWebhookOutcomeAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("isDevelopmentSimulation: true", paymentService, StringComparison.Ordinal);
        Assert.Contains("DEV-SIM:ONLINE:", paymentService, StringComparison.Ordinal);
        Assert.Contains("DEVELOPMENT_PAYMENT_SIMULATION_STATE_INVALID", paymentService, StringComparison.Ordinal);
        Assert.Contains("DEVELOPMENT_PAYMENT_SIMULATION_RESERVATION_INVALID", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessWebhookAsync(",
            Slice(paymentService, "public async Task<CommerceHubPaymentWebhookResultDto> ExecuteDevelopmentSuccessAsync", "private bool IsProviderGloballyConfigured"),
            StringComparison.Ordinal);

        Assert.Contains("IsDevelopmentSimulation", events, StringComparison.Ordinal);
        Assert.Contains("ExternalMoneyMovementConfirmed", events, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_DEVELOPMENT_SIMULATION_NOT_TRANSFERABLE", payoutService, StringComparison.Ordinal);
        Assert.Contains("DEV-SIM:ONLINE:", payoutService, StringComparison.Ordinal);

        var compensationWorker = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentCompensationOutboxWorker.cs");
        Assert.Contains("IsDevelopmentSimulationCompensation", compensationWorker, StringComparison.Ordinal);
        Assert.Contains("CloseDevelopmentSimulationCompensationAsync", compensationWorker, StringComparison.Ordinal);
        Assert.Contains("compensation closed without a gateway refund", compensationWorker, StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentSuccessFrontend_MustBeDevGatedAndRejectUnmarkedResponses()
    {
        var page = ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/PaymentProvidersPage.tsx");
        var service = ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/paymentProvidersService.ts");
        var panel = ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/DevelopmentOnlinePaymentSimulationPanel.tsx");

        Assert.Contains("import.meta.env.DEV", page, StringComparison.Ordinal);
        Assert.Contains("if (!import.meta.env.DEV)", service, StringComparison.Ordinal);
        Assert.Contains("/api/dev/commercehub/payments/", service, StringComparison.Ordinal);
        Assert.Contains("result.isDevelopmentSimulation !== true", service, StringComparison.Ordinal);
        Assert.Contains("result.externalMoneyMovementConfirmed !== false", service, StringComparison.Ordinal);
        Assert.Contains("no external Paymob charge or money movement", panel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("administratorConfirmation: true", panel, StringComparison.Ordinal);
    }

    private static string Slice(string value, string start, string end)
    {
        var startIndex = value.IndexOf(start, StringComparison.Ordinal);
        var endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return value[startIndex..endIndex];
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
