import type { OrderStatus, PaymentStatus } from "./server";

export type CommerceHubRecoverableOrderItem = {
  listingId: string;
  productId: string;
  variantId: string;
  productName: string;
  variantName?: string | null;
  variantAttributesJson?: string | null;
  imageUrl?: string | null;
  quantity: number;
  unitPrice: number;
  total: number;
};

export type CommerceHubRecoverableOrderSubOrder = {
  subOrderId: string;
  orderNumber: string;
  storeId: string;
  storeName: string;
  currency: string;
  subtotal: number;
  shipping: number;
  shippingMethodId?: string | null;
  shippingMethodName?: string | null;
  shippingSnapshotJson?: string | null;
  paymentMethod: "Online" | "CashOnDelivery";
  paymentMethodCode: string;
  paymentMethodName: string;
  paymentSnapshotJson?: string | null;
  paymentReferenceId?: string | null;
  total: number;
  status: OrderStatus;
  paymentStatus: PaymentStatus;
  paidAt?: string | null;
  items: CommerceHubRecoverableOrderItem[];
};

export type CommerceHubRecoverableOrder = {
  marketplaceOrderId: string;
  orderNumber: string;
  fullName: string;
  phoneNumber: string;
  addressLine: string;
  city: string;
  area?: string | null;
  notes?: string | null;
  currency: string;
  paymentMethodCode: string;
  paymentMethodName: string;
  paymentReference?: string | null;
  paymentReferenceId?: string | null;
  paymentGateway?: string | null;
  subtotal: number;
  shipping: number;
  total: number;
  status: OrderStatus;
  paymentStatus: PaymentStatus;
  createdAt: string;
  updatedAt: string;
  paidAt?: string | null;
  canRetryPayment: boolean;
  paymentRecoveryMessage?: string | null;
  subOrders: CommerceHubRecoverableOrderSubOrder[];
};
