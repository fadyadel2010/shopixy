import type { ReactNode } from "react";

import type { CustomerOrderItemStatus, CustomerRefundStatus, OrderStatus, PaymentStatus } from "@/types";
import { cn } from "@/utils";
import { Badge } from "@/ui/badge";

const orderStatusClasses: Record<OrderStatus, string> = {
  Pending: "bg-slate-100 text-slate-700",
  "Awaiting Payment": "bg-amber-100 text-amber-800",
  AwaitingPayment: "bg-amber-100 text-amber-800",
  "Payment Pending": "bg-amber-100 text-amber-800",
  PaymentPending: "bg-amber-100 text-amber-800",
  Confirmed: "bg-sky-100 text-sky-700",
  Processing: "bg-indigo-100 text-indigo-700",
  Packed: "bg-indigo-100 text-indigo-700",
  "Ready For Shipment": "bg-amber-100 text-amber-800",
  ReadyForShipment: "bg-amber-100 text-amber-800",
  Shipped: "bg-amber-100 text-amber-800",
  Delivered: "bg-emerald-100 text-emerald-700",
  Completed: "bg-emerald-100 text-emerald-700",
  Cancelled: "bg-rose-100 text-rose-700",
  Returned: "bg-orange-100 text-orange-700",
  Refunded: "bg-rose-100 text-rose-700",
  Failed: "bg-rose-100 text-rose-700",
  OnHold: "bg-orange-100 text-orange-700"
};

const paymentStatusClasses: Record<PaymentStatus, string> = {
  Pending: "bg-slate-100 text-slate-700",
  Authorized: "bg-sky-100 text-sky-700",
  Collected: "bg-emerald-100 text-emerald-700",
  Paid: "bg-emerald-100 text-emerald-700",
  PartiallyPaid: "bg-amber-100 text-amber-800",
  Failed: "bg-rose-100 text-rose-700",
  Refunded: "bg-rose-100 text-rose-700",
  PartiallyRefunded: "bg-orange-100 text-orange-700",
  AwaitingVerification: "bg-indigo-100 text-indigo-700",
  Rejected: "bg-rose-100 text-rose-700",
  Settled: "bg-emerald-100 text-emerald-700",
  CashOnDelivery: "bg-amber-100 text-amber-800",
  Expired: "bg-rose-100 text-rose-700",
  AmountMismatch: "bg-rose-100 text-rose-700"
};

const itemStatusClasses: Record<CustomerOrderItemStatus, string> = {
  Pending: "bg-slate-100 text-slate-700",
  Shipped: "bg-amber-100 text-amber-800",
  Delivered: "bg-emerald-100 text-emerald-700",
  Cancelled: "bg-rose-100 text-rose-700"
};

const refundStatusClasses: Record<CustomerRefundStatus, string> = {
  Requested: "bg-slate-200 text-slate-700",
  UnderReview: "bg-sky-100 text-sky-700",
  Approved: "bg-amber-100 text-amber-800",
  WaitingForReturn: "bg-amber-100 text-amber-800",
  AwaitingPayout: "bg-amber-100 text-amber-800",
  ReadyForExecution: "bg-amber-100 text-amber-800",
  Rejected: "bg-rose-100 text-rose-700",
  Cancelled: "bg-slate-100 text-slate-600",
  Processing: "bg-violet-100 text-violet-700",
  Paid: "bg-emerald-100 text-emerald-700",
  Executed: "bg-emerald-100 text-emerald-700",
  Completed: "bg-emerald-100 text-emerald-700",
  Failed: "bg-rose-100 text-rose-700"
};

const formatRefundStatusLabel = (status: CustomerRefundStatus) => {
  if (status === "AwaitingPayout" || status === "ReadyForExecution") {
    return "Awaiting Payout";
  }

  if (status === "Paid" || status === "Executed" || status === "Completed") {
    return "Paid";
  }

  return status.replace(/([a-z])([A-Z])/g, "$1 $2");
};

const renderBadge = (label: ReactNode, className: string) => (
  <Badge variant="muted" className={cn("border-0 px-3 py-1 text-xs font-semibold", className)}>
    {label}
  </Badge>
);

export const OrderStatusBadge = ({ status }: { status: OrderStatus }) => renderBadge(status, orderStatusClasses[status]);

export const PaymentStatusBadge = ({ status }: { status: PaymentStatus }) =>
  renderBadge(status.replace(/([a-z])([A-Z])/g, "$1 $2"), paymentStatusClasses[status]);

export const ItemStatusBadge = ({ status }: { status: CustomerOrderItemStatus }) =>
  renderBadge(status, itemStatusClasses[status]);

export const RefundStatusBadge = ({ status }: { status: CustomerRefundStatus }) =>
  renderBadge(formatRefundStatusLabel(status), refundStatusClasses[status]);

export const formatCustomerDate = (value: string) =>
  new Intl.DateTimeFormat("en-EG", {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(new Date(value));
