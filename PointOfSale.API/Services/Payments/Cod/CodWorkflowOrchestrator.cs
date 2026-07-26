using System.Collections.ObjectModel;
using PointOfSale.API.Services.Payments.Intents;

namespace PointOfSale.API.Services.Payments.Cod;

public interface ICodWorkflowOrchestrator
{
    CodWorkflowOrchestrationResult Orchestrate(
        CodWorkflowContext context,
        CodAdmissionLifecycleDecision decision);
}

public sealed class CodWorkflowOrchestrator : ICodWorkflowOrchestrator
{
    public CodWorkflowOrchestrationResult Orchestrate(
        CodWorkflowContext context,
        CodAdmissionLifecycleDecision decision)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(decision);

        var intents = decision.Status switch
        {
            CodAdmissionLifecycleDecisionStatus.Accepted => new[]
            {
                Intent(CodWorkflowIntentType.AdvanceCollection, context, decision),
                Intent(CodWorkflowIntentType.CompleteAdmissionWorkflow, context, decision)
            },
            CodAdmissionLifecycleDecisionStatus.Rejected => new[]
            {
                Intent(CodWorkflowIntentType.RejectAdmissionWorkflow, context, decision)
            },
            CodAdmissionLifecycleDecisionStatus.AwaitingEvidence => new[]
            {
                Intent(CodWorkflowIntentType.AwaitEvidence, context, decision)
            },
            CodAdmissionLifecycleDecisionStatus.Deferred => new[]
            {
                Intent(CodWorkflowIntentType.QueueOperationalReview, context, decision)
            },
            CodAdmissionLifecycleDecisionStatus.AwaitingReview => new[]
            {
                Intent(CodWorkflowIntentType.QueueOperationalReview, context, decision)
            },
            _ => new[]
            {
                Intent(CodWorkflowIntentType.QueueOperationalReview, context, decision)
            }
        };

        return new CodWorkflowOrchestrationResult(context, decision, intents);
    }

    private static CodWorkflowIntent Intent(
        CodWorkflowIntentType type,
        CodWorkflowContext context,
        CodAdmissionLifecycleDecision decision)
    {
        return new CodWorkflowIntent(
            type,
            context.TenantId,
            context.OrderIdentity,
            context.CollectionIdentity,
            context.AdmissionId,
            context.Correlation,
            decision.Code,
            decision.Description,
            decision.Metadata);
    }
}

public sealed record CodWorkflowContext(
    string TenantId,
    string? OrderIdentity,
    string CollectionIdentity,
    CodAdmissionId AdmissionId,
    CodAdmissionCorrelation Correlation);

public sealed record CodWorkflowIntent(
    CodWorkflowIntentType Type,
    string TenantId,
    string? OrderIdentity,
    string CollectionIdentity,
    CodAdmissionId AdmissionId,
    CodAdmissionCorrelation Correlation,
    string DecisionCode,
    string DecisionDescription,
    CodAdmissionMetadata Metadata) : IPaymentWorkflowIntent
{
    PaymentWorkflowIntentType IPaymentWorkflowIntent.IntentType => Type switch
    {
        CodWorkflowIntentType.AdvanceCollection => PaymentWorkflowIntentType.AdvanceCollection,
        CodWorkflowIntentType.AwaitEvidence => PaymentWorkflowIntentType.AwaitEvidence,
        CodWorkflowIntentType.QueueOperationalReview => PaymentWorkflowIntentType.QueueOperationalReview,
        CodWorkflowIntentType.CompleteAdmissionWorkflow => PaymentWorkflowIntentType.CompleteAdmissionWorkflow,
        CodWorkflowIntentType.RejectAdmissionWorkflow => PaymentWorkflowIntentType.RejectAdmissionWorkflow,
        _ => throw new InvalidOperationException($"Unsupported COD workflow intent type '{Type}'.")
    };

    string IPaymentWorkflowIntent.AdmissionIdentity => AdmissionId.Value;

    string IPaymentWorkflowIntent.CorrelationId => Correlation.CorrelationId;
}

public sealed record CodWorkflowOrchestrationResult(
    CodWorkflowContext Context,
    CodAdmissionLifecycleDecision Decision,
    IReadOnlyList<CodWorkflowIntent> Intents)
{
    private IReadOnlyList<CodWorkflowIntent> _intents = Freeze(Intents);

    public IReadOnlyList<CodWorkflowIntent> Intents
    {
        get => _intents;
        init => _intents = Freeze(value);
    }

    private static IReadOnlyList<CodWorkflowIntent> Freeze(IReadOnlyList<CodWorkflowIntent>? values)
    {
        return new ReadOnlyCollection<CodWorkflowIntent>(
            values?.ToArray() ?? Array.Empty<CodWorkflowIntent>());
    }
}

public enum CodWorkflowIntentType
{
    AdvanceCollection = 1,
    AwaitEvidence = 2,
    QueueOperationalReview = 3,
    CompleteAdmissionWorkflow = 4,
    RejectAdmissionWorkflow = 5
}
