using PointOfSale.Core.Entities;

namespace PointOfSale.Core.DTOs;

/// <summary>
/// Result of a checkout-session mutation or resolution operation.
/// Separates durable persistence from best-effort event dispatch so callers
/// can avoid reporting a false failure when the session write succeeded.
/// </summary>
public sealed record CheckoutSessionOperationResult(
    CheckoutSession? Session,
    bool IsPartialSuccess = false,
    IReadOnlyList<string>? DispatchFailureStages = null)
{
    public static CheckoutSessionOperationResult Success(CheckoutSession? session) =>
        new(session);

    public static CheckoutSessionOperationResult PartialSuccess(
        CheckoutSession session,
        IReadOnlyList<string> dispatchFailureStages) =>
        new(session, true, dispatchFailureStages);
}
