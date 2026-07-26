namespace PointOfSale.API.Services.Payments.Cod;

public interface ICodAdmissionLifecycle
{
    CodAdmissionLifecycleDecision Decide(CodRuntimeAdmissionOutcome outcome);
}

public sealed class CodAdmissionLifecycle : ICodAdmissionLifecycle
{
    public CodAdmissionLifecycleDecision Decide(CodRuntimeAdmissionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome.Status switch
        {
            CodAdmissionOutcomeStatus.Accepted => Decision(
                outcome,
                CodAdmissionLifecycleDecisionStatus.Accepted,
                CodAdmissionWorkflowState.CollectionAccepted,
                "COD_ADMISSION_ACCEPTED",
                "Canonical admission outcome accepted the COD collection workflow progression.",
                true),
            CodAdmissionOutcomeStatus.Rejected => Decision(
                outcome,
                CodAdmissionLifecycleDecisionStatus.Rejected,
                CodAdmissionWorkflowState.CollectionRejected,
                "COD_ADMISSION_REJECTED",
                "Canonical admission outcome rejected COD collection workflow progression.",
                true),
            CodAdmissionOutcomeStatus.Deferred => Decision(
                outcome,
                CodAdmissionLifecycleDecisionStatus.Deferred,
                CodAdmissionWorkflowState.CollectionDeferred,
                "COD_ADMISSION_DEFERRED",
                "Canonical admission outcome deferred COD collection workflow progression.",
                false),
            CodAdmissionOutcomeStatus.Incomplete => Decision(
                outcome,
                CodAdmissionLifecycleDecisionStatus.AwaitingEvidence,
                CodAdmissionWorkflowState.AwaitingCollectionEvidence,
                "COD_ADMISSION_AWAITING_EVIDENCE",
                "Canonical admission outcome requires additional COD collection evidence.",
                false),
            CodAdmissionOutcomeStatus.Unsupported => Decision(
                outcome,
                CodAdmissionLifecycleDecisionStatus.AwaitingReview,
                CodAdmissionWorkflowState.AwaitingOperationalReview,
                "COD_ADMISSION_AWAITING_REVIEW",
                "Canonical admission outcome requires operational review before workflow progression.",
                false),
            _ => Decision(
                outcome,
                CodAdmissionLifecycleDecisionStatus.AwaitingReview,
                CodAdmissionWorkflowState.AwaitingOperationalReview,
                "COD_ADMISSION_UNKNOWN_OUTCOME",
                "Unknown canonical admission outcome requires operational review before workflow progression.",
                false)
        };
    }

    private static CodAdmissionLifecycleDecision Decision(
        CodRuntimeAdmissionOutcome outcome,
        CodAdmissionLifecycleDecisionStatus status,
        CodAdmissionWorkflowState workflowState,
        string code,
        string description,
        bool terminalForAdmissionLifecycle)
    {
        return new CodAdmissionLifecycleDecision(
            outcome.AdmissionId,
            outcome.Status,
            status,
            workflowState,
            code,
            description,
            terminalForAdmissionLifecycle,
            outcome.Metadata);
    }
}

public sealed record CodAdmissionLifecycleDecision(
    CodAdmissionId AdmissionId,
    CodAdmissionOutcomeStatus OutcomeStatus,
    CodAdmissionLifecycleDecisionStatus Status,
    CodAdmissionWorkflowState WorkflowState,
    string Code,
    string Description,
    bool TerminalForAdmissionLifecycle,
    CodAdmissionMetadata Metadata);

public enum CodAdmissionLifecycleDecisionStatus
{
    Accepted = 1,
    Rejected = 2,
    Deferred = 3,
    AwaitingEvidence = 4,
    AwaitingReview = 5
}

public enum CodAdmissionWorkflowState
{
    CollectionAccepted = 1,
    CollectionRejected = 2,
    CollectionDeferred = 3,
    AwaitingCollectionEvidence = 4,
    AwaitingOperationalReview = 5
}
