namespace PointOfSale.Core.Enums;

public enum CashboxTransactionType
{
    Sale,
    SaleRefund,
    PurchasePayment,
    PurchaseRefund,
    ManualCashIn,
    ManualCashOut,
    OpeningBalance,
    ClosingBalance,
    CustomerPayment,
    SupplierPayment,
    PaymentReceived,
    Expense
}
