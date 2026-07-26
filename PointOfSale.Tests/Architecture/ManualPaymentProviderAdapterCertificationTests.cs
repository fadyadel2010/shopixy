using System.Text.Json;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentProviderAdapterCertificationTests
{
    [Fact]
    public void CanonicalRequestMapping_MustCreateManualProviderRequest()
    {
        var result = new ManualPaymentProviderAdapter().CreatePendingPayment(CreateRequest());

        Assert.True(result.Succeeded);
        Assert.False(result.Rejected);
        Assert.NotNull(result.ProviderRequest);
        Assert.Equal("manual_payment", result.ProviderRequest!.ProviderName);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.ProviderRequest.TenantId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), result.ProviderRequest.PaymentIntentId);
        Assert.Equal(123.46m, result.ProviderRequest.Amount);
        Assert.Equal("EGP", result.ProviderRequest.Currency);
        Assert.Equal("customer-123", result.ProviderRequest.Customer.CustomerReference);
        Assert.True(result.ProviderRequest.ReferenceNumberRequired);
        Assert.True(result.ProviderRequest.ProofRequired);
        Assert.Equal(ManualPaymentChannel.VodafoneCash, result.ProviderRequest.Channel);
        Assert.Equal("Vodafone Cash", result.ProviderRequest.ChannelDisplayName);
        Assert.Contains("Use the exact payment reference", result.ProviderRequest.Instructions, StringComparison.Ordinal);
        Assert.Contains("Send to wallet", result.ProviderRequest.Instructions, StringComparison.Ordinal);
        Assert.Equal("manual-v1", result.ProviderRequest.InstructionVersion);
        Assert.StartsWith("manual_payment:11111111111111111111111111111111:22222222222222222222222222222222:vodafone_cash:manual-v1", result.ProviderRequest.InstructionIdentifier, StringComparison.Ordinal);
        Assert.Equal(ManualPaymentProviderStatus.PendingManualPayment, result.ProviderRequest.ProviderStatus);
        Assert.Equal(PaymentStatus.AwaitingCustomer, result.ProviderRequest.CanonicalStatus);
    }

    [Fact]
    public void ProviderEvidenceMapping_MustTranslatePendingManualPaymentToCanonicalEvidence()
    {
        var result = new ManualPaymentProviderAdapter().CreatePendingPayment(CreateRequest());

        Assert.NotNull(result.ProviderEvidence);
        Assert.NotNull(result.CanonicalEvidence);
        Assert.Equal(result.ProviderRequest!.InstructionIdentifier, result.ProviderEvidence!.InstructionIdentifier);
        Assert.True(result.ProviderEvidence.ReferenceRequired);
        Assert.True(result.ProviderEvidence.ProofRequired);
        Assert.Equal(DateTimeOffset.Parse("2026-07-17T10:30:00Z"), result.ProviderEvidence.ExpirationUtc);
        Assert.Equal(ManualPaymentProviderStatus.PendingManualPayment, result.ProviderEvidence.ProviderStatus);
        Assert.Equal(PaymentStatus.AwaitingCustomer, result.ProviderEvidence.CanonicalStatus);

        Assert.Equal(new PaymentActionId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")), result.CanonicalEvidence!.ActionId);
        Assert.Equal(EvidenceType.Receipt, result.CanonicalEvidence.Type);
        Assert.Equal(EvidenceStatus.Recorded, result.CanonicalEvidence.Status);
        Assert.Equal("manual_payment", result.CanonicalEvidence.Source.Value);
        Assert.Equal(result.ProviderRequest.InstructionIdentifier, result.CanonicalEvidence.Reference.Value);
        Assert.Equal("manual_payment:instruction:" + result.ProviderRequest.InstructionIdentifier, result.CanonicalEvidence.PayloadReference.Value);
        Assert.Equal("PendingManualPayment", result.CanonicalEvidence.Metadata.Values["provider_status"]);
        Assert.Equal("AwaitingCustomer", result.CanonicalEvidence.Metadata.Values["canonical_status"]);
        Assert.Equal("vodafone_cash", result.CanonicalEvidence.Metadata.Values["channel"]);
        Assert.Equal("manual-v1", result.CanonicalEvidence.Metadata.Values["instruction_version"]);
        Assert.Equal("True", result.CanonicalEvidence.Metadata.Values["reference_required"]);
        Assert.Equal("True", result.CanonicalEvidence.Metadata.Values["proof_required"]);
    }

    [Fact]
    public void InstructionChannelAndExpirationMapping_MustBeCanonicalAndDeterministic()
    {
        var adapter = new ManualPaymentProviderAdapter();
        var first = adapter.CreatePendingPayment(CreateRequest());
        var second = adapter.CreatePendingPayment(CreateRequest());

        Assert.Equal("vodafone_cash", ManualPaymentChannelMapper.ToCanonical(ManualPaymentChannel.VodafoneCash));
        Assert.Equal("instapay", ManualPaymentChannelMapper.ToCanonical(ManualPaymentChannel.Instapay));
        Assert.Equal("2026-07-17T10:30:00.0000000+00:00", first.CanonicalEvidence!.Metadata.Values["expires_utc"]);
        Assert.Equal(first.ProviderRequest!.InstructionIdentifier, second.ProviderRequest!.InstructionIdentifier);
        Assert.Equal(first.CanonicalEvidence.Id, second.CanonicalEvidence!.Id);
        Assert.Equal(first.CanonicalEvidence.Hash, second.CanonicalEvidence.Hash);
    }

    [Fact]
    public void RejectedEvidence_MustFailClosedForUnknownChannelMissingInstructionsAndReferencePolicy()
    {
        var adapter = new ManualPaymentProviderAdapter();
        var unknownChannel = adapter.CreatePendingPayment(CreateRequest(channel: (ManualPaymentChannel)999));
        var missingInstructions = adapter.CreatePendingPayment(CreateRequest(instructionsConfigured: false));
        var missingReferencePolicy = adapter.CreatePendingPayment(CreateRequest(referenceRequired: null));

        AssertRejected(unknownChannel, "MANUAL_CHANNEL_UNKNOWN");
        AssertRejected(missingInstructions, "MANUAL_INSTRUCTIONS_MISSING");
        AssertRejected(missingReferencePolicy, "MANUAL_REFERENCE_POLICY_MISSING");
    }

    [Fact]
    public void CapabilityRejection_MustReturnRejectedProviderEvidenceNotExecution()
    {
        var result = new ManualPaymentProviderAdapter().RejectUnsupportedCapability(CreateRequest(), "approval_execution");

        AssertRejected(result, "MANUAL_CAPABILITY_UNSUPPORTED");
        Assert.Equal("approval_execution", result.CanonicalEvidence!.Metadata.Values["unsupported_capability"]);
        Assert.Equal(EvidenceStatus.Rejected, result.CanonicalEvidence.Status);
    }

    [Fact]
    public void CanonicalProviderErrorMapping_MustReturnProviderNeutralErrorForMalformedRequest()
    {
        var result = new ManualPaymentProviderAdapter().CreatePendingPayment(CreateRequest(actionId: new PaymentActionId(Guid.Empty)));

        Assert.False(result.Succeeded);
        Assert.False(result.Rejected);
        Assert.Null(result.CanonicalEvidence);
        Assert.NotNull(result.Error);
        Assert.Equal(CanonicalProviderErrorType.Validation, result.Error!.Type);
        Assert.Equal("MANUAL_PROVIDER_REQUEST_INVALID", result.Error.Code);
        Assert.Equal("manual_payment", result.Error.Metadata["provider"]);
        Assert.DoesNotContain("ManualPaymentProviderRequest", JsonSerializer.Serialize(result.Error), StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepManualAdapterTranslationOnlyAndRuntimeNeutral()
    {
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentProviderAdapter.cs");
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.Contains("IManualPaymentProviderAdapter", adapter, StringComparison.Ordinal);
        Assert.Contains("ProviderEvidenceFactory.Create", adapter, StringComparison.Ordinal);
        Assert.Contains("CanonicalProviderError", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IConfiguration", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IOptions", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", adapter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PaymentLifecycleService", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Upload", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Approve", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Review", adapter, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("ManualPaymentProviderAdapter", source, StringComparison.Ordinal);
            Assert.DoesNotContain("manual_payment", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("VodafoneCash", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Instapay", source, StringComparison.Ordinal);
        }
    }

    private static void AssertRejected(ManualPaymentProviderAdapterResult result, string code)
    {
        Assert.False(result.Succeeded);
        Assert.True(result.Rejected);
        Assert.Null(result.Error);
        Assert.NotNull(result.ProviderEvidence);
        Assert.NotNull(result.CanonicalEvidence);
        Assert.Equal(ManualPaymentProviderStatus.Rejected, result.ProviderEvidence!.ProviderStatus);
        Assert.Equal(PaymentStatus.Rejected, result.ProviderEvidence.CanonicalStatus);
        Assert.Equal(EvidenceStatus.Rejected, result.CanonicalEvidence!.Status);
        Assert.Equal("Rejected", result.CanonicalEvidence.Metadata.Values["provider_status"]);
        Assert.Equal(code, result.CanonicalEvidence.Metadata.Values["provider_error_code"]);
    }

    private static ManualPaymentCanonicalRequest CreateRequest(
        ManualPaymentChannel channel = ManualPaymentChannel.VodafoneCash,
        bool instructionsConfigured = true,
        bool? referenceRequired = true,
        PaymentActionId? actionId = null) =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            actionId ?? new PaymentActionId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            new ActionCorrelationId("correlation-manual-123"),
            123.456m,
            "egp",
            new ManualPaymentCustomer("customer-123", "buyer@example.test", "+201000000000"),
            referenceRequired,
            true,
            channel,
            new[]
            {
                new ManualPaymentChannelConfiguration
                {
                    Channel = ManualPaymentChannel.VodafoneCash,
                    Enabled = true,
                    DisplayName = "Vodafone Cash",
                    Instructions = "Send to wallet and upload proof."
                },
                new ManualPaymentChannelConfiguration
                {
                    Channel = ManualPaymentChannel.Instapay,
                    Enabled = true,
                    DisplayName = "Instapay",
                    Instructions = "Send using Instapay and upload proof."
                }
            },
            instructionsConfigured,
            instructionsConfigured ? "Use the exact payment reference in your transfer." : string.Empty,
            true,
            DateTimeOffset.Parse("2026-07-17T10:30:00Z"),
            "manual-v1",
            DateTimeOffset.Parse("2026-07-15T10:30:00Z"));

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
