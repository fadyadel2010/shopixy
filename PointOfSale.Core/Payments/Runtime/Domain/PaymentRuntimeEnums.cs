namespace PointOfSale.Core.Payments.Runtime.Domain;

public enum PaymentStatus
{
    Unknown = 0,
    Pending = 1,
    ActionRequired = 2,
    AwaitingConfirmation = 3,
    Authorized = 4,
    Confirmed = 5,
    Captured = 6,
    Collected = 7,
    Settled = 8,
    PartiallyRefunded = 9,
    Refunded = 10,
    Cancelled = 11,
    Expired = 12,
    Failed = 13,
    Rejected = 14,
    Quarantined = 15,
    ReconciliationRequired = 16,
    Created = 17,
    AwaitingCustomer = 18,
    AwaitingProvider = 19,
    Verified = 20,
    Approved = 21,
    Completed = 22,
    ManualReview = 23,
    Recovery = 24,
    SettlementPending = 25,
    RefundPending = 26
}

public enum DecisionType
{
    Unknown = 0,
    Approved = 1,
    Rejected = 2,
    ManualReview = 3,
    Retry = 5,
    Cancelled = 12,
    Expired = 13,
    Recovery = 17,
    Quarantine = 18,
    FraudReview = 19,
    ComplianceReview = 20
}

public enum EvidenceType
{
    Unknown = 0,
    ProviderNotification = 1,
    Callback = 2,
    StatusInquiry = 3,
    Receipt = 4,
    OperatorDecision = 5,
    FulfillmentCollection = 6,
    RefundReport = 7,
    SettlementReport = 8,
    RecoveryRecord = 9,
    Webhook = 10,
    DriverConfirmation = 11,
    CarrierConfirmation = 12,
    RefundEvidence = 13,
    ManualApproval = 14
}

public enum EvidenceStatus
{
    Unknown = 0,
    Recorded = 1,
    PendingReview = 2,
    Admitted = 3,
    Rejected = 4,
    Superseded = 5
}

public enum SettlementStatus
{
    Unknown = 0,
    NotRequired = 1,
    Pending = 2,
    PartiallySettled = 3,
    Settled = 4,
    Failed = 5,
    ReconciliationRequired = 6,
    Replayed = 7,
    Duplicate = 8,
    Rejected = 9
}

public enum PaymentActionType
{
    Unknown = 0,
    Redirect = 1,
    EmbeddedCheckout = 2,
    ClientConfirmation = 3,
    ReferencePayment = 4,
    ReceiptUpload = 5,
    DeliveryCollection = 6,
    QrPayment = 7,
    WalletApproval = 8,
    OperatorReview = 9,
    StatusRefresh = 10,
    SdkConfirmation = 11,
    ReceiveShipment = 12,
    ManualReview = 13,
    ActionRetry = 14,
    ActionCancel = 15
}

public enum PaymentActionStatus
{
    Unknown = 0,
    Pending = 1,
    Available = 2,
    Presented = 3,
    Submitted = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7,
    Expired = 8
}

[Flags]
public enum ProviderCapability
{
    None = 0,
    Redirect = 1 << 0,
    EmbeddedCheckout = 1 << 1,
    ClientConfirmation = 1 << 2,
    Wallet = 1 << 3,
    Qr = 1 << 4,
    ReferencePayment = 1 << 5,
    ReceiptUpload = 1 << 6,
    OperatorReview = 1 << 7,
    DeliveryCollection = 1 << 8,
    Capture = 1 << 9,
    Refund = 1 << 10,
    PartialRefund = 1 << 11,
    Void = 1 << 12,
    Settlement = 1 << 13,
    AsyncConfirmation = 1 << 14,
    StatusInquiry = 1 << 15,
    Recovery = 1 << 16,
    ReplayProtection = 1 << 17,
    Idempotency = 1 << 18,
    MultiCurrency = 1 << 19,
    Metadata = 1 << 20,
    Audit = 1 << 21,
    Evidence = 1 << 22,
    Retry = 1 << 23,
    Quarantine = 1 << 24,
    Reconciliation = 1 << 25
}

[Flags]
public enum ProviderConstraint
{
    None = 0,
    RequiresRedirect = 1 << 0,
    RequiresDelivery = 1 << 1,
    RequiresReview = 1 << 2,
    RequiresReferenceNumber = 1 << 3,
    RequiresNotification = 1 << 4,
    RequiresCallback = 1 << 5,
    RequiresStatusInquiry = 1 << 6,
    RequiresSettlementReport = 1 << 7,
    RequiresReceipt = 1 << 8,
    RequiresMessageAuthentication = 1 << 9,
    RequiresApproval = 1 << 10,
    RequiresProviderSession = 1 << 11,
    RequiresCollectionEvidence = 1 << 12,
    RequiresEvidenceRetention = 1 << 13
}
