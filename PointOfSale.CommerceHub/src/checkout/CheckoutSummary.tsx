import { memo, useMemo } from "react";
import { AlertTriangle, ArrowRight, CreditCard, LoaderCircle, PackageCheck, Store, TicketPercent, Truck } from "lucide-react";

import type { CartSummary as CartSummaryData, CheckoutCartPayload, CheckoutPaymentMethod } from "@/types";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { formatCurrency } from "@/utils";
import { sanitizePriceSnapshot } from "@/utils/price";

type CheckoutSummaryProps = {
  cart: CheckoutCartPayload;
  financialSummary: CartSummaryData | null;
  paymentMethod: CheckoutPaymentMethod;
  promo?: {
    code: string;
    discountTotal: number;
  } | null;
  checkoutMessage: string;
  isSubmitting: boolean;
  isCheckoutDisabled: boolean;
  actionLabel?: string;
  mobileActionLabel?: string;
  onSubmit: () => void;
};

const getVariantDescription = (variantLabel?: string, variantAttributes?: { name: string; value: string }[]) => {
  if (variantLabel?.trim()) {
    return variantLabel;
  }

  if (!variantAttributes || variantAttributes.length === 0) {
    return null;
  }

  return variantAttributes.map((attribute) => `${attribute.name}: ${attribute.value}`).join(", ");
};

const getItemTotal = (priceSnapshot: number, quantity: number) => {
  if (!Number.isFinite(priceSnapshot) || !Number.isFinite(quantity)) {
    return 0;
  }

  return sanitizePriceSnapshot(priceSnapshot) * Math.max(0, Math.floor(quantity));
};

const CheckoutSummaryComponent = ({
  cart,
  financialSummary,
  paymentMethod,
  promo,
  checkoutMessage,
  isSubmitting,
  isCheckoutDisabled,
  actionLabel,
  mobileActionLabel,
  onSubmit
}: CheckoutSummaryProps) => {
  const financialValue = (value: number | undefined) =>
    financialSummary && value !== undefined ? formatCurrency(value, financialSummary.currency) : "--";
  const storeFinancialValue = (value: number | undefined, currency: string) =>
    financialSummary && value !== undefined ? formatCurrency(value, financialSummary.currency || currency) : "--";
  const resolvedActionLabel = actionLabel ?? (paymentMethod === "COD" ? "Place Order" : "Place Order & Prepare Payment");
  const paymentLabel =
    paymentMethod === "COD"
      ? "Cash on Delivery"
      : paymentMethod === "STRIPE"
        ? "Stripe"
        : "Paymob";

  const storeLabel = useMemo(
    () => `${cart.summary.storeCount} store${cart.summary.storeCount === 1 ? "" : "s"}`,
    [cart.summary.storeCount]
  );

  return (
    <>
      <Card className="hidden border-white/70 bg-white/95 xl:sticky xl:top-28 xl:block">
        <CardContent className="space-y-6 p-6">
          <div className="space-y-2">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Order Summary</p>
            <h2 className="text-2xl font-black tracking-tight text-foreground">Multi-vendor checkout</h2>
            <p className="text-sm leading-6 text-muted-foreground">
              One customer order, multiple store sub-orders, and one aggregated shipping total across all vendors.
            </p>
          </div>

          <div className="space-y-4">
            {cart.stores.map((store) => (
              <div key={store.storeId} className="rounded-[1.5rem] border border-border/70 bg-slate-50/70 p-4">
                <div className="flex items-start justify-between gap-3">
                  <div className="space-y-1">
                    <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
                      <Store className="h-4 w-4 text-primary" />
                      {store.storeName}
                    </div>
                    <p className="text-xs text-muted-foreground">{store.items.length} line{store.items.length === 1 ? "" : "s"} in this sub-order</p>
                  </div>
                  <p className="text-sm font-semibold text-foreground">{storeFinancialValue(store.total, store.currency)}</p>
                </div>

                <div className="mt-4 space-y-3">
                  {store.items.map((item) => {
                    const variantDescription = getVariantDescription(item.variantLabel, item.variantAttributes);
                    const itemTotal = getItemTotal(item.priceSnapshot, item.quantity);

                    return (
                      <div key={item.variantId} className="flex items-start justify-between gap-3 text-sm">
                        <div className="min-w-0 space-y-1">
                          <p className="font-medium text-foreground">{item.name}</p>
                          {variantDescription ? <p className="text-xs text-muted-foreground">{variantDescription}</p> : null}
                          <p className="text-xs text-muted-foreground">Qty {item.quantity}</p>
                        </div>
                        <p className="whitespace-nowrap font-semibold text-foreground">{storeFinancialValue(itemTotal, store.currency)}</p>
                      </div>
                    );
                  })}
                </div>

                <div className="mt-4 border-t border-border/70 pt-3 text-sm">
                  <div className="flex items-center justify-between gap-3">
                    <span className="text-muted-foreground">Store subtotal</span>
                    <span className="font-semibold text-foreground">{storeFinancialValue(store.subtotal, store.currency)}</span>
                  </div>
                  <div className="mt-2 flex items-center justify-between gap-3">
                    <span className="text-muted-foreground">Store shipping</span>
                    <span className="font-semibold text-foreground">{storeFinancialValue(store.shipping, store.currency)}</span>
                  </div>
                  <div className="mt-2 flex items-center justify-between gap-3">
                    <span className="text-muted-foreground">Store total</span>
                    <span className="font-semibold text-foreground">{storeFinancialValue(store.total, store.currency)}</span>
                  </div>
                </div>
              </div>
            ))}
          </div>

          <div className="grid gap-3 rounded-[1.75rem] border border-border/70 bg-white p-4">
            <div className="flex items-center justify-between gap-3 text-sm">
              <span className="text-muted-foreground">Subtotal</span>
              <span className="font-semibold text-foreground">{financialValue(financialSummary?.subtotal)}</span>
            </div>
            <div className="flex items-center justify-between gap-3 text-sm">
              <span className="text-muted-foreground">Shipping</span>
              <span className="font-semibold text-foreground">{financialValue(financialSummary?.shipping)}</span>
            </div>
            <div className="flex items-center justify-between gap-3 text-sm">
              <span className="text-muted-foreground">Payment</span>
              <span className="font-semibold text-foreground">{paymentLabel}</span>
            </div>
            <div className="h-px bg-border/70" />
            <div className="flex items-center justify-between gap-3">
              <span className="text-base font-semibold text-foreground">Total</span>
              <span className="text-2xl font-black tracking-tight text-foreground">{financialValue(financialSummary?.total)}</span>
            </div>
          </div>

          {promo ? (
            <div className="rounded-[1.75rem] border border-emerald-200 bg-emerald-50 p-4">
              <div className="flex items-start gap-3">
                <TicketPercent className="mt-0.5 h-4 w-4 shrink-0 text-emerald-700" />
                <div className="space-y-1 text-sm text-emerald-950">
                  <p className="font-semibold">
                    Promo {promo.code} is active. You saved {financialValue(promo.discountTotal)}
                  </p>
                  <p>CommerceHub absorbs this discount against platform commission, so your order total stays the same.</p>
                </div>
              </div>
            </div>
          ) : null}

          <div className="grid gap-3 rounded-[1.75rem] border border-border/70 bg-slate-50/80 p-4">
            <div className="flex items-center gap-3 text-sm text-muted-foreground">
              <PackageCheck className="h-4 w-4 text-primary" />
              {cart.summary.itemCount} item{cart.summary.itemCount === 1 ? "" : "s"} across {storeLabel}
            </div>
            <div className="flex items-center gap-3 text-sm text-muted-foreground">
              <Truck className="h-4 w-4 text-primary" />
              Shipping is selected per store behind the scenes and shown here as one total
            </div>
            <div className="flex items-center gap-3 text-sm text-muted-foreground">
              <CreditCard className="h-4 w-4 text-primary" />
              Selected payment method: {paymentLabel}
            </div>
          </div>

          <div className="rounded-[1.5rem] border border-border/70 bg-slate-50/80 p-4 text-sm text-muted-foreground">
            <div className="flex items-start gap-2">
              {isSubmitting ? <LoaderCircle className="mt-0.5 h-4 w-4 animate-spin text-primary" /> : <AlertTriangle className="mt-0.5 h-4 w-4 text-primary" />}
              <p className="font-medium text-foreground">{checkoutMessage}</p>
            </div>
          </div>

          <Button
            type="button"
            size="lg"
            className="w-full justify-between gap-3"
            disabled={isCheckoutDisabled}
            onClick={onSubmit}
          >
            {isSubmitting ? "Processing order..." : resolvedActionLabel}
            {isSubmitting ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <ArrowRight className="h-4 w-4" />}
          </Button>
        </CardContent>
      </Card>

      <div className="fixed inset-x-0 bottom-0 z-40 border-t border-border/70 bg-white/95 p-4 shadow-[0_-12px_32px_rgba(15,23,42,0.12)] backdrop-blur xl:hidden">
        <div className="mx-auto flex max-w-7xl items-center gap-3">
          <div className="min-w-0 flex-1">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Total</p>
            <p className="truncate text-xl font-black tracking-tight text-foreground">{financialValue(financialSummary?.total)}</p>
            <p className="truncate text-xs text-muted-foreground">{promo ? `Promo ${promo.code} active` : paymentLabel}</p>
          </div>

          <Button type="button" className="min-w-44 gap-2" disabled={isCheckoutDisabled} onClick={onSubmit}>
            {isSubmitting ? "Processing..." : mobileActionLabel ?? actionLabel ?? "Place Order"}
            {isSubmitting ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <ArrowRight className="h-4 w-4" />}
          </Button>
        </div>
      </div>
    </>
  );
};

export const CheckoutSummary = memo(CheckoutSummaryComponent);
