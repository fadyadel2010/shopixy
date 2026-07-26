using System.Collections.Generic;
using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class S17SettlementRecoveryHardeningTests
{
    [Fact]
    public void GatewaySettlement_MustRequireBatchOrExplicitIdentifiers_AndPersistAuditableMappings()
    {
        var contractSource = ReadRepoFile(
            "PointOfSale.Core",
            "Interfaces",
            "Payments",
            "IPaymentLifecycleService.cs");
        var lifecycleSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Payments",
            "PaymentLifecycleService.cs");
        var dbContextSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Data",
            "ApplicationDbContext.cs");

        Assert.Contains("PayoutBatchId", contractSource, StringComparison.Ordinal);
        Assert.Contains("OrderIds", contractSource, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentIds", contractSource, StringComparison.Ordinal);
        Assert.Contains("TenantId", contractSource, StringComparison.Ordinal);
        Assert.Contains("Currency", contractSource, StringComparison.Ordinal);
        Assert.Contains("PaymentSettlementBatches", dbContextSource, StringComparison.Ordinal);
        Assert.Contains("PaymentSettlementOrders", dbContextSource, StringComparison.Ordinal);
        Assert.Contains("ResolveGatewaySettlementOrdersAsync", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("resolved order count does not match requested identifiers", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("GrossAmount", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Collected", lifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "o.PaymentGateway == request.Gateway &&",
            lifecycleSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CodSettlement_MustRejectPartialOrInvalidOrderSets_BeforeMutation()
    {
        var lifecycleSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Payments",
            "PaymentLifecycleService.cs");
        var controllerSource = ReadRepoFile(
            "PointOfSale.API",
            "Controllers",
            "PaymentLifecycleController.cs");

        Assert.Contains("COD settlement rejected: gross amount mismatch", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("COD settlement rejected: collection accounting journal is not posted for all orders.", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("actualTotal = orders.Sum", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("RecordCodSettlementRejected", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("RecordCodSettlementRejected", lifecycleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Skipping order", lifecycleSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Settlement_MustRequirePostedCollectionAccounting_BeforeMarkingOrdersSettled()
    {
        var lifecycleSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Payments",
            "PaymentLifecycleService.cs");

        Assert.Contains("EnsureCollectionAccountingPostedForCodSettlementAsync", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("EnsureCollectionAccountingPostedForGatewaySettlementAsync", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("ResolveMissingPostedAccountingKeysAsync", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("collection_accounting_not_posted", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("journal.Status == JournalStatus.Posted", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("BuildCodDeliveredIdempotencyKey", lifecycleSource, StringComparison.Ordinal);
        Assert.Contains("BuildOnlinePaymentIdempotencyKey", lifecycleSource, StringComparison.Ordinal);

        var codGateIndex = lifecycleSource.IndexOf("EnsureCollectionAccountingPostedForCodSettlementAsync", StringComparison.Ordinal);
        var gatewayGateIndex = lifecycleSource.IndexOf("EnsureCollectionAccountingPostedForGatewaySettlementAsync", StringComparison.Ordinal);
        var codSettledMutationIndex = lifecycleSource.IndexOf("order.PaymentStatus = PaymentStatus.Settled;", codGateIndex, StringComparison.Ordinal);
        var gatewaySettledMutationIndex = lifecycleSource.IndexOf("order.PaymentStatus = PaymentStatus.Settled;", gatewayGateIndex, StringComparison.Ordinal);

        Assert.True(codGateIndex > 0, "COD settlement must validate collection accounting readiness.");
        Assert.True(gatewayGateIndex > 0, "Gateway settlement must validate collection accounting readiness.");
        Assert.True(codGateIndex < codSettledMutationIndex, "COD accounting readiness must be checked before settlement status mutation.");
        Assert.True(gatewayGateIndex < gatewaySettledMutationIndex, "Gateway accounting readiness must be checked before settlement status mutation.");
    }

    [Fact]
    public void GuestTracking_MustUseGuestSafeEndpoint_WithRenewableSessionBoundToken()
    {
        var controllerSource = ReadRepoFile(
            "PointOfSale.API",
            "Controllers",
            "StorefrontOrdersReadController.cs");
        var tokenServiceSource = ReadRepoFile(
            "PointOfSale.API",
            "Security",
            "CheckoutSessionTokenService.cs");
        var hookSource = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "hooks",
            "useOrder.ts");

        Assert.Contains("orders/{orderId:guid}/track-guest", controllerSource, StringComparison.Ordinal);
        Assert.Contains("CreateGuestTrackingToken", tokenServiceSource, StringComparison.Ordinal);
        Assert.Contains("ValidateGuestTrackingToken", tokenServiceSource, StringComparison.Ordinal);
        Assert.Contains("GuestTrackingTokenTtlMinutes", tokenServiceSource, StringComparison.Ordinal);
        Assert.Contains("RenewedTrackingToken", controllerSource, StringComparison.Ordinal);
        Assert.Contains("X-Session-Id", controllerSource, StringComparison.Ordinal);
        Assert.Contains("RecordGuestRecoverySuccess", controllerSource, StringComparison.Ordinal);
        Assert.Contains("track-guest", hookSource, StringComparison.Ordinal);
        Assert.Contains("persistActiveCheckoutOrderContext", hookSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutState_MustBeClearedOnLoginRegisterAndUserSwitch()
    {
        var authStoreSource = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "auth",
            "store",
            "authStore.ts");

        Assert.Contains("clearCheckoutStateForAuthTransition", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("login_or_register", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("user_switch_check_auth", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("user_switch_refresh", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("checkout-state", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("checkout-sensitive-session", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("active-checkout-order", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("emitCheckoutStateLeakPrevented", authStoreSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Observability_MustExposeS17MetricNames()
    {
        var telemetrySource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Instrumentation",
            "CommerceReliabilityTelemetry.cs");

        Assert.Contains("settlement_batch_mismatch_total", telemetrySource, StringComparison.Ordinal);
        Assert.Contains("cod_settlement_rejected_total", telemetrySource, StringComparison.Ordinal);
        Assert.Contains("guest_recovery_success_total", telemetrySource, StringComparison.Ordinal);
        Assert.Contains("checkout_state_leak_prevented_total", telemetrySource, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        // Strategy 1: Try the existing hardcoded approach (for backward compatibility)
        var pathParts = new List<string>
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."
        };
        pathParts.AddRange(segments);
        var candidatePath = Path.GetFullPath(Path.Combine(pathParts.ToArray()));
        
        if (File.Exists(candidatePath))
        {
            return File.ReadAllText(candidatePath);
        }

        // Strategy 2: Try REPO_ROOT environment variable
        var repoRootEnv = Environment.GetEnvironmentVariable("REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(repoRootEnv))
        {
            var envPath = Path.GetFullPath(Path.Combine(new[] { repoRootEnv }.Concat(segments).ToArray()));
            if (File.Exists(envPath))
            {
                return File.ReadAllText(envPath);
            }
        }

        // Strategy 3: Walk up from AppContext.BaseDirectory to find repo root marker
        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        if (repoRoot != null)
        {
            var repoPath = Path.GetFullPath(Path.Combine(new[] { repoRoot }.Concat(segments).ToArray()));
            if (File.Exists(repoPath))
            {
                return File.ReadAllText(repoPath);
            }
        }

        // All strategies failed - throw clear exception
        throw new FileNotFoundException(
            $"Could not locate repository file: {string.Join(Path.DirectorySeparatorChar, segments)}. " +
            $"Tried: (1) hardcoded parent navigation from '{AppContext.BaseDirectory}', " +
            $"(2) REPO_ROOT environment variable{(repoRootEnv != null ? $" ('{repoRootEnv}')" : " (not set)")}, " +
            $"(3) walking up to find .git or .sln marker{(repoRoot != null ? $" (found '{repoRoot}')" : " (not found)")}.");
    }

    private static string? FindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        
        while (current != null)
        {
            // Check for common repository markers
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                Directory.GetFiles(current.FullName, "*.sln").Length > 0 ||
                File.Exists(Path.Combine(current.FullName, ".gitignore")))
            {
                return current.FullName;
            }
            
            current = current.Parent;
        }
        
        return null;
    }
}
