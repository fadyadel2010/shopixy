using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Services.Payments.Cod;

namespace PointOfSale.Tests.Architecture;

public sealed class CodAdmissionLifecycleCertificationTests
{
    [Fact]
    public void Lifecycle_MustExistAndConsumeCanonicalAdmissionOutcomeOnly()
    {
        ICodAdmissionLifecycle lifecycle = new CodAdmissionLifecycle();
        var outcome = Outcome(CodAdmissionOutcomeStatus.Accepted);

        var decision = lifecycle.Decide(outcome);

        Assert.Equal(outcome.AdmissionId, decision.AdmissionId);
        Assert.Equal(CodAdmissionOutcomeStatus.Accepted, decision.OutcomeStatus);
        Assert.Equal(CodAdmissionLifecycleDecisionStatus.Accepted, decision.Status);
        Assert.Equal(CodAdmissionWorkflowState.CollectionAccepted, decision.WorkflowState);
    }

    [Theory]
    [InlineData(CodAdmissionOutcomeStatus.Accepted, CodAdmissionLifecycleDecisionStatus.Accepted, CodAdmissionWorkflowState.CollectionAccepted)]
    [InlineData(CodAdmissionOutcomeStatus.Rejected, CodAdmissionLifecycleDecisionStatus.Rejected, CodAdmissionWorkflowState.CollectionRejected)]
    [InlineData(CodAdmissionOutcomeStatus.Deferred, CodAdmissionLifecycleDecisionStatus.Deferred, CodAdmissionWorkflowState.CollectionDeferred)]
    [InlineData(CodAdmissionOutcomeStatus.Incomplete, CodAdmissionLifecycleDecisionStatus.AwaitingEvidence, CodAdmissionWorkflowState.AwaitingCollectionEvidence)]
    [InlineData(CodAdmissionOutcomeStatus.Unsupported, CodAdmissionLifecycleDecisionStatus.AwaitingReview, CodAdmissionWorkflowState.AwaitingOperationalReview)]
    public void OutcomeMapping_MustReturnCanonicalLifecycleDecisions(
        CodAdmissionOutcomeStatus outcomeStatus,
        CodAdmissionLifecycleDecisionStatus expectedDecision,
        CodAdmissionWorkflowState expectedWorkflowState)
    {
        var lifecycle = new CodAdmissionLifecycle();

        var decision = lifecycle.Decide(Outcome(outcomeStatus));

        Assert.Equal(expectedDecision, decision.Status);
        Assert.Equal(expectedWorkflowState, decision.WorkflowState);
    }

    [Fact]
    public void AcceptedRejectedAndDeferred_MustMapCorrectly()
    {
        var lifecycle = new CodAdmissionLifecycle();

        Assert.Equal(
            CodAdmissionLifecycleDecisionStatus.Accepted,
            lifecycle.Decide(Outcome(CodAdmissionOutcomeStatus.Accepted)).Status);
        Assert.Equal(
            CodAdmissionLifecycleDecisionStatus.Rejected,
            lifecycle.Decide(Outcome(CodAdmissionOutcomeStatus.Rejected)).Status);
        Assert.Equal(
            CodAdmissionLifecycleDecisionStatus.Deferred,
            lifecycle.Decide(Outcome(CodAdmissionOutcomeStatus.Deferred)).Status);
    }

    [Fact]
    public void LifecycleDecision_MustCarryFrozenOutcomeMetadata()
    {
        var values = new Dictionary<string, string> { ["source"] = "certification" };
        var outcome = new CodRuntimeAdmissionOutcome(
            new CodAdmissionId("admission-1"),
            CodAdmissionOutcomeStatus.Accepted,
            "accepted",
            "Canonical admission outcome for lifecycle certification.",
            new CodAdmissionMetadata(values, "source-reference", "actor-reference", null));

        var decision = new CodAdmissionLifecycle().Decide(outcome);
        values["source"] = "mutated";
        values["new"] = "leaked";

        Assert.Equal("certification", decision.Metadata.Values["source"]);
        Assert.False(decision.Metadata.Values.ContainsKey("new"));
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)decision.Metadata.Values).Add("decision", "mutation"));
    }

    [Fact]
    public void DependencyInjection_MustRegisterLifecycleAndRejectDuplicateRegistration()
    {
        var services = new ServiceCollection();

        services.AddCodAdmissionLifecycle();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<ICodAdmissionLifecycle>();

        Assert.NotNull(lifecycle);
        Assert.Single(services.Where(descriptor =>
            descriptor.ServiceType == typeof(ICodAdmissionLifecycle)));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCodAdmissionLifecycle());
        Assert.Contains("duplicate registration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgramRegistration_MustRegisterLifecycleAfterRuntimeAdmissionBridge()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("AddCodAdmissionLifecycle", program, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("AddCodRuntimeAdmissionBridge", StringComparison.Ordinal) <
            program.IndexOf("AddCodAdmissionLifecycle", StringComparison.Ordinal));
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveLifecyclePurity()
    {
        var source = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Cod/CodAdmissionLifecycle.cs");

        Assert.Contains("CodRuntimeAdmissionOutcome", source, StringComparison.Ordinal);
        Assert.Contains("CodAdmissionLifecycleDecision", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PointOfSale.Core.Payments.Runtime", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPaymentLifecycleAuthority", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SettlementService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentService", source, StringComparison.Ordinal);

        var runtimeFiles = Directory.GetFiles(
            Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var runtimeFile in runtimeFiles)
        {
            var runtimeSource = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("CodAdmissionLifecycle", runtimeSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ArchitectureDocument_MustRecordAdmissionLifecycleBoundary()
    {
        var document = CertificationSource.ReadRepoFile(
            "docs/architecture/COD_LIFECYCLE_1_CANONICAL_ADMISSION_LIFECYCLE.md");

        Assert.Contains("Executive Summary", document, StringComparison.Ordinal);
        Assert.Contains("Scope", document, StringComparison.Ordinal);
        Assert.Contains("Lifecycle Responsibilities", document, StringComparison.Ordinal);
        Assert.Contains("Outcome Consumption", document, StringComparison.Ordinal);
        Assert.Contains("Transition Rules", document, StringComparison.Ordinal);
        Assert.Contains("Registration", document, StringComparison.Ordinal);
        Assert.Contains("Regression", document, StringComparison.Ordinal);
        Assert.Contains("Certification Decision", document, StringComparison.Ordinal);
    }

    private static CodRuntimeAdmissionOutcome Outcome(CodAdmissionOutcomeStatus status)
    {
        return new CodRuntimeAdmissionOutcome(
            new CodAdmissionId("admission-1"),
            status,
            status.ToString().ToLowerInvariant(),
            "Canonical admission outcome for lifecycle certification.",
            new CodAdmissionMetadata(
                new Dictionary<string, string>
                {
                    ["source"] = "certification"
                },
                "source-reference",
                "actor-reference",
                "Lifecycle certification."));
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
