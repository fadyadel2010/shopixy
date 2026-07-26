export type OrderStatus =
  | "Pending"
  | "Awaiting Payment"
  | "AwaitingPayment"
  | "Payment Pending"
  | "PaymentPending"
  | "Confirmed"
  | "Processing"
  | "Packed"
  | "Ready For Shipment"
  | "ReadyForShipment"
  | "Shipped"
  | "Delivered"
  | "Completed"
  | "Cancelled"
  | "Returned"
  | "Refunded"
  | "Failed"
  | "OnHold";

export type PaymentStatus =
  | "Pending"
  | "Authorized"
  | "Collected"
  | "Paid"
  | "PartiallyPaid"
  | "Failed"
  | "Refunded"
  | "PartiallyRefunded"
  | "AwaitingVerification"
  | "Rejected"
  | "Settled"
  | "CashOnDelivery"
  | "Expired"
  | "AmountMismatch";

export type PagedResult<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
};
