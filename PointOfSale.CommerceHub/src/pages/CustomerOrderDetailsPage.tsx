import {
  clearActivePaymentRecoveryContextIfMatches,
  persistActivePaymentRecoveryContext
} from "@/features/checkout/activePaymentRecoveryContext";
import {
  ArrowLeft,
  Clock3,
  CreditCard,
  MapPinned,
  PackageCheck,
  ReceiptText,
  Truck
} from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "react-router-dom";

import { normalizeApiError } from "@/api";
import { EmbeddedStripePaymentForm } from "@/payments/EmbeddedStripePaymentForm";
import {
  formatRefundPayoutMethodLabel,
  isDevelopmentRefundSimulation
} from "@/features/customer/payout-details";
import { RefundRequestModal } from "@/features/customer/components/RefundRequestModal";
import { RefundEvidenceDialog, formatRefundReasonLabel } from "@/features/customer/refund-evidence";
import { customerOrderDetailsQueryOptions } from "@/features/customer/queries";
import { ItemStatusBadge, OrderStatusBadge, PaymentStatusBadge, RefundStatusBadge, formatCustomerDate } from "@/features/customer/status";
import { beginPaymentInitiationTrace } from "@/features/checkout/paymentInitiationTrace";
import { checkoutService } from "@/services";
import { InlineErrorState } from "@/shared/InlineErrorState";
import { SectionHeading } from "@/shared/SectionHeading";
import { useToastStore } from "@/store";
import type { CheckoutPaymentMethod, CommerceHubPaymentInitiateResponse, CustomerOrderItem, CustomerOrderItemExistingRefund } from "@/types";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { formatCurrency, useDocumentTitle } from "@/utils";

const getRefundStatusMessage = (
  status: string,
  isDevelopmentSimulation: boolean,
  externalMoneyMovementConfirmed: boolean
) => {
  if (status === "AwaitingPayout" || status === "ReadyForExecution") {
    return {
      tone: "border-amber-200 bg-amber-50 text-amber-950",
      message: "Your refund has been approved and is being processed. The payment will be sent shortly."
    };
  }

  if (status === "Paid" || status === "Executed" || status === "Completed") {
    return {
      tone: isDevelopmentSimulation
        ? "border-blue-200 bg-blue-50 text-blue-950"
        : externalMoneyMovementConfirmed
          ? "border-emerald-200 bg-emerald-50 text-emerald-950"
          : "border-amber-200 bg-amber-50 text-amber-950",
      message: isDevelopmentSimulation
        ? "Development simulation recorded successfully. No external money movement occurred."
        : externalMoneyMovementConfirmed
          ? "Your refund has been sent successfully."
          : "Your refund was recorded, but external money movement has not been confirmed."
    };
  }

  return null;
};

export const CustomerOrderDetailsPage = () => {
  const { id } = useParams();
  const showToast = useToastStore((state) => state.showToast);
  const [activeRefundItem, setActiveRefundItem] = useState<CustomerOrderItem | null>(null);
  const [activeEvidenceRefund, setActiveEvidenceRefund] = useState<{
    title: string;
    refund: CustomerOrderItemExistingRefund;
  } | null>(null);
  const [paymentInitiationResult, setPaymentInitiationResult] = useState<CommerceHubPaymentInitiateResponse | null>(null);
  const [paymentInitiationError, setPaymentInitiationError] = useState<string | null>(null);
  const [isInitiatingPayment, setIsInitiatingPayment] = useState(false);
  const orderQuery = useQuery(customerOrderDetailsQueryOptions(id ?? ""));
  const previousOrderIdRef = useRef<string | null>(null);

  // Clear previous order's recovery context when route changes
  useEffect(() => {
    const currentOrderId = orderQuery.data?.orderId;
    const previousOrderId = previousOrderIdRef.current;

    // If route changed and we had a previous order, clear its context
    if (previousOrderId && previousOrderId !== currentOrderId) {
      clearActivePaymentRecoveryContextIfMatches(previousOrderId);
    }

    // Update ref to track current order
    if (currentOrderId) {
      previousOrderIdRef.current = currentOrderId;
    }
  }, [id, orderQuery.data?.orderId]);

  // Reset payment state when order ID changes to prevent stale data from carrying over
  useEffect(() => {
    setPaymentInitiationResult(null);
    setPaymentInitiationError(null);
    setIsInitiatingPayment(false);
  }, [id]);

  useEffect(() => {
    const order = orderQuery.data;
    if (!order) {
      return;
    }

    if (order.canRetryPayment || order.paymentRecoveryMessage) {
      persistActivePaymentRecoveryContext({
        orderId: order.orderId,
        orderNumber: order.orderNumber
      });
      return;
    }

    clearActivePaymentRecoveryContextIfMatches(order.orderId);
  }, [orderQuery.data]);

  useEffect(() => {
    if (!activeRefundItem || !orderQuery.data) {
      return;
    }

    const currentItem = orderQuery.data.subOrders
      .flatMap((subOrder) => subOrder.items)
      .find((item) => item.orderItemId === activeRefundItem.orderItemId);

    if (!currentItem?.canRefund) {
      setActiveRefundItem(null);
    }
  }, [activeRefundItem, orderQuery.data]);

  useDocumentTitle(orderQuery.data ? `${orderQuery.data.orderNumber} | CommerceHub` : "Order Details | CommerceHub");

  if (!id) {
    return <InlineErrorState title="Invalid order" message="This customer order link is incomplete." />;
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
        title="Order details unavailable"
        message={message}
        onRetry={() => void orderQuery.refetch()}
      />
    );
  }

  const order = orderQuery.data;
  const currentActiveRefundItem = activeRefundItem
    ? order.subOrders
        .flatMap((subOrder) => subOrder.items)
        .find((item) => item.orderItemId === activeRefundItem.orderItemId) ?? null
    : null;
  const isRetryableOnlinePayment = order.canRetryPayment;
  const stripeClientSecretReady =
    paymentInitiationResult?.handoffType === "client_secret" &&
    !!paymentInitiationResult.paymentToken &&
    !!paymentInitiationResult.publishableKey;

  const handleContinuePayment = async () => {
    const trace = beginPaymentInitiationTrace("customer-order-details", order.orderId);
    setIsInitiatingPayment(true);
    setPaymentInitiationError(null);

    try {
      // A rendered query result is presentation only. Re-enter the current backend
      // order authority at execution time so a stale tab cannot retain retry power.
      trace.mark("order-refetch-started");
      const refreshedOrder = await orderQuery.refetch();
      const currentOrder = refreshedOrder.data;
      trace.mark("order-refetch-completed", {
        isError: refreshedOrder.isError,
        hasOrder: Boolean(currentOrder),
        canRetryPayment: currentOrder?.canRetryPayment ?? null,
        currentOrderId: currentOrder?.orderId ?? null
      });
      if (refreshedOrder.isError || !currentOrder?.canRetryPayment) {
        trace.mark("retry-authority-rejected");
        throw new Error("Payment retry is no longer authorized for this order.");
      }

      const provider = currentOrder.paymentMethodCode as Exclude<CheckoutPaymentMethod, "COD">;
      trace.mark("checkout-service-entered", {
        orderId: currentOrder.orderId,
        provider,
        amount: currentOrder.totalAmount,
        currency: currentOrder.currency
      });
      const initiationResponse = await checkoutService.initiatePayment({
        orderId: currentOrder.orderId,
        provider
      }, currentOrder.totalAmount, currentOrder.currency);
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

  const resolveCurrentRefundItem = async (orderItemId: string) => {
    const refreshedOrder = await orderQuery.refetch();
    const currentOrder = refreshedOrder.data;
    if (refreshedOrder.isError || !currentOrder) {
      return null;
    }

    return currentOrder.subOrders
      .flatMap((subOrder) => subOrder.items)
      .find((item) => item.orderItemId === orderItemId) ?? null;
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
            <Link to="/orders">
              <ArrowLeft className="h-4 w-4" />
              Back to orders
            </Link>
          </Button>

          <SectionHeading
            eyebrow="Order Details"
            title={order.orderNumber}
            description="Vendor shipments, item states, and payment context all stay visible under your customer account."
          />
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <OrderStatusBadge status={order.status} />
          <PaymentStatusBadge status={order.paymentStatus} />
        </div>
      </div>

      {isRetryableOnlinePayment ? (
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

      {!isRetryableOnlinePayment && order.paymentRecoveryMessage ? (
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
                <p className="mt-2 text-lg font-black tracking-tight text-foreground">{formatCurrency(order.totalAmount, order.currency)}</p>
                <p className="text-sm text-muted-foreground">{order.subOrders.length} vendor group{order.subOrders.length === 1 ? "" : "s"}</p>
              </div>
            </div>

            <div className="space-y-4">
              {order.subOrders.map((subOrder) => (
                <div key={subOrder.subOrderId} className="rounded-[1.75rem] border border-border/70 bg-slate-50/70 p-5">
                  <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
                    <div className="space-y-1">
                      <h2 className="text-xl font-black tracking-tight text-foreground">{subOrder.vendorName}</h2>
                      <p className="text-sm text-muted-foreground">{subOrder.orderNumber}</p>
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                      <OrderStatusBadge status={subOrder.status} />
                      <PaymentStatusBadge status={subOrder.paymentStatus} />
                    </div>
                  </div>

                  <div className="mt-4 grid gap-3">
                    {subOrder.items.map((item) => (
                      <div key={item.orderItemId} className="rounded-[1.25rem] border border-white/80 bg-white/90 px-4 py-4">
                        <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
                          <div className="space-y-1">
                            <p className="font-semibold text-foreground">{item.productName}</p>
                            {item.variant ? <p className="text-sm text-muted-foreground">{item.variant}</p> : null}
                            <p className="text-sm text-muted-foreground">
                              Qty {item.quantity} at {formatCurrency(item.price, order.currency)}
                            </p>
                            <p className="text-sm text-muted-foreground">
                              Max refundable now: {item.maxRefundableQuantity}
                            </p>
                          </div>

                          <div className="flex flex-wrap items-center gap-2">
                            <ItemStatusBadge status={item.status} />
                            <span title={!item.canRefund ? item.refundDisabledReason ?? "Refund unavailable for this item." : undefined}>
                              <Button
                                type="button"
                                variant="outline"
                                size="sm"
                                disabled={!item.canRefund}
                                onClick={() => setActiveRefundItem(item)}
                              >
                                Request Refund
                              </Button>
                            </span>
                            <span className="text-base font-bold text-foreground">{formatCurrency(item.total, order.currency)}</span>
                          </div>
                        </div>

                        {item.existingRefunds.length > 0 ? (
                          <div className="mt-4 space-y-2 border-t border-border/70 pt-4">
                            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Existing refund requests</p>
                            {item.existingRefunds.map((refund) => {
                              const isDevelopmentSimulation = isDevelopmentRefundSimulation(refund.payout);
                               const refundStatusMessage = getRefundStatusMessage(
                                 refund.status,
                                 isDevelopmentSimulation,
                                 refund.payout?.externalMoneyMovementConfirmed === true
                               );

                              return (
                                <div key={refund.refundId} className="rounded-[1rem] border border-border/70 bg-slate-50/80 px-3 py-3">
                                  <div className="flex flex-col gap-2 md:flex-row md:items-start md:justify-between">
                                    <div className="space-y-1">
                                      <p className="text-sm font-semibold text-foreground">
                                        Qty {refund.quantity} - {formatCurrency(refund.amount, order.currency)}
                                      </p>
                                      <p className="text-sm font-semibold text-foreground">{formatRefundReasonLabel(refund.reasonCode)}</p>
                                      {refund.reasonText ? <p className="text-sm leading-6 text-muted-foreground">{refund.reasonText}</p> : null}
                                      {refundStatusMessage ? (
                                        <div className={`rounded-[1rem] border px-3 py-3 text-sm ${refundStatusMessage.tone}`}>
                                          {refundStatusMessage.message}
                                        </div>
                                      ) : null}
                                      {refund.payout ? (
                                        <div className="rounded-[1rem] border border-white/70 bg-white px-3 py-3 text-sm">
                                          {isDevelopmentSimulation ? (
                                            <>
                                              <p className="text-xs font-semibold uppercase tracking-[0.16em] text-blue-700">
                                                Development refund simulation
                                              </p>
                                              <p className="mt-1 text-xs font-semibold text-blue-900">
                                                Internal refund recognition only. No external money movement occurred.
                                              </p>
                                            </>
                                          ) : null}
                                          <p className="font-semibold text-foreground">
                                            {formatRefundPayoutMethodLabel(refund.payout.method)}
                                          </p>
                                          <p className="text-muted-foreground">{refund.payout.maskedAccountIdentifier}</p>
                                          {refund.payout.transactionReference ? (
                                            <>
                                              <p className="mt-2 text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                                                {isDevelopmentSimulation ? "Simulation reference" : "Transaction Ref"}
                                              </p>
                                              <p className="mt-1 break-all text-sm font-medium text-foreground">{refund.payout.transactionReference}</p>
                                            </>
                                          ) : null}
                                          {refund.payout.paidAt ? (
                                            <p className="mt-2 text-xs text-muted-foreground">
                                               {isDevelopmentSimulation
                                                 ? "Simulation recorded at"
                                                 : refund.payout.externalMoneyMovementConfirmed
                                                   ? "Paid at"
                                                   : "Recorded at"}{" "}
                                              {formatCustomerDate(refund.payout.paidAt)}
                                            </p>
                                          ) : null}
                                        </div>
                                      ) : null}
                                      {refund.media[0] ? (
                                        <div className="mt-3 overflow-hidden rounded-[1rem] border border-white/70 bg-white">
                                          {refund.media[0].mediaType === "video" ? (
                                            <video
                                              src={refund.media[0].url}
                                              className="h-28 w-full object-cover"
                                              controls
                                              preload="metadata"
                                            />
                                          ) : (
                                            <img src={refund.media[0].url} alt={refund.media[0].fileName} className="h-28 w-full object-cover" />
                                          )}
                                        </div>
                                      ) : null}
                                    </div>

                                    <div className="flex flex-wrap items-center gap-2">
                                      <RefundStatusBadge status={refund.status} />
                                      {refund.media.length > 0 ? (
                                        <Button
                                          type="button"
                                          variant="outline"
                                          size="sm"
                                          onClick={() =>
                                            setActiveEvidenceRefund({
                                              title: item.productName,
                                              refund
                                            })
                                          }
                                        >
                                          View evidence
                                        </Button>
                                      ) : null}
                                      <span className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                                        {formatCustomerDate(refund.createdAt)}
                                      </span>
                                    </div>
                                  </div>
                                </div>
                              );
                            })}
                          </div>
                        ) : null}
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
                <ReceiptText className="h-5 w-5 text-primary" />
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
                  <span className="font-semibold text-foreground">{formatCurrency(order.shippingTotal, order.currency)}</span>
                </div>
                <div className="h-px bg-border/70" />
                <div className="flex items-center justify-between gap-3">
                  <span className="font-semibold text-foreground">Total</span>
                  <span className="text-xl font-black tracking-tight text-foreground">{formatCurrency(order.totalAmount, order.currency)}</span>
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
                  <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Timeline</p>
                  <h3 className="text-xl font-black tracking-tight text-foreground">Order activity</h3>
                </div>
              </div>

              <div className="space-y-3">
                {order.timeline.length === 0 ? (
                  <p className="text-sm text-muted-foreground">Timeline updates will appear here as the order moves.</p>
                ) : (
                  order.timeline.map((entry) => (
                    <div key={`${entry.at}:${entry.type}:${entry.vendorName ?? "order"}`} className="rounded-[1.25rem] border border-border/70 bg-slate-50/80 px-4 py-4">
                      <div className="flex items-start gap-3">
                        <div className="mt-1 rounded-full bg-primary/10 p-2 text-primary">
                          {entry.type === "OrderCreated" ? <PackageCheck className="h-4 w-4" /> : <Truck className="h-4 w-4" />}
                        </div>

                        <div className="space-y-1">
                          <p className="text-sm font-semibold text-foreground">{entry.vendorName ?? "Marketplace order"}</p>
                          <p className="text-sm leading-6 text-muted-foreground">{entry.description}</p>
                          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">{formatCustomerDate(entry.at)}</p>
                        </div>
                      </div>
                    </div>
                  ))
                )}
              </div>
            </CardContent>
          </Card>
        </div>
      </div>

      <RefundRequestModal
        currency={order.currency}
        item={currentActiveRefundItem}
        open={currentActiveRefundItem != null}
        onClose={() => setActiveRefundItem(null)}
        resolveCurrentItem={resolveCurrentRefundItem}
      />

      <RefundEvidenceDialog
        open={activeEvidenceRefund != null}
        title={activeEvidenceRefund?.title ?? "Refund evidence"}
        media={activeEvidenceRefund?.refund.media ?? []}
        reasonCode={activeEvidenceRefund?.refund.reasonCode}
        reasonText={activeEvidenceRefund?.refund.reasonText}
        onClose={() => setActiveEvidenceRefund(null)}
      />
    </div>
  );
};
