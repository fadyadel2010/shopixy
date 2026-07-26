import { sanitizePriceSnapshot } from "../utils/price";
import type { CheckoutCartPayload, CommerceHubCartPricingSelection } from "./cart";
import type { OrderStatus, PaymentStatus } from "./server";

export type CheckoutPaymentMethod = "COD" | "PAYMOB" | "STRIPE";

export type CheckoutCustomerFormData = {
  fullName: string;
  phoneNumber: string;
  addressLine: string;
  city: string;
  area: string;
  notes: string;
};

export type CheckoutFieldErrors = Partial<Record<keyof CheckoutCustomerFormData, string>>;

export type CommerceHubCheckoutRequest = {
  idempotencyKey?: string;
  cartVersion: number;
  promoCode?: string;
  customer: {
    fullName: string;
    phoneNumber: string;
    addressLine: string;
    city: string;
    area?: string;
    notes?: string;
  };
  paymentMethod: CheckoutPaymentMethod;
  shippingTotal: number;
  shippingSelections: {
    storeId: string;
    shippingOptionId: string;
    shippingPrice: number;
  }[];
  stores: {
    storeId: string;
    items: {
      listingId: string;
      productId: string;
      variantId: string;
      quantity: number;
      expectedUnitPrice: number;
    }[];
  }[];
};

export type CommerceHubCheckoutResponse = {
  marketplaceOrderId: string;
  orderNumber: string;
  idempotencyKey?: string | null;
  isIdempotentReplay: boolean;
  currency: string;
  paymentMethod: CheckoutPaymentMethod;
  status: OrderStatus;
  paymentStatus: PaymentStatus;
  paymentReference?: string | null;
  paymentReferenceId?: string | null;
  paidAt?: string | null;
  subtotal: number;
  shipping: number;
  shippingTotal: number;
  total: number;
  promoCode?: string | null;
  promoDiscountTotal: number;
  message: string;
  orderAccessToken?: string | null;
  subOrders: CommerceHubCheckoutSubOrder[];
  paymentSession?: CommerceHubCheckoutPaymentSession | null;
};

export type CommerceHubPaymentInitiateRequest = {
  orderId: string;
  provider: Exclude<CheckoutPaymentMethod, "COD">;
  orderAccessToken?: string;
};

export type CommerceHubPaymentInitiateResponse = {
  paymentId: string;
  marketplaceOrderId: string;
  orderNumber: string;
  provider: string;
  status: string;
  amount: number;
  currency: string;
  handoffType: "redirect_url" | "client_secret" | "unknown";
  paymentUrl?: string | null;
  paymentToken?: string | null;
  publishableKey?: string | null;
  providerReference: string;
  providerIntentId?: string | null;
  isReusable: boolean;
  message: string;
};

export type CommerceHubCheckoutSubOrder = {
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
  total: number;
  status: OrderStatus;
  paymentStatus: PaymentStatus;
  paymentReferenceId?: string | null;
  paidAt?: string | null;
  items: CommerceHubCheckoutSubOrderItem[];
};

export type CommerceHubCheckoutSubOrderItem = {
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

export type CommerceHubCheckoutPaymentSession = {
  provider: string;
  status: string;
  isSimulated: boolean;
  merchantReference: string;
  amount: number;
  currency: string;
  metadata: Record<string, string>;
};

const phonePattern = /^\+?[0-9()\-\s]{8,20}$/;

export const getDefaultCheckoutCustomer = (): CheckoutCustomerFormData => ({
  fullName: "",
  phoneNumber: "",
  addressLine: "",
  city: "",
  area: "",
  notes: ""
});

export const validateCheckoutCustomer = (customer: CheckoutCustomerFormData): CheckoutFieldErrors => {
  const errors: CheckoutFieldErrors = {};

  if (!customer.fullName.trim()) {
    errors.fullName = "Full name is required.";
  }

  if (!customer.phoneNumber.trim()) {
    errors.phoneNumber = "Phone number is required.";
  } else if (!phonePattern.test(customer.phoneNumber.trim())) {
    errors.phoneNumber = "Use a valid phone number format.";
  }

  if (!customer.addressLine.trim()) {
    errors.addressLine = "Address is required.";
  }

  if (!customer.city.trim()) {
    errors.city = "City is required.";
  }

  return errors;
};

export const buildCommerceHubCheckoutRequest = (
  cart: CheckoutCartPayload,
  customer: CheckoutCustomerFormData,
  paymentMethod: CheckoutPaymentMethod,
  shippingTotal: number,
  shippingSelections: CommerceHubCartPricingSelection[],
  promoCode?: string | null
): CommerceHubCheckoutRequest => {
  // S40: Sanitize cart.version to ensure it's a finite integer within backend bounds [0, 2147483647]
  const sanitizedCartVersion = 
    Number.isFinite(cart.version) && Number.isInteger(cart.version)
      ? Math.min(Math.max(0, cart.version), 2147483647)
      : 0;

  return {
    cartVersion: sanitizedCartVersion,
    promoCode: promoCode?.trim() ? promoCode.trim().toUpperCase() : undefined,
    customer: {
      fullName: customer.fullName.trim(),
      phoneNumber: customer.phoneNumber.trim(),
      addressLine: customer.addressLine.trim(),
      city: customer.city.trim(),
      area: customer.area.trim() || undefined,
      notes: customer.notes.trim() || undefined
    },
    paymentMethod,
    shippingTotal: Number.isFinite(shippingTotal) ? Math.max(0, Number(shippingTotal.toFixed(2))) : 0,
    shippingSelections: shippingSelections.map((selection) => ({
      storeId: selection.storeId,
      shippingOptionId: selection.shippingOptionId,
      shippingPrice: Number.isFinite(selection.shippingPrice)
        ? Math.max(0, Number(selection.shippingPrice.toFixed(2)))
        : 0
    })),
  stores: cart.stores.map((store) => ({
    storeId: store.storeId,
    items: store.items
      .filter((item) => item.listingId)
      .map((item) => {
        const sanitizedPrice = sanitizePriceSnapshot(item.priceSnapshot);
        
        // Emit warning in non-production environments when priceSnapshot is invalid
        if (sanitizedPrice === 0 && item.priceSnapshot !== 0 && import.meta.env.DEV) {
          console.warn(
            '[Checkout] Invalid priceSnapshot detected - falling back to 0',
            {
              listingId: item.listingId,
              productId: item.productId,
              variantId: item.variantId,
              sku: item.sku,
              rawPriceSnapshot: item.priceSnapshot,
              priceSnapshotType: typeof item.priceSnapshot
            }
          );
        }
        
        return {
          listingId: item.listingId!,
          productId: item.productId,
          variantId: item.variantId,
          quantity: item.quantity,
          expectedUnitPrice: sanitizedPrice
        };
      })
  }))
  };
};
