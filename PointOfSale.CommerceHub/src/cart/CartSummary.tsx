import { memo } from "react";
import { AlertTriangle, ArrowRight, Layers3, LoaderCircle, Truck } from "lucide-react";

import type { CartSummary as CartSummaryData, CartValidationIssue } from "@/types";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { formatCurrency } from "@/utils";

type CartSummaryProps = {
  summary: CartSummaryData | null;
  isCartLocked: boolean;
  isCheckoutDisabled: boolean;
  checkoutMessage: string;
  issues: CartValidationIssue[];
  isInventoryRefreshing: boolean;
  onProceedToCheckout: () => void;
  onClearCart: () => void;
};

const CartSummaryComponent = ({
  summary,
  isCartLocked,
  isCheckoutDisabled,
  checkoutMessage,
  issues,
  isInventoryRefreshing,
  onProceedToCheckout,
  onClearCart
}: CartSummaryProps) => {
  const issuePreview = issues.slice(0, 3);
  const financialValue = (value: number | undefined) =>
    summary && value !== undefined ? formatCurrency(value, summary.currency) : "--";

  return (
    <>
      <Card className="hidden border-white/70 bg-white/92 xl:sticky xl:top-28 xl:block">
        <CardContent className="space-y-6 p-6">
          <div className="brand-gradient overflow-hidden rounded-[1.75rem] p-5 text-white shadow-glow">
            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-white/75">Order summary</p>
            <p className="mt-3 text-3xl font-black tracking-tight">{financialValue(summary?.total)}</p>
            <p className="mt-2 text-sm text-white/82">
              Shopixy keeps multi-vendor carts elegant by rolling vendor-level shipping into one clean total.
            </p>
          </div>

          <div className="grid gap-3 rounded-[1.75rem] border border-white/70 bg-secondary/60 p-4">
            <div className="flex items-center justify-between gap-3 text-sm">
              <span className="text-muted-foreground">Subtotal</span>
              <span className="font-semibold text-foreground">{financialValue(summary?.subtotal)}</span>
            </div>
            <div className="flex items-center justify-between gap-3 text-sm">
              <span className="text-muted-foreground">Shipping</span>
              <span className="font-semibold text-foreground">{financialValue(summary?.shipping)}</span>
            </div>
            <div className="h-px bg-border/70" />
            <div className="flex items-center justify-between gap-3">
              <span className="text-base font-semibold text-foreground">Grand total</span>
              <span className="text-2xl font-black tracking-tight text-foreground">{financialValue(summary?.total)}</span>
            </div>
          </div>

          <div className="grid gap-3 rounded-[1.75rem] border border-white/70 bg-white/78 p-4">
            <div className="flex items-center gap-3 text-sm text-muted-foreground">
              <Layers3 className="h-4 w-4 text-primary" />
              {summary?.storeCount ?? 0} store{summary?.storeCount === 1 ? "" : "s"} in this order
            </div>
            <div className="flex items-center gap-3 text-sm text-muted-foreground">
              <Truck className="h-4 w-4 text-primary" />
              {summary?.itemCount ?? 0} item{summary?.itemCount === 1 ? "" : "s"} across all vendors
            </div>
          </div>

          <div className="rounded-[1.5rem] border border-white/70 bg-white/78 p-4 text-sm text-muted-foreground">
            <div className="flex items-start gap-2">
              {isInventoryRefreshing ? (
                <LoaderCircle className="mt-0.5 h-4 w-4 animate-spin text-primary" />
              ) : (
                <AlertTriangle className="mt-0.5 h-4 w-4 text-primary" />
              )}
              <div className="space-y-2">
                <p className="font-medium text-foreground">{checkoutMessage}</p>
                {issuePreview.length > 0 ? (
                  <div className="space-y-1">
                    {issuePreview.map((issue) => (
                      <p key={`${issue.storeId}-${issue.variantId}`}>{issue.itemName}: {issue.message}</p>
                    ))}
                  </div>
                ) : null}
              </div>
            </div>
          </div>

          <div className="space-y-3">
            <Button
              type="button"
              size="lg"
              className="w-full justify-between gap-3"
              disabled={isCheckoutDisabled}
              onClick={onProceedToCheckout}
            >
              Proceed to checkout
              <ArrowRight className="h-4 w-4" />
            </Button>

            <Button type="button" variant="outline" className="w-full" disabled={isCartLocked} onClick={onClearCart}>
              Clear cart
            </Button>
          </div>
        </CardContent>
      </Card>

      <div className="fixed inset-x-0 bottom-0 z-40 border-t border-white/70 bg-background/80 p-4 backdrop-blur xl:hidden">
        <div className="surface-panel mx-auto flex max-w-7xl items-center gap-3 px-4 py-3">
          <div className="min-w-0 flex-1">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Total</p>
            <p className="truncate text-xl font-black tracking-tight text-foreground">{financialValue(summary?.total)}</p>
            <p className="truncate text-xs text-muted-foreground">{checkoutMessage}</p>
          </div>

          <Button type="button" className="min-w-40 gap-2" disabled={isCheckoutDisabled} onClick={onProceedToCheckout}>
            Proceed
            <ArrowRight className="h-4 w-4" />
          </Button>
        </div>
      </div>
    </>
  );
};

export const CartSummary = memo(CartSummaryComponent);
