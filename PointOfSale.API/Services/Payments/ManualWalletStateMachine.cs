using PointOfSale.Core.Enums;
using System;

namespace PointOfSale.API.Services.Payments;

/// <summary>
/// Explicit state transition guard for Manual Wallet payments.
/// 
/// Phase P3.9: Manual Wallet Payments Hardening
/// </summary>
public static class ManualWalletStateMachine
{
    public static void EnsureValidTransition(PaymentIntentStatus current, PaymentIntentStatus next)
    {
        if (current == next) return;

        bool isValid = false;

        // Transitions allowed:
        // Pending -> AwaitingVerification -> Paid OR Rejected
        // 
        // Note: Code may transition Pending -> Paid directly if verification step is skipped/implicit in previous implementation,
        // but strict hardening suggests we should respect the flow or at least blocking invalid ones (e.g. Paid -> Rejected).

        switch (current)
        {
            case PaymentIntentStatus.Pending:
                // Can move to Verification, or directly to Paid/Rejected (if admin acts on Pending)
                // Also can be Cancelled/Expired by system
                if (next == PaymentIntentStatus.AwaitingVerification) isValid = true;
                if (next == PaymentIntentStatus.Paid) isValid = true; 
                if (next == PaymentIntentStatus.Rejected) isValid = true;
                if (next == PaymentIntentStatus.Cancelled) isValid = true; 
                if (next == PaymentIntentStatus.Expired) isValid = true;
                break;

            case PaymentIntentStatus.AwaitingVerification:
                if (next == PaymentIntentStatus.Paid) isValid = true;
                if (next == PaymentIntentStatus.Rejected) isValid = true;
                if (next == PaymentIntentStatus.Cancelled) isValid = true; // User/Admin can cancel?
                if (next == PaymentIntentStatus.Expired) isValid = true;
                break;

            case PaymentIntentStatus.Paid:
                // Terminal state (mostly)
                // Cannot go to Rejected
                break;

            case PaymentIntentStatus.Rejected:
                // Terminal state
                // Cannot go to Paid
                break;
                
            case PaymentIntentStatus.Failed:
                // Can retry? Manual wallet usually doesn't fail technically, it's rejected.
                break;
        }

        if (!isValid)
        {
            throw new InvalidOperationException($"Invalid Manual Wallet transition: {current} -> {next}");
        }
    }
}
