import type { CustomerOrderItemExistingRefund, CustomerRefund, RefundEvidenceMedia } from "../../types";

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const CURRENCY = /^[A-Z]{3}$/;
const REFUND_STATUSES = new Set([
  "Requested",
  "UnderReview",
  "Approved",
  "WaitingForReturn",
  "AwaitingPayout",
  "ReadyForExecution",
  "Rejected",
  "Cancelled",
  "Processing",
  "Paid",
  "Executed",
  "Completed",
  "Failed"
]);
const REASON_CODES = new Set(["DamagedItem", "NotAsDescribed", "WrongItem", "MissingParts", "QualityIssue", "Other"]);
const PAYOUT_METHODS = new Set(["VodafoneCash", "InstaPay", "BankTransfer"]);
const MEDIA_TYPES = new Set(["image", "video"]);

const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const optionalText = (value: unknown) => value == null || typeof value === "string";
const guid = (value: unknown): value is string => text(value) && GUID.test(value);
const money = (value: unknown) => typeof value === "number" && Number.isFinite(value) && value >= 0;
const positiveInt = (value: unknown) => typeof value === "number" && Number.isInteger(value) && value > 0;
const date = (value: unknown) => text(value) && Number.isFinite(Date.parse(value));
const optionalDate = (value: unknown) => value == null || date(value);

function assertMedia(media: RefundEvidenceMedia[]) {
  const ids = new Set<string>();
  for (const item of media) {
    if (
      !guid(item?.mediaId) ||
      ids.has(item.mediaId) ||
      !text(item.url) ||
      !MEDIA_TYPES.has(item.mediaType) ||
      !text(item.mimeType) ||
      !money(item.sizeBytes) ||
      !text(item.fileName) ||
      !optionalText(item.thumbnailUrl) ||
      !optionalText(item.hash) ||
      !optionalDate(item.uploadedAtUtc)
    ) {
      throw new Error("Customer payment visibility authority rejected malformed refund media evidence.");
    }
    ids.add(item.mediaId);
  }
}

function assertRefundCommon(refund: CustomerRefund | CustomerOrderItemExistingRefund) {
  if (
    !guid(refund?.refundId) ||
    !positiveInt(refund.quantity) ||
    !money(refund.amount) ||
    !REFUND_STATUSES.has(refund.status) ||
    !text(refund.reason) ||
    !REASON_CODES.has(refund.reasonCode) ||
    !optionalText(refund.reasonText) ||
    !Array.isArray(refund.mediaUrls) ||
    !Array.isArray(refund.media) ||
    !date(refund.createdAt) ||
    !optionalDate(refund.updatedAt)
  ) {
    throw new Error("Customer payment visibility authority rejected malformed refund evidence.");
  }

  if (refund.payout) {
    if (
      !PAYOUT_METHODS.has(refund.payout.method) ||
      !text(refund.payout.maskedAccountIdentifier) ||
      !optionalText(refund.payout.accountHolderName) ||
      !optionalText(refund.payout.transactionReference) ||
      !optionalDate(refund.payout.paidAt) ||
      typeof refund.payout.isDevelopmentSimulation !== "boolean" ||
      typeof refund.payout.externalMoneyMovementConfirmed !== "boolean"
    ) {
      throw new Error("Customer payment visibility authority rejected malformed refund payout evidence.");
    }

    if (refund.payout.isDevelopmentSimulation && refund.payout.externalMoneyMovementConfirmed) {
      throw new Error("Customer payment visibility authority rejected contradictory refund payout evidence.");
    }
  }

  assertMedia(refund.media);
}

export function requireCustomerRefundVisibilityAuthority(refund: CustomerRefund): CustomerRefund {
  assertRefundCommon(refund);

  if (
    !guid(refund.orderId) ||
    !text(refund.orderNumber) ||
    !guid(refund.orderItemId) ||
    !guid(refund.vendorId) ||
    !text(refund.productName) ||
    !money(refund.unitPriceSnapshot) ||
    Math.abs(refund.amount - refund.unitPriceSnapshot * refund.quantity) >= 0.005 ||
    !CURRENCY.test(refund.currency)
  ) {
    throw new Error("Customer payment visibility authority rejected malformed refund visibility evidence.");
  }

  return refund;
}

/**
 * A customer create response is still only presentation evidence.  It must
 * remain bound to the exact order item that current backend authority allowed
 * the modal to submit; a response for a different item is not a replacement
 * scope candidate.
 */
export function requireCustomerCreatedRefundForCurrentOrderItem(
  refund: CustomerRefund,
  expectedOrderItemId: string
): CustomerRefund {
  const accepted = requireCustomerRefundVisibilityAuthority(refund);

  if (!guid(expectedOrderItemId) || accepted.orderItemId.toLowerCase() !== expectedOrderItemId.toLowerCase()) {
    throw new Error("Customer refund scope authority rejected create-response order-item substitution.");
  }

  return accepted;
}

export function requireCustomerExistingRefundVisibilityAuthority(
  refund: CustomerOrderItemExistingRefund,
  expectedItemTotal: number,
): CustomerOrderItemExistingRefund {
  assertRefundCommon(refund);

  if (!money(expectedItemTotal) || refund.amount - expectedItemTotal > 0.005) {
    throw new Error("Customer payment visibility authority rejected refund/order visibility contradiction.");
  }

  return refund;
}
