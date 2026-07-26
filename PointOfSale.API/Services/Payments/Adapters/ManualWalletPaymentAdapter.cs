using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Payments.Adapters;

public class ManualWalletPaymentAdapter : IPaymentAdapter
{
    public PaymentProvider Provider => PaymentProvider.ManualWallet;

    public Task<PaymentInitiationResult> InitiateAsync(
        PaymentIntent intent,
        CancellationToken cancellationToken)
    {
        // 1. Validate required fields
        if (string.IsNullOrWhiteSpace(intent.WalletType))
        {
            return Task.FromResult(new PaymentInitiationResult 
            { 
                IsSuccess = false, 
                ErrorMessage = "Wallet type is required for manual wallet payments." 
            });
        }

        if (string.IsNullOrWhiteSpace(intent.WalletPhoneNumber))
        {
            return Task.FromResult(new PaymentInitiationResult 
            { 
                IsSuccess = false, 
                ErrorMessage = "Wallet phone number is required for manual wallet payments." 
            });
        }

        if (string.IsNullOrWhiteSpace(intent.ProofImageUrl))
        {
            return Task.FromResult(new PaymentInitiationResult 
            { 
                IsSuccess = false, 
                ErrorMessage = "Proof of payment file is required for manual wallet payments." 
            });
        }

        // 2. Set status to AwaitingVerification
        intent.Status = PaymentIntentStatus.AwaitingVerification;

        // 3. Return success
        return Task.FromResult(new PaymentInitiationResult
        {
            IsSuccess = true,
            ReferenceCode = intent.PaymentReference // Use internal reference
        });
    }
}
