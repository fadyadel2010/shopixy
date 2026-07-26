using System.Reflection;
using System.Text.RegularExpressions;
using PointOfSale.Core.Payments.Runtime.Configuration;
using PointOfSale.Core.Payments.Runtime.Contracts;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentRuntimeFoundationCertificationTests
{
    private static readonly string[] RuntimeSourceRoots =
    [
        "PointOfSale.Core/Payments/Runtime",
        "PointOfSale.Infrastructure/Payments/Runtime"
    ];

    [Fact]
    public void CanonicalContracts_MustExistAsProviderNeutralContractsOnly()
    {
        var contractTypes = new[]
        {
            typeof(IPaymentRuntime),
            typeof(IPaymentProvider),
            typeof(IPaymentSession),
            typeof(IPaymentAttempt),
            typeof(IPaymentEvidence),
            typeof(IPaymentVerification),
            typeof(IPaymentDecision),
            typeof(IPaymentTransaction),
            typeof(IPaymentSettlement),
            typeof(IPaymentPolicyEngine),
            typeof(IPolicyContext),
            typeof(IPipelineContext)
        };

        Assert.All(contractTypes, type =>
        {
            Assert.True(type.IsInterface);
            Assert.StartsWith("PointOfSale.Core.Payments.Runtime.", type.Namespace, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void BaseTypes_MustBeImmutableValueObjects()
    {
        var valueObjectTypes = new[]
        {
            typeof(PaymentId),
            typeof(PaymentSessionId),
            typeof(PaymentAttemptId),
            typeof(EvidenceId),
            typeof(DecisionId),
            typeof(TransactionId),
            typeof(SettlementId),
            typeof(ProviderSessionId)
        };

        Assert.All(valueObjectTypes, type =>
        {
            Assert.True(type.IsValueType);
            Assert.True(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .All(field => field.IsInitOnly));
        });
    }

    [Fact]
    public void EnumsAndResults_MustBeCanonicalAndProviderNeutral()
    {
        Assert.Contains(nameof(PaymentStatus.Pending), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.Quarantined), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(DecisionType.Quarantine), Enum.GetNames<DecisionType>());
        Assert.Contains(nameof(EvidenceType.ProviderNotification), Enum.GetNames<EvidenceType>());
        Assert.Contains(nameof(SettlementStatus.ReconciliationRequired), Enum.GetNames<SettlementStatus>());
        Assert.Contains(nameof(PaymentActionType.ReferencePayment), Enum.GetNames<PaymentActionType>());
        Assert.Contains(nameof(ProviderCapability.ReplayProtection), Enum.GetNames<ProviderCapability>());
        Assert.Contains(nameof(ProviderConstraint.RequiresMessageAuthentication), Enum.GetNames<ProviderConstraint>());

        Assert.True(typeof(PaymentDecisionResult).IsClass);
        Assert.True(typeof(PointOfSale.Core.Payments.Runtime.Results.VerificationResult).IsClass);
        Assert.True(typeof(EvidenceAdmissionResult).IsClass);
        Assert.True(typeof(PointOfSale.Core.Payments.Runtime.Results.SettlementResult).IsClass);
        Assert.True(typeof(PolicyResult).IsClass);
        Assert.True(typeof(RuntimeResult).IsClass);
    }

    [Fact]
    public void FeatureFlags_MustExistAndRemainDisabledByDefault()
    {
        var expectedFlags = new[]
        {
            PaymentRuntimeFeatureFlags.NewPaymentRuntime,
            PaymentRuntimeFeatureFlags.NewPolicyEngine,
            PaymentRuntimeFeatureFlags.DualRunPayments,
            PaymentRuntimeFeatureFlags.ReplayEngine,
            PaymentRuntimeFeatureFlags.ProviderAdapterV2
        };

        Assert.All(expectedFlags, flag =>
        {
            Assert.True(PaymentRuntimeFeatureFlags.Defaults.ContainsKey(flag));
            Assert.False(PaymentRuntimeFeatureFlags.Defaults[flag]);
        });
    }

    [Fact]
    public void RuntimeFoundationSource_MustNotReferenceProvidersFrontendOrPersistence()
    {
        var sources = ReadRuntimeSources();
        var forbiddenTerms = new[]
        {
            "Stripe",
            "Paymob",
            "Fawry",
            "ManualWallet",
            "Vodafone",
            "Cash On Delivery",
            "PointOfSale.API.Services.Payments.Stripe",
            "PointOfSale.API.Services.Payments.Paymob",
            "PointOfSale.Storefront",
            "PointOfSale.Web",
            "PointOfSale.CommerceHub",
            "DbContext",
            "EntityFramework",
            "MigrationBuilder"
        };

        foreach (var source in sources)
        {
            foreach (var forbidden in forbiddenTerms)
            {
                Assert.DoesNotMatch(
                    new Regex($@"\b{Regex.Escape(forbidden)}\b", RegexOptions.CultureInvariant),
                    source.Text);
            }
        }
    }

    [Fact]
    public void RuntimeFoundationDocumentation_MustRecordDependencyGraphAndContributionRules()
    {
        var documentation = ReadRepoFile("docs/architecture/PAYMENT_RUNTIME_1_FOUNDATION.md");

        Assert.Contains("Policy decides. Runtime executes. Adapters translate.", documentation, StringComparison.Ordinal);
        Assert.Contains("Do not add provider-specific names", documentation, StringComparison.Ordinal);
        Assert.Contains("Do not add persistence or migrations", documentation, StringComparison.Ordinal);
        Assert.Contains("Keep feature flags disabled", documentation, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyRegistration_MustActivateCanonicalRuntimeWithoutDisabledPlaceholders()
    {
        var extension = ReadRepoFile("PointOfSale.Infrastructure/Payments/Runtime/PaymentRuntimeServiceCollectionExtensions.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("TryAddSingleton<PaymentRuntimeOrchestrator>", extension, StringComparison.Ordinal);
        Assert.Contains("TryAddSingleton<IPaymentRuntime>", extension, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<PaymentRuntimeOrchestrator>", extension, StringComparison.Ordinal);
        Assert.DoesNotContain("DisabledPaymentRuntime", extension, StringComparison.Ordinal);
        Assert.DoesNotContain("DisabledPaymentProvider", extension, StringComparison.Ordinal);
        Assert.DoesNotContain("DisabledPaymentPolicyEngine", extension, StringComparison.Ordinal);
        Assert.Contains("AddPaymentRuntimeFoundation();", program, StringComparison.Ordinal);
    }

    private static IReadOnlyCollection<(string Path, string Text)> ReadRuntimeSources()
    {
        var root = FindRepoRoot();
        return RuntimeSourceRoots
            .Select(relativeRoot => Path.Combine(root, relativeRoot.Replace('/', Path.DirectorySeparatorChar)))
            .SelectMany(path => Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .ToArray();
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);

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

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
