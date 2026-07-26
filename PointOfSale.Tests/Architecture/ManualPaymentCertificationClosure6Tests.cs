using System.Text.Json;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;

namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentCertificationClosure6Tests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PaymentIntentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime CreatedAtUtc = new(2026, 7, 16, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset SubmittedAtUtc = new(2026, 7, 16, 10, 5, 0, TimeSpan.Zero);

    [Fact]
    public void DurableEvidenceAuthority_WhenRequiredReceiptEvidenceIsPresent_Accepts()
    {
        var authority = new ManualPaymentDurableEvidenceAuthority();
        var paymentIntent = CreatePaymentIntent();
        var proofMetadata = CreateProofMetadata(paymentIntent);
        var evidenceAdmission = CreateEvidenceAdmission(paymentIntent, proofMetadata);

        paymentIntent.ProviderMetadata = authority.WriteReceiptEvidence(
            paymentIntent,
            "{\"merchantWalletNumber\":\"01000000000\",\"walletInstructions\":\"Transfer manually.\"}",
            CreateInitializationResult(evidenceAdmission.ProviderEvidence),
            evidenceAdmission,
            proofMetadata);

        var replayFacts = authority.ValidateReceiptEvidence(
            paymentIntent,
            ManualPaymentDurableEvidencePurpose.Replay);
        var recoveryFacts = authority.ValidateReceiptEvidence(
            paymentIntent,
            ManualPaymentDurableEvidencePurpose.Recovery);

        Assert.Equal("manual-durable-evidence-v1", replayFacts.EvidenceVersion);
        Assert.Equal("Receipt", replayFacts.EvidenceType);
        Assert.Equal("PendingReview", replayFacts.EvidenceStatus);
        Assert.Equal(paymentIntent.ProofImageUrl, replayFacts.ProofIdentifier);
        Assert.Equal(replayFacts.EvidenceHash, recoveryFacts.EvidenceHash);
    }

    [Fact]
    public void DurableEvidenceAuthority_WhenEvidenceIsMissing_Rejects()
    {
        var authority = new ManualPaymentDurableEvidenceAuthority();
        var paymentIntent = CreatePaymentIntent();

        var exception = Assert.Throws<ManualPaymentApplicationException>(() =>
            authority.ValidateReceiptEvidence(
                paymentIntent,
                ManualPaymentDurableEvidencePurpose.Recovery));

        Assert.Equal("MANUAL_PAYMENT_REQUEST_INVALID", exception.Code);
    }

    [Fact]
    public void DurableEvidenceAuthority_WhenEvidenceConflictsWithSqlFacts_Rejects()
    {
        var authority = new ManualPaymentDurableEvidenceAuthority();
        var paymentIntent = CreatePaymentIntent();
        var proofMetadata = CreateProofMetadata(paymentIntent);
        var evidenceAdmission = CreateEvidenceAdmission(paymentIntent, proofMetadata);
        paymentIntent.ProviderMetadata = authority.WriteReceiptEvidence(
            paymentIntent,
            null,
            CreateInitializationResult(evidenceAdmission.ProviderEvidence),
            evidenceAdmission,
            proofMetadata);

        paymentIntent.ProofImageUrl = $"tenants/{TenantId:D}/manual-wallet-proofs/changed.png";

        Assert.Throws<ManualPaymentApplicationException>(() =>
            authority.ValidateReceiptEvidence(
                paymentIntent,
                ManualPaymentDurableEvidencePurpose.Replay));
    }

    [Fact]
    public void DurableEvidenceAuthority_WhenEvidenceTypeIsUnsupported_Rejects()
    {
        var authority = new ManualPaymentDurableEvidenceAuthority();
        var paymentIntent = CreatePaymentIntent();
        paymentIntent.ProviderMetadata = JsonSerializer.Serialize(
            CreateFacts(paymentIntent, evidenceType: "OperatorDecision"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Throws<ManualPaymentApplicationException>(() =>
            authority.ValidateReceiptEvidence(
                paymentIntent,
                ManualPaymentDurableEvidencePurpose.Replay));
    }

    [Fact]
    public void ReplayRecoveryAndReview_MustUseDurableEvidenceAuthority()
    {
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");
        var recovery = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs");
        var application = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");
        var foundation = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentFoundationServiceCollectionExtensions.cs");

        Assert.Contains("IManualPaymentDurableEvidenceAuthority", foundation, StringComparison.Ordinal);
        Assert.Contains("WriteReceiptEvidence", paymentService, StringComparison.Ordinal);
        Assert.Contains("ValidateManualPaymentDurableEvidence", paymentService, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentDurableEvidencePurpose.Replay", paymentService, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentDurableEvidencePurpose.Recovery", recovery, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentDurableEvidencePurpose.OperatorReview", application, StringComparison.Ordinal);
        Assert.Contains("CompleteDurableManualPaymentReceiptEvidence", paymentService, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepAuthorityOutOfRuntimeAdaptersAndControllers()
    {
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var bridge = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs");
        var providerAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentProviderAdapter.cs");
        var evidenceAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentEvidenceAdapter.cs");
        var controllers = string.Concat(
            ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs"),
            ReadRepoFile("PointOfSale.API/Controllers/PaymentsController.cs"),
            ReadRepoFile("PointOfSale.API/Controllers/PaymentLifecycleController.cs"),
            ReadRepoFile("PointOfSale.API/Controllers/StorefrontManualWalletController.cs"));

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("ManualPaymentDurableEvidence", source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("ManualPaymentDurableEvidence", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPaymentDurableEvidence", providerAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPaymentDurableEvidence", evidenceAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPaymentDurableEvidence", controllers, StringComparison.Ordinal);
    }

    private static PaymentIntent CreatePaymentIntent() => new()
    {
        Id = PaymentIntentId,
        TenantId = TenantId,
        OrderId = OrderId,
        Provider = PaymentProvider.ManualWallet,
        PaymentMethod = PaymentMethod.BankTransfer,
        Status = PaymentIntentStatus.AwaitingVerification,
        Amount = 100m,
        Currency = "EGP",
        PaymentReference = "PAY-MANUAL-EVIDENCE",
        IdempotencyKey = "storefront:checkout:manual-evidence",
        CreatedAt = CreatedAtUtc,
        WalletType = "BANK_TRANSFER",
        WalletPhoneNumber = "manual-reference",
        ProofImageUrl = $"tenants/{TenantId:D}/manual-wallet-proofs/proof.png",
        ProofImageExpiresAtUtc = CreatedAtUtc.AddDays(7),
        ProofImageDeleted = false
    };

    private static ManualPaymentProofMetadata CreateProofMetadata(PaymentIntent paymentIntent) =>
        new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["proof_identifier"] = paymentIntent.ProofImageUrl!,
            ["proof_source"] = "checkout_manual_wallet",
            ["payment_intent_id"] = paymentIntent.Id.ToString("N")
        });

    private static ManualPaymentEvidenceApplicationResult CreateEvidenceAdmission(
        PaymentIntent paymentIntent,
        ManualPaymentProofMetadata proofMetadata)
    {
        var actionId = new PaymentActionId(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var evidence = ManualPaymentEvidenceTranslator.Translate(
            new ManualPaymentEvidenceSubmission(
                paymentIntent.TenantId,
                paymentIntent.Id,
                actionId,
                new ActionCorrelationId("manual-evidence-test"),
                ManualPaymentChannel.BankTransfer,
                [ManualPaymentChannel.BankTransfer],
                referenceNumberRequired: true,
                paymentIntent.WalletPhoneNumber,
                paymentIntent.ProofImageUrl,
                proofMetadata,
                SubmittedAtUtc,
                CreatedAtUtc.AddHours(2),
                customerNotes: null,
                instructionVersion: "manual-config-test"));

        return new ManualPaymentEvidenceApplicationResult(
            evidence,
            new RuntimeResult(),
            new ManualPaymentExecutionResult());
    }

    private static ManualPaymentRuntimeApplicationResult CreateInitializationResult(ProviderEvidence evidence) =>
        new(
            "manual-config-test",
            ManualPaymentChannel.BankTransfer,
            "bank_transfer",
            CreatedAtUtc.AddHours(2),
            ReferenceNumberRequired: true,
            ProofRequired: true,
            evidence,
            new RuntimeResult(),
            new ManualPaymentExecutionResult());

    private static ManualPaymentDurableEvidenceFacts CreateFacts(
        PaymentIntent paymentIntent,
        string evidenceType = "Receipt") =>
        new(
            "manual-durable-evidence-v1",
            "MANUAL_PAYMENT_RECEIPT_ADMITTED_TO_REVIEW",
            paymentIntent.TenantId,
            paymentIntent.OrderId,
            paymentIntent.Id,
            paymentIntent.PaymentReference,
            paymentIntent.Amount,
            paymentIntent.Currency,
            "bank_transfer",
            evidenceType,
            "PendingReview",
            Guid.NewGuid().ToString("N"),
            new string('a', 64),
            $"manual_payment:evidence:{paymentIntent.ProofImageUrl}",
            $"manual_payment:proof:{paymentIntent.ProofImageUrl}",
            SubmittedAtUtc.UtcDateTime,
            "manual-config-test",
            "manual-proof-metadata-v1",
            paymentIntent.ProofImageUrl!,
            "checkout_manual_wallet",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["proof_identifier"] = paymentIntent.ProofImageUrl!,
                ["proof_source"] = "checkout_manual_wallet"
            },
            null,
            null);

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
