using PointOfSale.Core.Enums.CommerceHubFinance;

namespace PointOfSale.Core.Entities.CommerceHubFinance;

public static class CommerceHubFinancialClassification
{
    public static CommerceHubFinancialBucket ClassifyBucket(
        CommerceHubFinancialEntityType entityType,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction)
    {
        return transactionType switch
        {
            CommerceHubFinancialTransactionType.OrderPayment => entityType == CommerceHubFinancialEntityType.Platform
                ? CommerceHubFinancialBucket.Cash
                : CommerceHubFinancialBucket.Payable,
            CommerceHubFinancialTransactionType.Commission => CommerceHubFinancialBucket.Receivable,
            CommerceHubFinancialTransactionType.Refund => CommerceHubFinancialBucket.Payable,
            CommerceHubFinancialTransactionType.Payout => CommerceHubFinancialBucket.Payable,
            CommerceHubFinancialTransactionType.Adjustment => entityType switch
            {
                CommerceHubFinancialEntityType.Platform => direction == CommerceHubFinancialTransactionDirection.Credit
                    ? CommerceHubFinancialBucket.Cash
                    : CommerceHubFinancialBucket.Payable,
                CommerceHubFinancialEntityType.Vendor => direction == CommerceHubFinancialTransactionDirection.Debit
                    ? CommerceHubFinancialBucket.Receivable
                    : CommerceHubFinancialBucket.Payable,
                _ => throw new ArgumentOutOfRangeException(nameof(entityType), "EntityType is invalid.")
            },
            CommerceHubFinancialTransactionType.Settlement => entityType == CommerceHubFinancialEntityType.Platform
                ? CommerceHubFinancialBucket.Cash
                : CommerceHubFinancialBucket.Receivable,
            CommerceHubFinancialTransactionType.Promotion => CommerceHubFinancialBucket.Receivable,
            _ => throw new ArgumentOutOfRangeException(nameof(transactionType), "TransactionType is invalid.")
        };
    }

    public static bool ImpactsPlatformCash(
        CommerceHubFinancialEntityType entityType,
        CommerceHubFinancialTransactionType transactionType)
    {
        return entityType == CommerceHubFinancialEntityType.Platform &&
               transactionType is CommerceHubFinancialTransactionType.OrderPayment
                   or CommerceHubFinancialTransactionType.Settlement
                   or CommerceHubFinancialTransactionType.Refund
                   or CommerceHubFinancialTransactionType.Payout
                   or CommerceHubFinancialTransactionType.Adjustment;
    }
}
