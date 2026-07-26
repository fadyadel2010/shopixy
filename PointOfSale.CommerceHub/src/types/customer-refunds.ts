export type CustomerRefundStatus =
  | "Requested"
  | "UnderReview"
  | "Approved"
  | "WaitingForReturn"
  | "AwaitingPayout"
  | "ReadyForExecution"
  | "Rejected"
  | "Cancelled"
  | "Processing"
  | "Paid"
  | "Executed"
  | "Completed"
  | "Failed";

export type RefundReasonCode =
  | "DamagedItem"
  | "NotAsDescribed"
  | "WrongItem"
  | "MissingParts"
  | "QualityIssue"
  | "Other";

export type RefundPayoutMethod =
  | "VodafoneCash"
  | "InstaPay"
  | "BankTransfer";

export type RefundEvidenceMedia = {
  mediaId: string;
  url: string;
  thumbnailUrl?: string | null;
  mediaType: "image" | "video" | string;
  mimeType: string;
  sizeBytes: number;
  hash?: string | null;
  uploadedAtUtc?: string | null;
  fileName: string;
};

export type CustomerCreateRefundRequest = {
  orderItemId: string;
  quantity: number;
  reasonCode: RefundReasonCode;
  reasonText?: string | null;
  savedPayoutDetailsId?: string | null;
  payoutDetails?: CustomerRefundPayoutInput | null;
  mediaUrls: string[];
};

export type CustomerRefundPayoutInput = {
  method: RefundPayoutMethod;
  accountIdentifier: string;
  accountHolderName?: string | null;
};

export type CustomerRefundPayoutSnapshot = {
  method: RefundPayoutMethod;
  maskedAccountIdentifier: string;
  accountHolderName?: string | null;
  transactionReference?: string | null;
  paidAt?: string | null;
};

export type CustomerRefundUploadRequest = {
  sha256: string;
};

export type CustomerRefundUploadResponse = {
  fileUrl: string;
};

export type CustomerRefund = {
  refundId: string;
  orderId: string;
  orderNumber: string;
  orderItemId: string;
  vendorId: string;
  productName: string;
  quantity: number;
  unitPriceSnapshot: number;
  amount: number;
  currency: string;
  status: CustomerRefundStatus;
  reason: string;
  reasonCode: RefundReasonCode;
  reasonText?: string | null;
  payout?: CustomerRefundPayoutSnapshot | null;
  mediaUrls: string[];
  media: RefundEvidenceMedia[];
  createdAt: string;
  updatedAt?: string | null;
};
