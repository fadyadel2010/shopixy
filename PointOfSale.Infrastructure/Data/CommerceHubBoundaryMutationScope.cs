namespace PointOfSale.Infrastructure.Data;

public sealed class CommerceHubBoundaryMutationScope : IDisposable
{
    private static readonly AsyncLocal<ScopeState> CurrentState = new();
    private readonly ScopeKind _scopeKind;
    private bool _disposed;

    private enum ScopeKind
    {
        OrderLifecycle,
        Payment,
        Refund,
        Payout,
        FinancialWorkflow,
        PaymentLifecycle,
        ShippingAssignment,
        Projection
    }

    private CommerceHubBoundaryMutationScope(ScopeKind scopeKind)
    {
        _scopeKind = scopeKind;
    }

    public static bool IsOrderLifecycleAuthorized =>
        CurrentState.Value?.OrderLifecycleDepth > 0;

    public static bool IsPaymentAuthorized =>
        CurrentState.Value?.PaymentDepth > 0;

    public static bool IsRefundAuthorized =>
        CurrentState.Value?.RefundDepth > 0;

    public static bool IsPayoutAuthorized =>
        CurrentState.Value?.PayoutDepth > 0;

    public static bool IsFinancialWorkflowAuthorized =>
        CurrentState.Value?.FinancialWorkflowDepth > 0;

    public static bool IsPaymentLifecycleAuthorized =>
        CurrentState.Value?.PaymentLifecycleDepth > 0;

    public static bool IsShippingAssignmentAuthorized =>
        CurrentState.Value?.ShippingAssignmentDepth > 0;

    public static bool IsProjectionAuthorized =>
        CurrentState.Value?.ProjectionDepth > 0;

    public static CommerceHubBoundaryMutationScope BeginOrderLifecycle()
    {
        var currentState = CurrentState.Value;
        var newState = currentState?.WithOrderLifecycleDepth(currentState.OrderLifecycleDepth + 1)
                       ?? new ScopeState(orderLifecycleDepth: 1);
        CurrentState.Value = newState;
        return new CommerceHubBoundaryMutationScope(ScopeKind.OrderLifecycle);
    }

    public static CommerceHubBoundaryMutationScope BeginPayment()
    {
        var currentState = CurrentState.Value;
        var newState = currentState?.WithPaymentDepth(currentState.PaymentDepth + 1)
                       ?? new ScopeState(paymentDepth: 1);
        CurrentState.Value = newState;
        return new CommerceHubBoundaryMutationScope(ScopeKind.Payment);
    }

    public static CommerceHubBoundaryMutationScope BeginRefund()
    {
        var currentState = CurrentState.Value;
        var newState = currentState?.WithRefundDepth(currentState.RefundDepth + 1)
                       ?? new ScopeState(refundDepth: 1);
        CurrentState.Value = newState;
        return new CommerceHubBoundaryMutationScope(ScopeKind.Refund);
    }

    public static CommerceHubBoundaryMutationScope BeginPayout()
    {
        var currentState = CurrentState.Value;
        var newState = currentState?.WithPayoutDepth(currentState.PayoutDepth + 1)
                       ?? new ScopeState(payoutDepth: 1);
        CurrentState.Value = newState;
        return new CommerceHubBoundaryMutationScope(ScopeKind.Payout);
    }

    public static CommerceHubBoundaryMutationScope BeginFinancialWorkflow()
    {
        var currentState = CurrentState.Value;
        var newState = currentState?.WithFinancialWorkflowDepth(currentState.FinancialWorkflowDepth + 1)
                       ?? new ScopeState(financialWorkflowDepth: 1);
        CurrentState.Value = newState;
        return new CommerceHubBoundaryMutationScope(ScopeKind.FinancialWorkflow);
    }

    public static CommerceHubBoundaryMutationScope BeginPaymentLifecycle()
    {
        var currentState = CurrentState.Value;
        var newState = currentState?.WithPaymentLifecycleDepth(currentState.PaymentLifecycleDepth + 1)
                       ?? new ScopeState(paymentLifecycleDepth: 1);
        CurrentState.Value = newState;
        return new CommerceHubBoundaryMutationScope(ScopeKind.PaymentLifecycle);
    }

    public static CommerceHubBoundaryMutationScope BeginShippingAssignment()
    {
        var currentState = CurrentState.Value;
        var newState = currentState?.WithShippingAssignmentDepth(currentState.ShippingAssignmentDepth + 1)
                       ?? new ScopeState(shippingAssignmentDepth: 1);
        CurrentState.Value = newState;
        return new CommerceHubBoundaryMutationScope(ScopeKind.ShippingAssignment);
    }

    public static CommerceHubBoundaryMutationScope BeginProjection()
    {
        var currentState = CurrentState.Value;
        var newState = currentState?.WithProjectionDepth(currentState.ProjectionDepth + 1)
                       ?? new ScopeState(projectionDepth: 1);
        CurrentState.Value = newState;
        return new CommerceHubBoundaryMutationScope(ScopeKind.Projection);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // S38C: Decrement the specific scope depth instead of restoring snapshot
        // This allows overlapping scopes to dispose in any order without corruption
        var currentState = CurrentState.Value;
        if (currentState != null)
        {
            // S38D: Exhaustive switch over ScopeKind - compiler emits warning CS8509 if new members are added
            // (becomes an error only if warnings are treated as errors via <TreatWarningsAsErrors>)
            CurrentState.Value = _scopeKind switch
            {
                ScopeKind.OrderLifecycle => currentState.WithOrderLifecycleDepth(Math.Max(0, currentState.OrderLifecycleDepth - 1)),
                ScopeKind.Payment => currentState.WithPaymentDepth(Math.Max(0, currentState.PaymentDepth - 1)),
                ScopeKind.Refund => currentState.WithRefundDepth(Math.Max(0, currentState.RefundDepth - 1)),
                ScopeKind.Payout => currentState.WithPayoutDepth(Math.Max(0, currentState.PayoutDepth - 1)),
                ScopeKind.FinancialWorkflow => currentState.WithFinancialWorkflowDepth(Math.Max(0, currentState.FinancialWorkflowDepth - 1)),
                ScopeKind.PaymentLifecycle => currentState.WithPaymentLifecycleDepth(Math.Max(0, currentState.PaymentLifecycleDepth - 1)),
                ScopeKind.ShippingAssignment => currentState.WithShippingAssignmentDepth(Math.Max(0, currentState.ShippingAssignmentDepth - 1)),
                ScopeKind.Projection => currentState.WithProjectionDepth(Math.Max(0, currentState.ProjectionDepth - 1))
            };
        }

        _disposed = true;
    }

    // S38C: Immutable ScopeState with copy-on-write semantics
    private sealed class ScopeState
    {
        public int OrderLifecycleDepth { get; }
        public int PaymentDepth { get; }
        public int RefundDepth { get; }
        public int PayoutDepth { get; }
        public int FinancialWorkflowDepth { get; }
        public int PaymentLifecycleDepth { get; }
        public int ShippingAssignmentDepth { get; }
        public int ProjectionDepth { get; }

        public ScopeState(
            int orderLifecycleDepth = 0,
            int paymentDepth = 0,
            int refundDepth = 0,
            int payoutDepth = 0,
            int financialWorkflowDepth = 0,
            int paymentLifecycleDepth = 0,
            int shippingAssignmentDepth = 0,
            int projectionDepth = 0)
        {
            OrderLifecycleDepth = orderLifecycleDepth;
            PaymentDepth = paymentDepth;
            RefundDepth = refundDepth;
            PayoutDepth = payoutDepth;
            FinancialWorkflowDepth = financialWorkflowDepth;
            PaymentLifecycleDepth = paymentLifecycleDepth;
            ShippingAssignmentDepth = shippingAssignmentDepth;
            ProjectionDepth = projectionDepth;
        }

        public ScopeState WithOrderLifecycleDepth(int depth) =>
            new(depth, PaymentDepth, RefundDepth, PayoutDepth, FinancialWorkflowDepth, PaymentLifecycleDepth, ShippingAssignmentDepth, ProjectionDepth);

        public ScopeState WithPaymentDepth(int depth) =>
            new(OrderLifecycleDepth, depth, RefundDepth, PayoutDepth, FinancialWorkflowDepth, PaymentLifecycleDepth, ShippingAssignmentDepth, ProjectionDepth);

        public ScopeState WithRefundDepth(int depth) =>
            new(OrderLifecycleDepth, PaymentDepth, depth, PayoutDepth, FinancialWorkflowDepth, PaymentLifecycleDepth, ShippingAssignmentDepth, ProjectionDepth);

        public ScopeState WithPayoutDepth(int depth) =>
            new(OrderLifecycleDepth, PaymentDepth, RefundDepth, depth, FinancialWorkflowDepth, PaymentLifecycleDepth, ShippingAssignmentDepth, ProjectionDepth);

        public ScopeState WithFinancialWorkflowDepth(int depth) =>
            new(OrderLifecycleDepth, PaymentDepth, RefundDepth, PayoutDepth, depth, PaymentLifecycleDepth, ShippingAssignmentDepth, ProjectionDepth);

        public ScopeState WithPaymentLifecycleDepth(int depth) =>
            new(OrderLifecycleDepth, PaymentDepth, RefundDepth, PayoutDepth, FinancialWorkflowDepth, depth, ShippingAssignmentDepth, ProjectionDepth);

        public ScopeState WithShippingAssignmentDepth(int depth) =>
            new(OrderLifecycleDepth, PaymentDepth, RefundDepth, PayoutDepth, FinancialWorkflowDepth, PaymentLifecycleDepth, depth, ProjectionDepth);

        public ScopeState WithProjectionDepth(int depth) =>
            new(OrderLifecycleDepth, PaymentDepth, RefundDepth, PayoutDepth, FinancialWorkflowDepth, PaymentLifecycleDepth, ShippingAssignmentDepth, depth);
    }
}
