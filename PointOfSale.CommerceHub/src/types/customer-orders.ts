import type {
  CustomerRefundPayoutSnapshot,
  CustomerRefundStatus,
  RefundEvidenceMedia,
  RefundReasonCode
} from "./customer-refunds";
import type { OrderStatus, PagedResult, PaymentStatus } from "./server";

export type CustomerOrdersQuery = {
  page?: number;
  pageSize?: number;
};

export type CustomerOrderItemStatus = "Pending" | "Shipped" | "Delivered" | "Cancelled";

export type CustomerOrderItem = {
  orderItemId: string;
  productName: string;
  variant?: string | null;
  quantity: number;
  price: number;
  total: number;
  status: CustomerOrderItemStatus;
  canRefund: boolean;
  maxRefundableQuantity: number;
  refundDisabledReason?: string | null;
  existingRefunds: CustomerOrderItemExistingRefund[];
  imageUrl?: string | null;
};

export type CustomerOrderItemExistingRefund = {
  refundId: string;
  quantity: number;
  amount: number;
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

export type CustomerOrderVendor = {
  subOrderId: string;
  vendorName: string;
  orderNumber: string;
  status: OrderStatus;
  paymentStatus: PaymentStatus;
  totalAmount: number;
  shippingAmount: number;
  items: CustomerOrderItem[];
};

export type CustomerOrderSummary = {
  orderId: string;
  orderNumber: string;
  createdAt: string;
  totalAmount: number;
  currency: string;
  status: OrderStatus;
  subOrders: CustomerOrderVendor[];
};

export type CustomerOrderTimelineEntry = {
  at: string;
  type: string;
  description: string;
  previousStatus?: string | null;
  newStatus?: string | null;
  vendorName?: string | null;
};

export type CustomerOrderDetails = {
  orderId: string;
  orderNumber: string;
  createdAt: string;
  updatedAt?: string | null;
  currency: string;
  subtotal: number;
  shippingTotal: number;
  totalAmount: number;
  status: OrderStatus;
  paymentStatus: PaymentStatus;
  paymentMethodCode: string;
  paymentMethodName: string;
  paymentReference?: string | null;
  paidAt?: string | null;
  canRetryPayment: boolean;
  paymentRecoveryMessage?: string | null;
  fullName: string;
  phoneNumber: string;
  addressLine: string;
  city: string;
  area?: string | null;
  subOrders: CustomerOrderVendor[];
  timeline: CustomerOrderTimelineEntry[];
};

export type CustomerOrdersResponse = PagedResult<CustomerOrderSummary>;
