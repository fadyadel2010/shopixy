import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, Clock3, CreditCard, MapPinned, PackageCheck } from "lucide-react";
import { useEffect, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";

import { normalizeApiError } from "@/api";
import {
  clearActivePaymentRecoveryContextIfMatches,
  persistActivePaymentRecoveryContext
} from "@/features/checkout/activePaymentRecoveryContext";
import { beginPaymentInitiationTrace } from "@/features/checkout/paymentInitiationTrace";
import { OrderStatusBadge, PaymentStatusBadge, formatCustomerDate } from "@/features/customer/status";
import { EmbeddedStripePaymentForm } from "@/payments/EmbeddedStripePaymentForm";
import { checkoutService, orderRecoveryService } from "@/services";
import { InlineErrorState } from "@/shared/InlineErrorState";
import { SectionHeading } from "@/shared/SectionHeading";
import { selectIsCustomerAuthenticated, useCustomerAuthStore, useToastStore } from "@/store";
import type { CheckoutPaymentMethod, CommerceHubPaymentInitiateResponse } from "@/types";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { formatCurrency, useDocumentTitle } from "@/utils";

export const OrderPaymentRecoveryPage = () => {
  const { id } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const showToast = useToastStore((state) => state.showToast);
  const isAuthenticated = useCustomerAuthStore(selectIsCustomerAuthenticated);
  const [paymentInitiationResult, setPaymentInitiationResult] = useState<CommerceHubPaymentInitiateResponse | null>(null);
  const [paymentInitiationError, setPaymentInitiationError] = useState<string | null>(null);
  const [isInitiatingPayment, setIsInitiatingPayment] = useState(false);
  const [orderAccessToken] = useState(() => {
    const token = searchParams.get("token");
    return token?.trim() || null;
  });

  useEffect(() => {
    if (!orderAccessToken || !searchParams.has("token")) {
      return;
    }

    const nextParams = new URLSearchParams(searchParams);
    nextParams.delete("token");
    setSearchParams(nextParams, { replace: true });
  }, [orderAccessToken, searchParams, setSearchParams]);

  const orderQuery = useQuery({
    queryKey: ["commercehub", "order-recovery", id, orderAccessToken ?? ""],
    queryFn: ({ signal }) => orderRecoveryService.getOrder(id ?? "", orderAccessToken, signal),
    enabled: Boolean(id)
  });

  useEffect(() => {
    const order = orderQuery.data;
    if (!order) {
      return;
    }

    if (order.canRetryPayment || order.paymentRecoveryMessage) {
      persistActivePaymentRecoveryContext({
        orderId: order.marketplaceOrderId,
        orderNumber: order.orderNumber
      });
      return;
    }

    clearActivePaymentRecoveryContextIfMatches(order.marketplaceOrderId);
  }, [orderQuery.data]);

  useEffect(() => {
    setPaymentInitiationResult(null);
    setPaymentInitiationError(null);
    setIsInitiatingPayment(false);
  }, [id]);

  useDocumentTitle(orderQuery.data ? `${orderQuery.data.orderNumber} | CommerceHub` : "Payment Recovery | CommerceHub");

  if (!id) {
    return <InlineErrorState title="Invalid recovery link" message="This order recovery link is incomplete." />;
  }

  if (orderQuery.isLoading) {
    return (
      <div className="space-y-4">
        {[0, 1, 2].map((item) => (
          <Card key={item} className="border-white/70 bg-white/90">
            <CardContent className="space-y-4 p-6">
              <div className="h-4 w-24 rounded-full bg-slate-200" />
              <div className="h-8 w-64 rounded-full bg-slate-200" />
              <div className="h-4 w-full rounded-full bg-slate-200" />
              <div className="h-4 w-3/4 rounded-full bg-slate-200" />
            </CardContent>
          </Card>
        ))}
      </div>
    );
  }

  if (orderQuery.isError || !orderQuery.data) {
    const message = normalizeApiError(orderQuery.error).message;
    return (
      <InlineErrorState
        title="Recovery unavailable"
        message={message}
        onRetry={() => void orderQuery.refetch()}
      />
    );
  }

  const order = orderQuery.data;
  const stripeClientSecretReady =
    paymentInitiationResult?.handoffType === "client_secret" &&
    !!paymentInitiationResult.paymentToken &&
    !!paymentInitiationResult.publishableKey;

  const handleContinuePayment = async () => {
    const trace = beginPaymentInitiationTrace("payment-recovery", order.marketplaceOrderId);
    setIsInitiatingPayment(true);
    setPaymentInitiationError(null);

    try {
      // The recovery URL/token only locates an order. Current retry authority must
      // be fetched again immediately before requesting a provider handoff.
      trace.mark("order-refetch-started");
      const refreshedOrder = await orderQuery.refetch();
      const currentOrder = refreshedOrder.data;
      trace.mark("order-refetch-completed", {
        isError: refreshedOrder.isError,
        hasOrder: Boolean(currentOrder),
        canRetryPayment: currentOrder?.canRetryPayment ?? null,
        currentOrderId: currentOrder?.marketplaceOrderId ?? null
      });
      if (refreshedOrder.isError || !currentOrder?.canRetryPayment) {
        trace.mark("retry-authority-rejected");
        throw new Error("Payment retry is no longer authorized for this order.");
      }

      const provider = currentOrder.paymentMethodCode as Exclude<CheckoutPaymentMethod, "COD">;
      trace.mark("checkout-service-entered", {
        orderId: currentOrder.marketplaceOrderId,
        provider,
        amount: currentOrder.total,
        currency: currentOrder.currency
      });
      const initiationResponse = await checkoutService.initiatePayment({
        orderId: currentOrder.marketplaceOrderId,
        provider
      }, currentOrder.total, currentOrder.currency);
      trace.mark("initiation-authority-accepted", {
        paymentId: initiationResponse.paymentId,
        handoffType: initiationResponse.handoffType,
        provider: initiationResponse.provider,
        isReusable: initiationResponse.isReusable
      });

      setPaymentInitiationResult(initiationResponse);

      if (initiationResponse.handoffType === "redirect_url" && initiationResponse.paymentUrl) {
        showToast({
          title: initiationResponse.isReusable ? "Resuming payment" : "Redirecting to payment",
          description: `${currentOrder.orderNumber} is waiting for payment confirmation.`,
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
          title: "Enter payment details",
          description: `${currentOrder.orderNumber} is waiting for Stripe payment confirmation.`,
          tone: "success"
        });
        return;
      }

      const message = initiationResponse.message || "Payment initiation succeeded, but the provider handoff details are still unavailable.";
      setPaymentInitiationError(message);
      showToast({
        title: "Payment still pending",
        description: message,
        tone: "error"
      });
    } catch (error) {
      const api = normalizeApiError(error);
      trace.mark("handler-failed", {
        status: api.status,
        code: api.code ?? null,
        message: api.message
      });
      setPaymentInitiationError(api.message);
      showToast({
        title: "Payment initiation failed",
        description: api.message,
        tone: "error"
      });
    } finally {
      trace.finish();
      setIsInitiatingPayment(false);
    }
  };

  const handleEmbeddedStripePending = (message: string) => {
    setPaymentInitiationError(null);
    showToast({
      title: "Payment submitted",
      description: message,
      tone: "success"
    });
    void orderQuery.refetch();
  };

  const handleEmbeddedStripeError = (message: string) => {
    showToast({
      title: "Stripe payment failed",
      description: message,
      tone: "error"
    });
  };

  return (
    <div className="space-y-8">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="space-y-3">
          <Button asChild type="button" variant="outline" size="sm" className="gap-2">
            <Link to="/checkout">
              <ArrowLeft className="h-4 w-4" />
              Back to checkout
            </Link>
          </Button>

          <SectionHeading
            eyebrow="Payment Recovery"
            title={order.orderNumber}
            description="This recovery view follows backend payment authority and keeps the same order in place while payment is resumed or held for reconciliation."
          />
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <OrderStatusBadge status={order.status} />
          <PaymentStatusBadge status={order.paymentStatus} />
        </div>
      </div>

      {order.canRetryPayment ? (
        <Card className="border-sky-200 bg-sky-50/80 shadow-card">
          <CardContent className="space-y-4 p-5 md:p-6">
            <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
              <div className="space-y-1">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-sky-700">Payment Pending</p>
                <h2 className="text-2xl font-black tracking-tight text-foreground">Continue the same authoritative order payment</h2>
                <p className="max-w-3xl text-sm leading-6 text-sky-950/80">
                  This order already exists in backend authority. Continue the pending {order.paymentMethodName} payment without creating a new order.
                </p>
              </div>

              {!stripeClientSecretReady ? (
                <Button type="button" onClick={() => void handleContinuePayment()} disabled={isInitiatingPayment}>
                  {isInitiatingPayment ? "Preparing payment..." : "Continue to payment"}
                </Button>
              ) : null}
            </div>

            {paymentInitiationError ? (
              <div className="rounded-[1.25rem] border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-950">
                {paymentInitiationError}
              </div>
            ) : null}

            {stripeClientSecretReady ? (
              <EmbeddedStripePaymentForm
                orderNumber={order.orderNumber}
                amount={paymentInitiationResult.amount}
                currency={paymentInitiationResult.currency}
                clientSecret={paymentInitiationResult.paymentToken!}
                publishableKey={paymentInitiationResult.publishableKey!}
                onPaymentPending={handleEmbeddedStripePending}
                onPaymentError={handleEmbeddedStripeError}
              />
            ) : null}
          </CardContent>
        </Card>
      ) : null}

      {!order.canRetryPayment && order.paymentRecoveryMessage ? (
        <Card className="border-amber-200 bg-amber-50/80 shadow-card">
          <CardContent className="space-y-3 p-5 md:p-6">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-amber-700">Payment Action Unavailable</p>
            <h2 className="text-2xl font-black tracking-tight text-foreground">Payment cannot continue yet</h2>
            <p className="max-w-3xl text-sm leading-6 text-amber-950/85">{order.paymentRecoveryMessage}</p>
          </CardContent>
        </Card>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1.15fr)_minmax(18rem,0.85fr)]">
        <Card className="border-white/70 bg-white/92">
          <CardContent className="space-y-6 p-6 md:p-7">
            <div className="grid gap-4 md:grid-cols-3">
              <div className="rounded-[1.5rem] border border-border/70 bg-slate-50/80 px-4 py-4">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Placed</p>
                <p className="mt-2 text-lg font-black tracking-tight text-foreground">{formatCustomerDate(order.createdAt)}</p>
              </div>

              <div className="rounded-[1.5rem] border border-border/70 bg-slate-50/80 px-4 py-4">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Payment</p>
                <p className="mt-2 text-lg font-black tracking-tight text-foreground">{order.paymentMethodName}</p>
                <p className="text-sm text-muted-foreground">{order.paymentStatus}</p>
              </div>

              <div className="rounded-[1.5rem] border border-border/70 bg-slate-50/80 px-4 py-4">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Total</p>
                <p className="mt-2 text-lg font-black tracking-tight text-foreground">{formatCurrency(order.total, order.currency)}</p>
                <p className="text-sm text-muted-foreground">{order.subOrders.length} vendor group{order.subOrders.length === 1 ? "" : "s"}</p>
              </div>
            </div>

            <div className="space-y-4">
              {order.subOrders.map((subOrder) => (
                <div key={subOrder.subOrderId} className="rounded-[1.75rem] border border-border/70 bg-slate-50/70 p-5">
                  <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
                    <div className="space-y-1">
                      <h2 className="text-xl font-black tracking-tight text-foreground">{subOrder.storeName}</h2>
                      <p className="text-sm text-muted-foreground">{subOrder.orderNumber}</p>
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                      <OrderStatusBadge status={subOrder.status} />
                      <PaymentStatusBadge status={subOrder.paymentStatus} />
                    </div>
                  </div>

                  <div className="mt-4 grid gap-3">
                    {subOrder.items.map((item) => (
                      <div key={item.variantId} className="rounded-[1.25rem] border border-white/80 bg-white/90 px-4 py-4">
                        <div className="flex items-start justify-between gap-3">
                          <div className="space-y-1">
                            <p className="font-semibold text-foreground">{item.productName}</p>
                            {item.variantName ? <p className="text-sm text-muted-foreground">{item.variantName}</p> : null}
                            <p className="text-sm text-muted-foreground">
                              Qty {item.quantity} at {formatCurrency(item.unitPrice, order.currency)}
                            </p>
                          </div>

                          <span className="text-base font-bold text-foreground">{formatCurrency(item.total, order.currency)}</span>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>

        <div className="space-y-4">
          <Card className="border-white/70 bg-white/92">
            <CardContent className="space-y-4 p-5">
              <div className="flex items-center gap-3">
                <MapPinned className="h-5 w-5 text-primary" />
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Delivery</p>
                  <h3 className="text-xl font-black tracking-tight text-foreground">{order.fullName}</h3>
                </div>
              </div>

              <div className="space-y-2 text-sm leading-6 text-muted-foreground">
                <p>{order.phoneNumber}</p>
                <p>{order.addressLine}</p>
                <p>
                  {order.city}
                  {order.area ? `, ${order.area}` : ""}
                </p>
              </div>
            </CardContent>
          </Card>

          <Card className="border-white/70 bg-white/92">
            <CardContent className="space-y-4 p-5">
              <div className="flex items-center gap-3">
                <CreditCard className="h-5 w-5 text-primary" />
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Charges</p>
                  <h3 className="text-xl font-black tracking-tight text-foreground">Order breakdown</h3>
                </div>
              </div>

              <div className="grid gap-3 text-sm">
                <div className="flex items-center justify-between gap-3">
                  <span className="text-muted-foreground">Subtotal</span>
                  <span className="font-semibold text-foreground">{formatCurrency(order.subtotal, order.currency)}</span>
                </div>
                <div className="flex items-center justify-between gap-3">
                  <span className="text-muted-foreground">Shipping</span>
                  <span className="font-semibold text-foreground">{formatCurrency(order.shipping, order.currency)}</span>
                </div>
                <div className="h-px bg-border/70" />
                <div className="flex items-center justify-between gap-3">
                  <span className="font-semibold text-foreground">Total</span>
                  <span className="text-xl font-black tracking-tight text-foreground">{formatCurrency(order.total, order.currency)}</span>
                </div>
              </div>

              {order.paymentReference ? (
                <div className="rounded-[1.25rem] border border-border/70 bg-slate-50/80 px-4 py-3 text-sm text-muted-foreground">
                  <div className="flex items-center gap-2 font-semibold text-foreground">
                    <CreditCard className="h-4 w-4 text-primary" />
                    Payment reference
                  </div>
                  <p className="mt-1 break-all">{order.paymentReference}</p>
                </div>
              ) : null}
            </CardContent>
          </Card>

          <Card className="border-white/70 bg-white/92">
            <CardContent className="space-y-4 p-5">
              <div className="flex items-center gap-3">
                <Clock3 className="h-5 w-5 text-primary" />
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Recovery state</p>
                  <h3 className="text-xl font-black tracking-tight text-foreground">Authority notes</h3>
                </div>
              </div>

              <div className="rounded-[1.25rem] border border-border/70 bg-slate-50/80 px-4 py-4 text-sm leading-6 text-muted-foreground">
                <div className="flex items-start gap-3">
                  <div className="mt-1 rounded-full bg-primary/10 p-2 text-primary">
                    <PackageCheck className="h-4 w-4" />
                  </div>
                  <div className="space-y-2">
                    <p>The backend will either resume the same provider session or block retries when reconciliation is required.</p>
                    <p>Last order update: {formatCustomerDate(order.updatedAt)}</p>
                    {order.paidAt ? <p>Paid at: {formatCustomerDate(order.paidAt)}</p> : null}
                  </div>
                </div>
              </div>

              <div className="flex flex-wrap gap-3">
                {isAuthenticated ? (
                  <>
                    <Button asChild type="button" variant="outline">
                      <Link to={`/orders/${order.marketplaceOrderId}`}>Open full order details</Link>
                    </Button>
                    <Button asChild type="button" variant="outline">
                      <Link to="/orders">View all orders</Link>
                    </Button>
                  </>
                ) : (
                  <Button asChild type="button" variant="outline">
                    <Link to="/products">Continue shopping</Link>
                  </Button>
                )}
              </div>
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
};
