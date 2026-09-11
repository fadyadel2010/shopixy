import { AlertTriangle, ArrowLeft, CheckCircle2, CreditCard, LogIn, ShoppingBag, UserRound } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { shallow } from "zustand/shallow";

import { normalizeApiError } from "@/api/api-error";
import { useCommerceHubCartPricing } from "@/cart";
import { CheckoutForm, CheckoutSummary, PaymentMethodSelector } from "@/checkout";
import {
  clearActivePaymentRecoveryContextIfMatches,
  persistActivePaymentRecoveryContext,
  readActivePaymentRecoveryContext
} from "@/features/checkout/activePaymentRecoveryContext";
import { EmbeddedStripePaymentForm } from "@/payments/EmbeddedStripePaymentForm";
import { EmptyState } from "@/shared/EmptyState";
import { SectionHeading } from "@/shared/SectionHeading";
import { cartService, checkoutService } from "@/services";
import {
  selectCartStoreIds,
  selectCartSummary,
  selectCartValidation,
  selectCheckoutPayload,
  selectCustomerProfile,
  selectIsCartLocked,
  selectIsCustomerAuthenticated,
  selectPromoCode,
  useCartStore,
  useCustomerAuthStore,
  useToastStore
} from "@/store";
import type {
  CheckoutCustomerFormData,
  CheckoutFieldErrors,
  CheckoutPaymentMethod,
  CommerceHubCartNotice,
  CommerceHubCheckoutResponse,
  CommerceHubPaymentInitiateResponse
} from "@/types";
import {
  buildCommerceHubCartPricingRequest,
  buildCommerceHubCheckoutRequest,
  getDefaultCheckoutCustomer,
  validateCheckoutCustomer
} from "@/types";
import { buildCheckoutAttemptFingerprint, createCheckoutAttemptIdempotencyKey } from "./checkoutIdempotency";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { formatCurrency, useDocumentTitle } from "@/utils";

const PROMO_INVALIDATION_CODES = new Set([
  "PROMO_NOT_FOUND",
  "PROMO_INACTIVE",
  "PROMO_EXPIRED",
  "PROMO_USAGE_LIMIT_REACHED",
  "PROMO_CUSTOMER_USAGE_LIMIT_REACHED",
  "PROMO_NOT_APPLICABLE"
]);

const getCheckoutPaymentMethodLabel = (paymentMethod: CheckoutPaymentMethod) => {
  switch (paymentMethod) {
    case "COD":
      return "Cash on Delivery";
    case "STRIPE":
      return "Stripe";
    default:
      return "Paymob";
  }
};

export const CheckoutPage = () => {
  const navigate = useNavigate();
  const showToast = useToastStore((state) => state.showToast);
  const storeIds = useCartStore(selectCartStoreIds, shallow);
  const summary = useCartStore(selectCartSummary, shallow);
  const validation = useCartStore(selectCartValidation, shallow);
  const checkoutPayload = useCartStore(selectCheckoutPayload, shallow);
  const isCartLocked = useCartStore(selectIsCartLocked);
  const appliedPromoCode = useCartStore(selectPromoCode);
  const isAuthenticated = useCustomerAuthStore(selectIsCustomerAuthenticated);
  const authenticatedCustomer = useCustomerAuthStore(selectCustomerProfile);
  const clearCart = useCartStore((state) => state.clearCart);
  const clearPromoCode = useCartStore((state) => state.clearPromoCode);
  const saveCart = useCartStore((state) => state.saveCart);
  const applyBackendCart = useCartStore((state) => state.applyBackendCart);
  const setCheckoutLock = useCartStore((state) => state.setCheckoutLock);

  const [customer, setCustomer] = useState<CheckoutCustomerFormData>(getDefaultCheckoutCustomer);
  const [fieldErrors, setFieldErrors] = useState<CheckoutFieldErrors>({});
  const [paymentMethod, setPaymentMethod] = useState<CheckoutPaymentMethod>("COD");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [apiError, setApiError] = useState<string | null>(null);
  const [promoNotice, setPromoNotice] = useState<string | null>(null);
  const [orderResult, setOrderResult] = useState<CommerceHubCheckoutResponse | null>(null);
  const [paymentInitiationResult, setPaymentInitiationResult] = useState<CommerceHubPaymentInitiateResponse | null>(null);
  const [paymentInitiationError, setPaymentInitiationError] = useState<string | null>(null);
  const [isInitiatingPayment, setIsInitiatingPayment] = useState(false);
  const idempotencyKeyRef = useRef(createCheckoutAttemptIdempotencyKey());
  const checkoutFingerprintRef = useRef<string | null>(null);
  const submissionInFlightRef = useRef(false);

  const hasMissingListings = useMemo(
    () => checkoutPayload.stores.some((store) => store.items.some((item) => !item.listingId)),
    [checkoutPayload.stores]
  );

  useEffect(() => {
    if (!authenticatedCustomer) {
      return;
    }

    setCustomer((current) => ({
      ...current,
      fullName: current.fullName || authenticatedCustomer.name,
      phoneNumber: current.phoneNumber || authenticatedCustomer.phoneNumber || ""
    }));
  }, [authenticatedCustomer]);

  const cartPricingQuery = useCommerceHubCartPricing(checkoutPayload, [], appliedPromoCode);
  const pricingApiError = cartPricingQuery.error ? normalizeApiError(cartPricingQuery.error) : null;
  const pricingError = pricingApiError?.message ?? null;
  const isPricingRefreshing = cartPricingQuery.isPending || cartPricingQuery.isFetching;
  const appliedPromo = cartPricingQuery.data?.promo?.isApplied ? cartPricingQuery.data.promo : null;
  const paymentMethods = cartPricingQuery.data?.paymentMethods ?? [];
  const availablePaymentMethods = paymentMethods.filter((method) => method.isAvailable);
  const hasAvailablePaymentMethod = availablePaymentMethods.length > 0;
  const selectedPaymentMethod = paymentMethods.find((method) => method.code === paymentMethod);
  const selectedPaymentMethodAvailable = selectedPaymentMethod?.isAvailable ?? paymentMethods.length === 0;
  const paymentAuthorityMessage =
    paymentMethods.length > 0 && !hasAvailablePaymentMethod
      ? "No common payment method is currently enabled across every store in this order."
      : null;

  useEffect(() => {
    if (!appliedPromoCode || !pricingApiError?.code || !PROMO_INVALIDATION_CODES.has(pricingApiError.code)) {
      return;
    }

    clearPromoCode();
    setPromoNotice(pricingApiError.message);
    showToast({
      title: "Promo removed",
      description: pricingApiError.message,
      tone: "error"
    });
  }, [appliedPromoCode, clearPromoCode, pricingApiError, showToast]);

  useEffect(() => {
    if (paymentMethods.length === 0 || selectedPaymentMethodAvailable) {
      return;
    }

    const fallbackMethod = paymentMethods.find((method) => method.isAvailable);
    if (fallbackMethod && fallbackMethod.code !== paymentMethod) {
      setPaymentMethod(fallbackMethod.code);
    }
  }, [paymentMethod, paymentMethods, selectedPaymentMethodAvailable]);

  const showCartNotices = useCallback(
    (notices: CommerceHubCartNotice[]) => {
      notices.forEach((notice) => {
        showToast({
          title: notice.code.includes("REMOVED")
            ? "Cart item removed"
            : notice.code.includes("PRICE_CHANGED")
              ? "Price updated"
              : "Cart updated",
          description: notice.message,
          tone: "error"
        });
      });
    },
    [showToast]
  );

  const pricedCart = useMemo(
    () => {
      const pricing = cartPricingQuery.data;
      const shippingByStore = new Map(
        pricing?.storeSelections.map((selection) => [selection.storeId, selection.shippingPrice]) ?? []
      );

      return {
        ...checkoutPayload,
        stores: checkoutPayload.stores.map((store) => {
          const shipping = pricing ? shippingByStore.get(store.storeId) ?? 0 : store.shipping;

          return {
            ...store,
            shipping,
            total: store.subtotal + shipping
          };
        }),
        summary: {
          ...checkoutPayload.summary,
          currency: pricing?.currency ?? checkoutPayload.summary.currency,
          subtotal: pricing?.subtotal ?? checkoutPayload.summary.subtotal,
          shipping: pricing?.shippingTotal ?? checkoutPayload.summary.shipping,
          total: pricing?.total ?? checkoutPayload.summary.total
        }
      };
    },
    [checkoutPayload, cartPricingQuery.data]
  );
  const authoritativeFinancialSummary = useMemo(() => {
    if (isPricingRefreshing || cartPricingQuery.isError || !cartPricingQuery.data) {
      return null;
    }

    return {
      ...checkoutPayload.summary,
      currency: cartPricingQuery.data.currency,
      subtotal: cartPricingQuery.data.subtotal,
      shipping: cartPricingQuery.data.shippingTotal,
      total: cartPricingQuery.data.total
    };
  }, [cartPricingQuery.data, cartPricingQuery.isError, checkoutPayload.summary, isPricingRefreshing]);
  const checkoutAttemptFingerprint = useMemo(
    () => buildCheckoutAttemptFingerprint(pricedCart, customer, paymentMethod, appliedPromoCode),
    [appliedPromoCode, customer, paymentMethod, pricedCart]
  );

  const rotateIdempotencyKey = useCallback(() => {
    const nextKey = createCheckoutAttemptIdempotencyKey();
    idempotencyKeyRef.current = nextKey;
    return nextKey;
  }, []);

  const ensureIdempotencyKeyForFingerprint = useCallback(
    (fingerprint: string) => {
      // Freeze idempotency key rotation while a submission is in flight
      // to preserve the key used for the active request across retries
      if (submissionInFlightRef.current) {
        return idempotencyKeyRef.current;
      }

      if (checkoutFingerprintRef.current == null) {
        checkoutFingerprintRef.current = fingerprint;
        return idempotencyKeyRef.current;
      }

      if (checkoutFingerprintRef.current !== fingerprint) {
        checkoutFingerprintRef.current = fingerprint;
        return rotateIdempotencyKey();
      }

      return idempotencyKeyRef.current;
    },
    [rotateIdempotencyKey]
  );

  useEffect(() => {
    ensureIdempotencyKeyForFingerprint(checkoutAttemptFingerprint);
  }, [checkoutAttemptFingerprint, ensureIdempotencyKeyForFingerprint]);

  const isCheckoutDisabled =
    isSubmitting ||
    (!isSubmitting && isCartLocked) ||
    storeIds.length === 0 ||
    !validation.canCheckout ||
    hasMissingListings ||
    !hasAvailablePaymentMethod ||
    isPricingRefreshing ||
    cartPricingQuery.isError ||
    !cartPricingQuery.data;

  const checkoutMessage = isSubmitting
    ? "Placing the marketplace order and splitting it into store sub-orders."
    : isCartLocked
      ? "Your cart is locked because checkout is already in progress in another session."
    : !isAuthenticated
      ? "Sign in to place this order and link it to your Shopixy customer account."
      : paymentAuthorityMessage
        ? paymentAuthorityMessage
      : hasMissingListings
        ? "Some items are missing listing references and need to be added to cart again."
        : pricingError
          ? pricingError
          : isPricingRefreshing
            ? "Refreshing the latest shipping, total, and payment eligibility before the order is submitted."
            : validation.issues[0]?.message ?? "We will validate stock and recompute all totals again on the server before the order is created.";

  useDocumentTitle(orderResult ? `${orderResult.orderNumber} | CommerceHub` : "Checkout | CommerceHub");

  const handleFieldChange = (field: keyof CheckoutCustomerFormData, value: string) => {
    setCustomer((current) => ({
      ...current,
      [field]: value
    }));

    setFieldErrors((current) => {
      if (!current[field]) {
        return current;
      }

      const next = { ...current };
      delete next[field];
      return next;
    });
  };

  const initiateOnlinePayment = useCallback(
    async (response: CommerceHubCheckoutResponse) => {
      if (response.paymentMethod === "COD") {
        return;
      }

      setIsInitiatingPayment(true);
      setPaymentInitiationError(null);

      try {
        const initiationResponse = await checkoutService.initiatePayment({
          orderId: response.marketplaceOrderId,
          provider: response.paymentMethod
        }, response.total, response.currency);

        setPaymentInitiationResult(initiationResponse);

        if (initiationResponse.handoffType === "redirect_url" && initiationResponse.paymentUrl) {
          showToast({
            title: initiationResponse.isReusable ? "Resuming payment" : "Redirecting to payment",
            description: `${response.orderNumber} is waiting for payment confirmation. Complete payment to finish the order lifecycle.`,
            tone: "success"
          });
          window.location.assign(initiationResponse.paymentUrl);
          return;
        }

        if (
          initiationResponse.handoffType === "client_secret" &&
          initiationResponse.paymentToken &&
          initiationResponse.publishableKey
        ) {
          showToast({
            title: initiationResponse.isReusable ? "Stripe payment resumed" : "Enter payment details",
            description: `${response.orderNumber} is waiting for Stripe payment confirmation. Complete the embedded payment form to continue.`,
            tone: "success"
          });
          return;
        }

        const message = initiationResponse.message || "Payment was initiated, but the provider redirect URL is not available yet.";
        setPaymentInitiationError(message);
        showToast({
          title: "Payment still pending",
          description: message,
          tone: "error"
        });
      } catch (error) {
        const api = normalizeApiError(error);
        setPaymentInitiationError(api.message);
        showToast({
          title: "Payment initiation failed",
          description: api.message,
          tone: "error"
        });
        throw error;
      } finally {
        setIsInitiatingPayment(false);
      }
    },
    [showToast]
  );

  const handleEmbeddedStripePending = useCallback(
    (message: string) => {
      setPaymentInitiationError(null);
      showToast({
        title: "Payment submitted",
        description: message,
        tone: "success"
      });
    },
    [showToast]
  );

  const handleEmbeddedStripeError = useCallback(
    (message: string) => {
      showToast({
        title: "Stripe payment failed",
        description: message,
        tone: "error"
      });
    },
    [showToast]
  );

  const handleRetryPayment = useCallback(async () => {
    if (!orderResult) {
      return;
    }

    try {
      await initiateOnlinePayment(orderResult);
    } catch {
      // The order already exists; keep the customer on the payment-pending recovery path.
    }
  }, [initiateOnlinePayment, orderResult]);

  const handlePlaceOrder = async () => {
    if (!isAuthenticated) {
      navigate("/login", {
        state: {
          redirectTo: "/checkout"
        }
      });
      return;
    }

    const nextErrors = validateCheckoutCustomer(customer);
    setFieldErrors(nextErrors);
    setApiError(null);
    setPaymentInitiationError(null);
    setPaymentInitiationResult(null);

    if (Object.keys(nextErrors).length > 0) {
      showToast({
        title: "Checkout form needs attention",
        description: "Please complete the required customer details before placing the order.",
        tone: "error"
      });
      return;
    }

    if (isCheckoutDisabled) {
      return;
    }

    try
    {
      setCheckoutLock(true);
      setIsSubmitting(true);
      submissionInFlightRef.current = true;

      const authoritativeCart = await saveCart({ persistIfAvailableOnly: false });
      if (authoritativeCart?.notices.length) {
        showCartNotices(authoritativeCart.notices);
      }

      const authoritativeState = useCartStore.getState();
      const authoritativePayload = selectCheckoutPayload(authoritativeState);
      const authoritativeValidation = selectCartValidation(authoritativeState);

      if (authoritativePayload.stores.length === 0 || !authoritativeValidation.canCheckout) {
        const message =
          authoritativeValidation.issues[0]?.message ??
          "Your cart changed during validation. Review the updated cart before placing the order.";

        setApiError(message);
        showToast({
          title: "Cart updated",
          description: message,
          tone: "error"
        });
        return;
      }

      const authoritativePricing = await cartService.priceCart(buildCommerceHubCartPricingRequest(authoritativePayload, [], appliedPromoCode));
      const authoritativePaymentMethod = authoritativePricing.paymentMethods.find((method) => method.code === paymentMethod);

      if (!authoritativePaymentMethod?.isAvailable) {
        const fallbackPaymentMethod = authoritativePricing.paymentMethods.find((method) => method.isAvailable);
        if (fallbackPaymentMethod && fallbackPaymentMethod.code !== paymentMethod) {
          setPaymentMethod(fallbackPaymentMethod.code);
        }

        const message =
          authoritativePaymentMethod?.unavailableReason ??
          "The selected payment method is not enabled for every store in this order. Review the available methods and try again.";

        setApiError(message);
        showToast({
          title: "Payment method updated",
          description: message,
          tone: "error"
        });
        return;
      }

      const request = buildCommerceHubCheckoutRequest(
        authoritativePayload,
        customer,
        paymentMethod,
        authoritativePricing.shippingTotal,
        authoritativePricing.storeSelections,
        appliedPromoCode
      );
      const requestFingerprint = buildCheckoutAttemptFingerprint(
        {
          ...authoritativePayload,
          summary: {
            ...authoritativePayload.summary,
            currency: authoritativePricing.currency,
            subtotal: authoritativePricing.subtotal,
            shipping: authoritativePricing.shippingTotal,
            total: authoritativePricing.total
          }
        },
        customer,
        paymentMethod,
        appliedPromoCode
      );
      const requestIdempotencyKey = ensureIdempotencyKeyForFingerprint(requestFingerprint);
      const response = await checkoutService.placeOrder(request, requestIdempotencyKey);

      setOrderResult(response);
      if (response.paymentSession?.status === "requires_initiation" && response.paymentMethod !== "COD") {
        persistActivePaymentRecoveryContext({
          orderId: response.marketplaceOrderId,
          orderNumber: response.orderNumber
        });
      } else {
        clearActivePaymentRecoveryContextIfMatches(response.marketplaceOrderId);
      }

      void clearCart().catch((clearCartError) => {
        console.error("[CommerceHub Checkout] Order placed but local cart clear failed:", clearCartError);
        showToast({
          title: "Order placed, but cart still needs refresh",
          description: "The order succeeded, but we couldn't clear the local cart state. Refresh the page if the cart still shows old items.",
          tone: "error"
        });
      });

      if (response.paymentSession?.status === "requires_initiation" && response.paymentMethod !== "COD") {
        showToast({
          title: "Order created",
          description: `${response.orderNumber} was created and is waiting for payment initiation. Redirecting you to complete payment now.`,
          tone: "success"
        });

        try {
          await initiateOnlinePayment(response);
        } catch {
          // The order is already committed. Keep the customer on the recovery-safe payment-pending screen.
        }
      } else {
        showToast({
          title: "Order placed successfully",
          description: `${response.orderNumber} was created with ${response.subOrders.length} store sub-order${response.subOrders.length === 1 ? "" : "s"}.`,
          tone: "success"
        });
      }
    }
    catch (error)
    {
      const api = normalizeApiError(error);

      if (api.code === "CART_LOCKED") {
        try {
          const latestCart = await cartService.getCart();
          applyBackendCart(latestCart);
        } catch (refreshError) {
          console.error("[CommerceHub Checkout] Failed to refresh cart after checkout lock error:", refreshError);
        }
      }

      if (!api.isRetryable) {
        rotateIdempotencyKey();
      }

      setApiError(api.message);
      showToast({
        title: "Checkout failed",
        description: api.message,
        tone: "error"
      });
    }
    finally
    {
      submissionInFlightRef.current = false;
      setCheckoutLock(false);
      setIsSubmitting(false);
    }
  };

  const paymentPending = orderResult?.paymentSession?.status === "requires_initiation" && orderResult.paymentMethod !== "COD";
  const stripeClientSecretReady =
    paymentInitiationResult?.handoffType === "client_secret" &&
    !!paymentInitiationResult.paymentToken &&
    !!paymentInitiationResult.publishableKey;
  const activePaymentRecovery = useMemo(() => readActivePaymentRecoveryContext(), []);

  if (orderResult) {
    return (
      <div className="space-y-8">
        <Button asChild type="button" variant="outline" size="sm" className="gap-2">
          <Link to="/products">
            <ArrowLeft className="h-4 w-4" />
            Continue shopping
          </Link>
        </Button>

        <Card className="border-emerald-200 bg-emerald-50/80 shadow-card">
          <CardContent className="space-y-6 p-6 md:p-8">
            <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
              <div className="flex items-start gap-4">
                <div className={`rounded-full p-3 text-white ${paymentPending ? "bg-amber-500" : "bg-emerald-500"}`}>
                  {paymentPending ? <CreditCard className="h-6 w-6" /> : <CheckCircle2 className="h-6 w-6" />}
                </div>

                <div className="space-y-2">
                  <p className={`text-xs font-semibold uppercase tracking-[0.18em] ${paymentPending ? "text-amber-700" : "text-emerald-700"}`}>
                    {paymentPending ? "Payment pending" : "Order placed"}
                  </p>
                  <h1 className="text-3xl font-black tracking-tight text-foreground">
                    {paymentPending ? "Order created, payment still required" : "Order placed successfully"}
                  </h1>
                  <p className={`max-w-2xl text-sm leading-6 ${paymentPending ? "text-amber-950/80" : "text-emerald-900/80"}`}>
                    {orderResult.message}
                  </p>
                </div>
              </div>

              <div className="rounded-[1.5rem] border border-emerald-200 bg-white/80 px-5 py-4 text-right">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Order number</p>
                <p className="text-2xl font-black tracking-tight text-foreground">{orderResult.orderNumber}</p>
                <p className="text-sm text-muted-foreground">{formatCurrency(orderResult.total, orderResult.currency)}</p>
              </div>
            </div>

            {orderResult.paymentSession ? (
              <div className={`rounded-[1.5rem] px-5 py-4 text-sm ${paymentPending ? "border border-amber-200 bg-amber-50 text-amber-950" : "border border-emerald-200 bg-white/80 text-emerald-950"}`}>
                <div className="flex items-start gap-3">
                  <CreditCard className="mt-0.5 h-4 w-4 shrink-0" />
                  <div className="space-y-1">
                    <p className="font-semibold">
                      {paymentPending ? "Payment handoff required" : "Payment session prepared"}
                    </p>
                    <p>
                      Merchant reference{" "}
                      {paymentInitiationResult?.providerReference ?? orderResult.paymentSession.merchantReference}.{" "}
                      {paymentPending
                        ? stripeClientSecretReady
                          ? "The order already exists in backend authority. Complete the embedded Stripe payment form below to continue the same authoritative order."
                          : "The order exists in backend authority, but payment is still pending. Use the payment action below to continue or retry the same authoritative order."
                        : `The backend created the order and prepared the ${getCheckoutPaymentMethodLabel(orderResult.paymentMethod)} payment handoff.`}
                    </p>
                    {paymentInitiationResult?.message ? <p>{paymentInitiationResult.message}</p> : null}
                  </div>
                </div>
              </div>
            ) : null}

            {stripeClientSecretReady ? (
              <EmbeddedStripePaymentForm
                orderNumber={orderResult.orderNumber}
                amount={paymentInitiationResult.amount}
                currency={paymentInitiationResult.currency}
                clientSecret={paymentInitiationResult.paymentToken!}
                publishableKey={paymentInitiationResult.publishableKey!}
                onPaymentPending={handleEmbeddedStripePending}
                onPaymentError={handleEmbeddedStripeError}
              />
            ) : null}

            {paymentInitiationError ? (
              <div className="rounded-[1.5rem] border border-rose-200 bg-rose-50 px-5 py-4 text-sm text-rose-950">
                <div className="flex items-start gap-3">
                  <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                  <div className="space-y-1">
                    <p className="font-semibold">Payment still pending</p>
                    <p>{paymentInitiationError}</p>
                  </div>
                </div>
              </div>
            ) : null}

            {orderResult.promoCode && orderResult.promoDiscountTotal > 0 ? (
              <div className="rounded-[1.5rem] border border-emerald-200 bg-white/80 px-5 py-4 text-sm text-emerald-950">
                <div className="flex items-start gap-3">
                  <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-700" />
                  <div className="space-y-1">
                    <p className="font-semibold">
                      Promo {orderResult.promoCode} applied. You saved {formatCurrency(orderResult.promoDiscountTotal, orderResult.currency)}
                    </p>
                    <p>Shopixy absorbed the discount against platform commission. Product totals and vendor payouts were left unchanged.</p>
                  </div>
                </div>
              </div>
            ) : null}

            <div className="flex flex-wrap gap-3">
              {paymentPending && !stripeClientSecretReady ? (
                <Button type="button" onClick={() => void handleRetryPayment()} disabled={isInitiatingPayment}>
                  {isInitiatingPayment ? "Preparing payment..." : "Continue to payment"}
                </Button>
              ) : null}
              {paymentPending ? (
                <Button asChild type="button" variant="outline">
                  <Link to={`/orders/${orderResult.marketplaceOrderId}/recover`}>Open payment recovery</Link>
                </Button>
              ) : null}
              <Button asChild type="button" variant="outline">
                <Link to="/orders">View all orders</Link>
              </Button>
              <Button asChild type="button" variant={paymentPending ? "outline" : "default"}>
                <Link to={`/orders/${orderResult.marketplaceOrderId}`}>Open order details</Link>
              </Button>
            </div>

            <div className="grid gap-4 lg:grid-cols-2">
              {orderResult.subOrders.map((subOrder) => (
                <div key={subOrder.subOrderId} className="rounded-[1.75rem] border border-white/80 bg-white/80 p-5">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Store sub-order</p>
                      <h2 className="mt-1 text-xl font-black tracking-tight text-foreground">{subOrder.storeName}</h2>
                      <p className="text-sm text-muted-foreground">{subOrder.orderNumber}</p>
                    </div>
                    <p className="text-lg font-bold text-foreground">{formatCurrency(subOrder.total, subOrder.currency)}</p>
                  </div>

                  <div className="mt-4 space-y-3">
                    {subOrder.items.map((item) => (
                      <div key={item.variantId} className="flex items-start justify-between gap-3 text-sm">
                        <div className="space-y-1">
                          <p className="font-medium text-foreground">{item.productName}</p>
                          {item.variantName ? <p className="text-xs text-muted-foreground">{item.variantName}</p> : null}
                          <p className="text-xs text-muted-foreground">Qty {item.quantity}</p>
                        </div>
                        <p className="whitespace-nowrap font-semibold text-foreground">{formatCurrency(item.total, subOrder.currency)}</p>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      </div>
    );
  }

  if (storeIds.length === 0) {
    return (
      <div className="space-y-6">
        {activePaymentRecovery ? (
          <Card className="border-sky-200 bg-sky-50/80 shadow-card">
            <CardContent className="space-y-4 p-6">
              <div className="space-y-2">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-sky-700">Pending payment recovery</p>
                <h2 className="text-2xl font-black tracking-tight text-foreground">{activePaymentRecovery.orderNumber} still has an active recovery path</h2>
                <p className="max-w-3xl text-sm leading-6 text-sky-950/80">
                  Your cart was already converted into an authoritative order. Use the recovery page to resume the same payment flow instead of creating another order.
                </p>
              </div>

              <div className="flex flex-wrap gap-3">
                <Button asChild type="button">
                  <Link to={`/orders/${activePaymentRecovery.orderId}/recover`}>Resume payment recovery</Link>
                </Button>
                <Button asChild type="button" variant="outline">
                  <Link to="/products">Keep shopping</Link>
                </Button>
              </div>
            </CardContent>
          </Card>
        ) : null}

        <EmptyState
          icon={<ShoppingBag className="h-6 w-6" />}
          title="Your cart is empty"
          description="Add products from Shopixy stores before heading to checkout."
          actionLabel="Return to cart"
          onAction={() => navigate("/cart")}
        />
      </div>
    );
  }

  return (
    <div className="space-y-8 pb-28 xl:pb-0">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="space-y-3">
          <Button asChild type="button" variant="outline" size="sm" className="gap-2">
            <Link to="/cart">
              <ArrowLeft className="h-4 w-4" />
              Back to cart
            </Link>
          </Button>

          <SectionHeading
            eyebrow="Checkout"
            title="Place one order across multiple stores"
            description={`You are checking out ${summary.itemCount} item${summary.itemCount === 1 ? "" : "s"} from ${summary.storeCount} store${summary.storeCount === 1 ? "" : "s"}. We will validate stock, recompute prices, pick the cheapest shipping per store, and present one final shipping total.`}
          />
        </div>
      </div>

      {apiError ? (
        <div className="rounded-[1.75rem] border border-rose-200 bg-rose-50 px-5 py-4 text-sm text-rose-900">
          <div className="flex items-start gap-3">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <div className="space-y-1">
              <p className="font-semibold">We could not place the order.</p>
              <p>{apiError}</p>
            </div>
          </div>
        </div>
      ) : null}

      {promoNotice ? (
        <div className="rounded-[1.75rem] border border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900">
          <div className="flex items-start gap-3">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <div className="space-y-1">
              <p className="font-semibold">Promo removed before checkout.</p>
              <p>{promoNotice}</p>
            </div>
          </div>
        </div>
      ) : null}

      {pricingError ? (
        <div className="rounded-[1.75rem] border border-rose-200 bg-rose-50 px-5 py-4 text-sm text-rose-900">
          <div className="flex items-start gap-3">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <div className="space-y-1">
              <p className="font-semibold">Cart pricing could not be recalculated.</p>
              <p>{pricingError}</p>
            </div>
          </div>
        </div>
      ) : null}

      {validation.issues.length > 0 || hasMissingListings ? (
        <div className="rounded-[1.75rem] border border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900">
          <div className="flex items-start gap-3">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <div className="space-y-1">
              <p className="font-semibold">Checkout is paused until the cart is valid.</p>
              <p>{hasMissingListings ? "One or more items need to be re-added to cart before checkout." : validation.issues[0]?.message}</p>
            </div>
          </div>
        </div>
      ) : null}

      {paymentAuthorityMessage ? (
        <div className="rounded-[1.75rem] border border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900">
          <div className="flex items-start gap-3">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <div className="space-y-1">
              <p className="font-semibold">Payment method unavailable for this multi-store order.</p>
              <p>{paymentAuthorityMessage}</p>
            </div>
          </div>
        </div>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_24rem] xl:items-start">
        <div className="space-y-5">
          {!isAuthenticated ? (
            <Card className="border-sky-200 bg-sky-50/80 shadow-card">
              <CardContent className="space-y-5 p-6 md:p-7">
                <div className="space-y-3">
                  <div className="inline-flex items-center gap-2 rounded-full bg-white/80 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-sky-700">
                    <UserRound className="h-4 w-4" />
                    Customer sign-in required
                  </div>
                  <h2 className="text-2xl font-black tracking-tight text-foreground">Sign in before checkout</h2>
                  <p className="text-sm leading-6 text-sky-950/80">
                    Shopixy now links every marketplace order to a customer account. Sign in or create one first, then we will bring you straight
                    back here to place the order.
                  </p>
                </div>

                <div className="grid gap-3 rounded-[1.5rem] border border-sky-200/70 bg-white/85 p-4 text-sm text-muted-foreground">
                  <div className="flex items-start gap-3">
                    <LogIn className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
                    <p>Your cart stays intact while you authenticate.</p>
                  </div>
                  <div className="flex items-start gap-3">
                    <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" />
                    <p>Order ownership and refund request access depend on a valid customer ID.</p>
                  </div>
                </div>

                <div className="flex flex-wrap gap-3">
                  <Button asChild type="button">
                    <Link to="/login" state={{ redirectTo: "/checkout" }}>
                      Sign in
                    </Link>
                  </Button>
                  <Button asChild type="button" variant="outline">
                    <Link to="/register" state={{ redirectTo: "/checkout" }}>
                      Create account
                    </Link>
                  </Button>
                </div>
              </CardContent>
            </Card>
          ) : (
            <>
              <CheckoutForm customer={customer} errors={fieldErrors} disabled={isSubmitting} onChange={handleFieldChange} />
              <PaymentMethodSelector
                value={paymentMethod}
                disabled={isSubmitting}
                paymentMethods={paymentMethods}
                onChange={setPaymentMethod}
              />
            </>
          )}
        </div>

        <CheckoutSummary
          cart={pricedCart}
          financialSummary={authoritativeFinancialSummary}
          paymentMethod={paymentMethod}
          promo={appliedPromo ? { code: appliedPromo.code, discountTotal: appliedPromo.discountTotal } : null}
          checkoutMessage={checkoutMessage}
          isSubmitting={isSubmitting}
          isCheckoutDisabled={isCheckoutDisabled}
          actionLabel={isAuthenticated ? undefined : "Sign In to Continue"}
          mobileActionLabel={isAuthenticated ? undefined : "Sign In"}
          onSubmit={handlePlaceOrder}
        />
      </div>
    </div>
  );
};
