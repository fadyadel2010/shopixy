namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontWhatsAppAuthorityCertificationTests
{
    [Fact]
    public void Notifications_MustRecomposeCurrentSqlOrderAndPaymentAuthority()
    {
        var authority = ReadRepoFile(
            "PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppDispatchAuthority.cs");

        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", authority, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Orders", authority, StringComparison.Ordinal);
        Assert.Contains("_dbContext.PaymentIntents", authority, StringComparison.Ordinal);
        Assert.Contains("x.TenantId == tenantId", authority, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentStatus.Paid", authority, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentStatus.Rejected", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("PayloadSnapshot", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_MustUseAtomicLeaseConsentAndAmbiguousDeliveryContainment()
    {
        var dispatcher = ReadRepoFile(
            "PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppMessageDispatcher.cs");
        var logService = ReadRepoFile(
            "PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppMessageLogService.cs");

        Assert.Contains("TryClaimDispatchAsync", dispatcher, StringComparison.Ordinal);
        Assert.Contains("HasMessagingEligibilityAsync", dispatcher, StringComparison.Ordinal);
        Assert.Contains("DELIVERY_UNKNOWN", dispatcher, StringComparison.Ordinal);
        Assert.Contains("providerMessageId: result.MessageId", dispatcher, StringComparison.Ordinal);
        Assert.Contains("FindOneAndUpdateAsync", logService, StringComparison.Ordinal);
        Assert.Contains("DispatchLeaseId", logService, StringComparison.Ordinal);
        Assert.Contains("\"PENDING\", \"QUEUED\", \"FAILED\"", logService, StringComparison.Ordinal);
        Assert.Contains("WhatsApp message idempotency key is required", logService, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageCreation_MustUseDeterministicBusinessIdentity()
    {
        var orderConsumer = ReadRepoFile(
            "PointOfSale.Infrastructure/Consumers/WhatsApp/WhatsAppOrderConfirmationConsumer.cs");
        var paymentService = ReadRepoFile(
            "PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppPaymentNotificationService.cs");
        var otpConsumer = ReadRepoFile(
            "PointOfSale.Infrastructure/Consumers/WhatsApp/WhatsAppOtpConsumer.cs");

        Assert.Contains("whatsapp:order-confirmation:", orderConsumer, StringComparison.Ordinal);
        Assert.Contains("whatsapp:payment-approved:", paymentService, StringComparison.Ordinal);
        Assert.Contains("whatsapp:payment-rejected:", paymentService, StringComparison.Ordinal);
        Assert.Contains("RelatedEntityType = \"PaymentIntent\"", paymentService, StringComparison.Ordinal);
        Assert.Contains("whatsapp:otp:", otpConsumer, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountOwnership_MustRequireTransportAttestationAndOneActiveUseCaseOwner()
    {
        var controller = ReadRepoFile(
            "PointOfSale.API/Controllers/Admin/WhatsAppAdminController.cs");
        var accountService = ReadRepoFile(
            "PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppAccountService.cs");
        var heartbeat = ReadRepoFile(
            "PointOfSale.Infrastructure/WhatsApp/Jobs/WhatsAppSessionHeartbeatJob.cs");
        var migration = ReadRepoFile(
            "PointOfSale.Infrastructure/Migrations/20260929115948_MainWhatsAppSingleAccountPerTenant.cs");

        Assert.Contains("ConnectedPhoneMatches", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("mark-connected", controller, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConnectedPhoneMatches", heartbeat, StringComparison.Ordinal);
        Assert.Contains("statusResult.Success", heartbeat, StringComparison.Ordinal);
        Assert.Contains("WHATSAPP_PROVIDER_UNAVAILABLE", controller, StringComparison.Ordinal);
        Assert.Contains("EnsureUseCaseOwnershipAsync", accountService, StringComparison.Ordinal);
        Assert.Contains("ix_whatsapp_accounts_tenant_id", migration, StringComparison.Ordinal);
        Assert.Contains("unique: true", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatsAppRuntime_MustUseBaileysWithoutPlaywrightOrChromium()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");
        var controller = ReadRepoFile(
            "PointOfSale.API/Controllers/Admin/WhatsAppAdminController.cs");
        var dispatcher = ReadRepoFile(
            "PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppMessageDispatcher.cs");
        var heartbeat = ReadRepoFile(
            "PointOfSale.Infrastructure/WhatsApp/Jobs/WhatsAppSessionHeartbeatJob.cs");
        var infrastructureProject = ReadRepoFile(
            "PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj");
        var tenant = ReadRepoFile("PointOfSale.Core/Entities/Tenant.cs");
        var dbContext = ReadRepoFile(
            "PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = ReadRepoFile(
            "PointOfSale.Infrastructure/Migrations/20261007154500_MAIN_BaileysOnlyTransport.cs");

        Assert.Contains("BaileysWhatsAppTransport", program, StringComparison.Ordinal);
        Assert.Contains("FromKeyedServices(\"baileys\")", controller, StringComparison.Ordinal);
        Assert.Contains("FromKeyedServices(\"baileys\")", dispatcher, StringComparison.Ordinal);
        Assert.Contains("FromKeyedServices(\"baileys\")", heartbeat, StringComparison.Ordinal);
        Assert.Contains("UseBaileys { get; set; } = true", tenant, StringComparison.Ordinal);
        Assert.Contains("Property(e => e.UseBaileys).HasDefaultValue(true)", dbContext, StringComparison.Ordinal);
        Assert.Contains("SET \"UseBaileys\" = TRUE", migration, StringComparison.Ordinal);

        foreach (var source in new[] { program, controller, dispatcher, heartbeat, infrastructureProject })
        {
            Assert.DoesNotContain("Microsoft.Playwright", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PlaywrightTransport", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PlaywrightWhatsAppEngine", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Chromium", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void BaileysSessionIdentity_MustComeFromTheLiveSocket()
    {
        var sessionManager = ReadRepoFile(
            "PointOfSale.WhatsApp.Baileys/src/services/SessionManager.ts");
        var sessionRoutes = ReadRepoFile(
            "PointOfSale.WhatsApp.Baileys/src/routes/sessions.ts");
        var transport = ReadRepoFile(
            "PointOfSale.Infrastructure/WhatsApp/Transport/BaileysWhatsAppTransport.cs");

        Assert.Contains("session.socket?.user?.id", sessionManager, StringComparison.Ordinal);
        Assert.Contains("session.connectedPhone", sessionManager, StringComparison.Ordinal);
        Assert.Contains("connectedPhone: session.connectedPhone", sessionRoutes, StringComparison.Ordinal);
        Assert.Contains("ConnectedPhone = result.ConnectedPhone", transport, StringComparison.Ordinal);
    }

    [Fact]
    public void Templates_MustBeTenantOwnedAllowlistedAndVariableBounded()
    {
        var accountService = ReadRepoFile(
            "PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppAccountService.cs");
        var resolver = ReadRepoFile(
            "PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppTemplateResolver.cs");

        Assert.Contains("Unsupported WhatsApp template type", accountService, StringComparison.Ordinal);
        Assert.Contains("\"OrderConfirmation\"", accountService, StringComparison.Ordinal);
        Assert.Contains("\"OTP\"", accountService, StringComparison.Ordinal);
        Assert.Contains("unsupported or malformed variables", accountService, StringComparison.Ordinal);
        Assert.Contains("t.TenantId == tenantId", resolver, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryMessaging_MustReloadVerifiedCurrentCartAndOptOutAuthority()
    {
        var scheduler = ReadRepoFile(
            "PointOfSale.Infrastructure/Services/NativeAbandonedCartRecoveryScheduler.cs");
        var authority = ReadRepoFile(
            "PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppDispatchAuthority.cs");

        Assert.Contains("_dbContext.AbandonedCarts", scheduler, StringComparison.Ordinal);
        Assert.Contains("item.TenantId == tenantId", scheduler, StringComparison.Ordinal);
        Assert.Contains("_eligibilityService.GetEligibilityStatusAsync", scheduler, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", authority, StringComparison.Ordinal);
        Assert.Contains("item.PhoneVerified", authority, StringComparison.Ordinal);
        Assert.Contains("canonicalOrderExists", authority, StringComparison.Ordinal);
        Assert.Contains("_consentService.CanSendMessageAsync", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("CartSnapshotJson", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionEvents_MustNotMutateForeignTenantAccounts()
    {
        var consumer = ReadRepoFile(
            "PointOfSale.Infrastructure/Consumers/WhatsApp/WhatsAppSessionMonitorConsumer.cs");

        Assert.Contains("x.TenantId == msg.TenantId.Value", consumer, StringComparison.Ordinal);
        Assert.Contains("IgnoreQueryFilters()", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatsAppRuntime_MustNotContainInboundConversationOrCommerceMutationEndpoints()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");
        var routes = ReadRepoFile("PointOfSale.WhatsApp.Baileys/src/index.ts");
        var authority = ReadRepoFile(
            "PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppDispatchAuthority.cs");

        Assert.DoesNotContain("WhatsAppWebhookController", program, StringComparison.Ordinal);
        Assert.DoesNotContain("/webhook", routes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SaveChangesAsync", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("order.PaymentStatus =", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("order.FulfillmentStatus =", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("intent.Status =", authority, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
