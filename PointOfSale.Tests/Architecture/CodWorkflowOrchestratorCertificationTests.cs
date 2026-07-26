using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Services.Payments.Cod;

namespace PointOfSale.Tests.Architecture;

public sealed class CodWorkflowOrchestratorCertificationTests
{
    [Fact]
    public void Workflow_MustExistAndConsumeLifecycleDecisionOnly()
    {
        ICodWorkflowOrchestrator orchestrator = new CodWorkflowOrchestrator();

        var result = orchestrator.Orchestrate(Context(), Decision(CodAdmissionLifecycleDecisionStatus.Accepted));

        Assert.Equal(CodAdmissionLifecycleDecisionStatus.Accepted, result.Decision.Status);
        Assert.All(result.Intents, intent => Assert.Equal(result.Context, ContextFromIntent(intent)));
    }

    [Fact]
    public void AcceptedDecision_MustMapToAdvanceAndCompleteIntents()
    {
        var result = new CodWorkflowOrchestrator().Orchestrate(
            Context(),
            Decision(CodAdmissionLifecycleDecisionStatus.Accepted));

        Assert.Equal(
            new[] { CodWorkflowIntentType.AdvanceCollection, CodWorkflowIntentType.CompleteAdmissionWorkflow },
            result.Intents.Select(intent => intent.Type));
    }

    [Fact]
    public void RejectedDecision_MustMapToRejectIntent()
    {
        var result = new CodWorkflowOrchestrator().Orchestrate(
            Context(),
            Decision(CodAdmissionLifecycleDecisionStatus.Rejected));

        var intent = Assert.Single(result.Intents);
        Assert.Equal(CodWorkflowIntentType.RejectAdmissionWorkflow, intent.Type);
    }

    [Fact]
    public void AwaitEvidenceDecision_MustMapToAwaitEvidenceIntent()
    {
        var result = new CodWorkflowOrchestrator().Orchestrate(
            Context(),
            Decision(CodAdmissionLifecycleDecisionStatus.AwaitingEvidence));

        var intent = Assert.Single(result.Intents);
        Assert.Equal(CodWorkflowIntentType.AwaitEvidence, intent.Type);
    }

    [Theory]
    [InlineData(CodAdmissionLifecycleDecisionStatus.Deferred)]
    [InlineData(CodAdmissionLifecycleDecisionStatus.AwaitingReview)]
    public void ReviewLikeDecisions_MustMapToOperationalReviewIntent(
        CodAdmissionLifecycleDecisionStatus status)
    {
        var result = new CodWorkflowOrchestrator().Orchestrate(Context(), Decision(status));

        var intent = Assert.Single(result.Intents);
        Assert.Equal(CodWorkflowIntentType.QueueOperationalReview, intent.Type);
    }

    [Fact]
    public void WorkflowContext_MustBeImmutable()
    {
        Assert.All(typeof(CodWorkflowContext).GetProperties(), AssertInitOnlyProperty);
        Assert.All(typeof(CodWorkflowIntent).GetProperties(), AssertInitOnlyProperty);
        Assert.All(typeof(CodWorkflowOrchestrationResult).GetProperties(), AssertInitOnlyProperty);
    }

    [Fact]
    public void WorkflowResultAndIntentMetadata_MustDefensivelyFreezeCollections()
    {
        var metadataValues = new Dictionary<string, string> { ["source"] = "certification" };
        var decision = new CodAdmissionLifecycleDecision(
            new CodAdmissionId("admission-1"),
            CodAdmissionOutcomeStatus.Accepted,
            CodAdmissionLifecycleDecisionStatus.Accepted,
            CodAdmissionWorkflowState.CollectionAccepted,
            "COD_WORKFLOW_ACCEPTED",
            "Canonical lifecycle decision for workflow certification.",
            true,
            new CodAdmissionMetadata(metadataValues, "source-reference", "actor-reference", null));
        var result = new CodWorkflowOrchestrator().Orchestrate(Context(), decision);

        metadataValues["source"] = "mutated";
        metadataValues["new"] = "leaked";

        Assert.Equal("certification", result.Intents[0].Metadata.Values["source"]);
        Assert.False(result.Intents[0].Metadata.Values.ContainsKey("new"));
        Assert.IsNotType<CodWorkflowIntent[]>(result.Intents);
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<CodWorkflowIntent>)result.Intents).Add(result.Intents[0]));
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)result.Intents[0].Metadata.Values).Add("intent", "mutation"));
    }

    [Fact]
    public void DependencyInjection_MustRegisterWorkflowAndRejectDuplicateRegistration()
    {
        var services = new ServiceCollection();

        services.AddCodWorkflowOrchestrator();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<ICodWorkflowOrchestrator>();

        Assert.NotNull(workflow);
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(ICodWorkflowOrchestrator));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCodWorkflowOrchestrator());
        Assert.Contains("duplicate registration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgramRegistration_MustRegisterWorkflowAfterLifecycle()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("AddCodWorkflowOrchestrator", program, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("AddCodAdmissionLifecycle", StringComparison.Ordinal) <
            program.IndexOf("AddCodWorkflowOrchestrator", StringComparison.Ordinal));
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveWorkflowPurity()
    {
        var source = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Cod/CodWorkflowOrchestrator.cs");

        Assert.Contains("CodAdmissionLifecycleDecision", source, StringComparison.Ordinal);
        Assert.Contains("CodWorkflowIntent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CodRuntimeAdmissionOutcome", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PointOfSale.Core.Payments.Runtime", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPaymentLifecycleAuthority", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SettlementService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IEmail", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Webhook", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureDocument_MustRecordCanonicalWorkflowBoundary()
    {
        var document = CertificationSource.ReadRepoFile(
            "docs/architecture/COD_WORKFLOW_1_CANONICAL_WORKFLOW.md");

        Assert.Contains("Executive Summary", document, StringComparison.Ordinal);
        Assert.Contains("Scope", document, StringComparison.Ordinal);
        Assert.Contains("Workflow Responsibilities", document, StringComparison.Ordinal);
        Assert.Contains("Lifecycle Consumption", document, StringComparison.Ordinal);
        Assert.Contains("Workflow Intents", document, StringComparison.Ordinal);
        Assert.Contains("Workflow Context", document, StringComparison.Ordinal);
        Assert.Contains("Registration", document, StringComparison.Ordinal);
        Assert.Contains("Regression", document, StringComparison.Ordinal);
        Assert.Contains("Certification Decision", document, StringComparison.Ordinal);
    }

    private static CodWorkflowContext Context()
    {
        return new CodWorkflowContext(
            "tenant-1",
            "order-1",
            "collection-1",
            new CodAdmissionId("admission-1"),
            new CodAdmissionCorrelation("correlation-1", "causation-1", "request-1", "trace-1"));
    }

    private static void AssertInitOnlyProperty(System.Reflection.PropertyInfo property)
    {
        Assert.NotNull(property.SetMethod);
        Assert.Contains(
            property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers(),
            modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");
    }

    private static CodWorkflowContext ContextFromIntent(CodWorkflowIntent intent)
    {
        return new CodWorkflowContext(
            intent.TenantId,
            intent.OrderIdentity,
            intent.CollectionIdentity,
            intent.AdmissionId,
            intent.Correlation);
    }

    private static CodAdmissionLifecycleDecision Decision(CodAdmissionLifecycleDecisionStatus status)
    {
        return new CodAdmissionLifecycleDecision(
            new CodAdmissionId("admission-1"),
            CodAdmissionOutcomeStatus.Accepted,
            status,
            WorkflowState(status),
            $"COD_WORKFLOW_{status.ToString().ToUpperInvariant()}",
            "Canonical lifecycle decision for workflow certification.",
            status is CodAdmissionLifecycleDecisionStatus.Accepted or CodAdmissionLifecycleDecisionStatus.Rejected,
            new CodAdmissionMetadata(
                new Dictionary<string, string>
                {
                    ["source"] = "certification"
                },
                "source-reference",
                "actor-reference",
                "Workflow certification."));
    }

    private static CodAdmissionWorkflowState WorkflowState(CodAdmissionLifecycleDecisionStatus status)
    {
        return status switch
        {
            CodAdmissionLifecycleDecisionStatus.Accepted => CodAdmissionWorkflowState.CollectionAccepted,
            CodAdmissionLifecycleDecisionStatus.Rejected => CodAdmissionWorkflowState.CollectionRejected,
            CodAdmissionLifecycleDecisionStatus.Deferred => CodAdmissionWorkflowState.CollectionDeferred,
            CodAdmissionLifecycleDecisionStatus.AwaitingEvidence => CodAdmissionWorkflowState.AwaitingCollectionEvidence,
            _ => CodAdmissionWorkflowState.AwaitingOperationalReview
        };
    }
}
