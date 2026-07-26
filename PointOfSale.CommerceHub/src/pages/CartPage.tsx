import { AlertTriangle, ArrowLeft, ShoppingCart, TicketPercent } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { shallow } from "zustand/shallow";

import { CartSummary, StoreGroup, useCartInventoryValidation, useCommerceHubCartPricing } from "@/cart";
import { normalizeApiError } from "@/api/api-error";
import { EmptyState } from "@/shared/EmptyState";
import { SectionHeading } from "@/shared/SectionHeading";
import { selectCartStoreIds, selectCartSummary, selectCartValidation, selectCheckoutPayload, selectIsCartLocked, selectPromoCode, useCartStore } from "@/store";
import { Badge } from "@/ui/badge";
import { Button } from "@/ui/button";
import { Input } from "@/ui/input";
import { formatCurrency, useDocumentTitle } from "@/utils";

const PROMO_INVALIDATION_CODES = new Set([
  "PROMO_NOT_FOUND",
  "PROMO_INACTIVE",
  "PROMO_EXPIRED",
  "PROMO_USAGE_LIMIT_REACHED",
  "PROMO_CUSTOMER_USAGE_LIMIT_REACHED",
  "PROMO_NOT_APPLICABLE"
]);

export const CartPage = () => {
  const navigate = useNavigate();
  const storeIds = useCartStore(selectCartStoreIds, shallow);
  const summary = useCartStore(selectCartSummary, shallow);
  const validation = useCartStore(selectCartValidation, shallow);
  const checkoutPayload = useCartStore(selectCheckoutPayload, shallow);
  const isCartLocked = useCartStore(selectIsCartLocked);
  const appliedPromoCode = useCartStore(selectPromoCode);
  const clearCart = useCartStore((state) => state.clearCart);
  const setPromoCode = useCartStore((state) => state.setPromoCode);
  const clearPromoCode = useCartStore((state) => state.clearPromoCode);
  const { isInventoryRefreshing, hasInventoryErrors } = useCartInventoryValidation();
  const [promoInput, setPromoInput] = useState(appliedPromoCode ?? "");
  const [promoNotice, setPromoNotice] = useState<string | null>(null);
  const cartPricingQuery = useCommerceHubCartPricing(checkoutPayload, [], appliedPromoCode);

  useEffect(() => {
    setPromoInput(appliedPromoCode ?? "");
  }, [appliedPromoCode]);

  useDocumentTitle(storeIds.length > 0 ? `Cart (${summary.itemCount})` : "Cart");

  const hasMissingListings = useMemo(
    () => checkoutPayload.stores.some((store) => store.items.some((item) => !item.listingId)),
    [checkoutPayload.stores]
  );

  const pricingApiError = cartPricingQuery.error ? normalizeApiError(cartPricingQuery.error) : null;
  const pricingError = pricingApiError?.message ?? null;
  const isPricingRefreshing = cartPricingQuery.isPending || cartPricingQuery.isFetching;
  const appliedPromo = cartPricingQuery.data?.promo?.isApplied ? cartPricingQuery.data.promo : null;
  const normalizedPromoInput = promoInput.trim().toUpperCase();
  const canApplyPromo =
    normalizedPromoInput.length > 0 &&
    normalizedPromoInput !== (appliedPromoCode ?? "") &&
    !isPricingRefreshing &&
    !isCartLocked;

  const pricedSummary = useMemo(() => {
    if (isPricingRefreshing || cartPricingQuery.isError || !cartPricingQuery.data) {
      return null;
    }

    return {
      ...summary,
      currency: cartPricingQuery.data.currency,
      subtotal: cartPricingQuery.data.subtotal,
      shipping: cartPricingQuery.data.shippingTotal,
      total: cartPricingQuery.data.total
    };
  }, [cartPricingQuery.data, cartPricingQuery.isError, isPricingRefreshing, summary]);

  useEffect(() => {
    if (!appliedPromoCode || !pricingApiError?.code || !PROMO_INVALIDATION_CODES.has(pricingApiError.code)) {
      return;
    }

    clearPromoCode();
    setPromoInput("");
    setPromoNotice(pricingApiError.message);
  }, [appliedPromoCode, clearPromoCode, pricingApiError]);

  const storeCountLabel = useMemo(() => {
    if (summary.storeCount === 1) {
      return "1 store";
    }

    return `${summary.storeCount} stores`;
  }, [summary.storeCount]);

  const itemCountLabel = useMemo(() => {
    if (summary.itemCount === 1) {
      return "1 item";
    }

    return `${summary.itemCount} items`;
  }, [summary.itemCount]);

  const handleProceedToCheckout = () => {
    if (
      storeIds.length === 0 ||
      !validation.canCheckout ||
      isInventoryRefreshing ||
      isPricingRefreshing ||
      hasInventoryErrors ||
      hasMissingListings ||
      cartPricingQuery.isError ||
      !cartPricingQuery.data
    ) {
      return;
    }

    navigate("/checkout");
  };

  const handleApplyPromo = () => {
    if (!normalizedPromoInput) {
      return;
    }

    setPromoNotice(null);
    setPromoCode(normalizedPromoInput);
  };

  const handleRemovePromo = () => {
    clearPromoCode();
    setPromoInput("");
    setPromoNotice(null);
  };

  const isCheckoutDisabled =
    storeIds.length === 0 ||
    isCartLocked ||
    !validation.canCheckout ||
    isInventoryRefreshing ||
    hasInventoryErrors ||
    isPricingRefreshing ||
    hasMissingListings ||
    cartPricingQuery.isError ||
    !cartPricingQuery.data;
  const checkoutMessage = isInventoryRefreshing || isPricingRefreshing
    ? "Refreshing live stock, shipping, and totals before checkout."
    : isCartLocked
      ? "Your cart is locked because checkout is already in progress in another session."
    : hasMissingListings
      ? "Some cart items need to be re-added before shipping can be calculated."
      : pricingError
        ? pricingError
        : hasInventoryErrors
          ? "Some cart items could not be revalidated. Review your cart before checking out."
          : validation.issues[0]?.message ?? "Shipping is selected per store automatically and shown here as one global total.";

  if (storeIds.length === 0) {
    return (
      <EmptyState
        icon={<ShoppingCart className="h-6 w-6" />}
        title="Your cart is empty"
        description="Add products from Shopixy sellers and we'll group them by vendor, price shipping per store behind the scenes, and show one polished total at checkout."
        actionLabel="Continue shopping"
        onAction={() => navigate("/products")}
      />
    );
  }

  return (
    <div className="space-y-8 pb-28 xl:pb-0">
      <div className="surface-panel overflow-hidden px-6 py-6 md:px-8 md:py-8">
        <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_auto] xl:items-end">
          <div className="space-y-4">
            <Button asChild type="button" variant="outline" size="sm" className="gap-2">
              <Link to="/products">
                <ArrowLeft className="h-4 w-4" />
                Continue shopping
              </Link>
            </Button>

            <SectionHeading
              eyebrow="Shopixy cart"
              title="Review your premium marketplace cart before checkout"
              description={`${itemCountLabel} across ${storeCountLabel}. Shipping is priced per vendor internally, then merged into one polished marketplace total.`}
            />

            <div className="flex flex-wrap gap-2">
              <Badge variant="muted">{itemCountLabel}</Badge>
              <Badge variant="accent">{storeCountLabel}</Badge>
              <Badge variant="success">{pricedSummary ? `${formatCurrency(pricedSummary.total, pricedSummary.currency)} total` : "Pricing refresh required"}</Badge>
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <div className="rounded-[1.5rem] border border-white/70 bg-white/80 px-4 py-4 shadow-soft">
              <p className="text-xs font-semibold uppercase tracking-[0.22em] text-muted-foreground">Vendor grouping</p>
              <p className="mt-2 text-lg font-bold tracking-tight text-foreground">Clean fulfillment by store</p>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">Each seller stays grouped so fulfillment and shipping remain clear.</p>
            </div>
            <div className="rounded-[1.5rem] border border-white/70 bg-white/80 px-4 py-4 shadow-soft">
              <p className="text-xs font-semibold uppercase tracking-[0.22em] text-muted-foreground">Checkout clarity</p>
              <p className="mt-2 text-lg font-bold tracking-tight text-foreground">One premium summary</p>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">Shopixy rolls the complexity into a single customer-friendly total.</p>
            </div>
          </div>
        </div>
      </div>

      {validation.issues.length > 0 ? (
        <div className="rounded-[1.75rem] border border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900">
          <div className="flex items-start gap-3">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <div className="space-y-1">
              <p className="font-semibold">Checkout is paused until the cart is valid.</p>
              <p>{validation.issues[0]?.message}</p>
            </div>
          </div>
        </div>
      ) : null}

      {isCartLocked ? (
        <div className="rounded-[1.75rem] border border-sky-200 bg-sky-50 px-5 py-4 text-sm text-sky-900">
          <div className="flex items-start gap-3">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <div className="space-y-1">
              <p className="font-semibold">Checkout is already running for this cart.</p>
              <p>The cart is temporarily read-only until that checkout finishes or the lock times out.</p>
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

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem] xl:items-start">
        <div className="space-y-5">
          {storeIds.map((storeId) => (
            <StoreGroup key={storeId} storeId={storeId} />
          ))}
        </div>

        <div className="space-y-5">
          <div className="brand-gradient-soft rounded-[1.75rem] border border-white/70 p-5 shadow-card">
            <div className="flex items-start gap-3">
              <div className="rounded-2xl bg-white p-2 text-primary shadow-soft">
                <TicketPercent className="h-5 w-5" />
              </div>
              <div className="min-w-0 flex-1 space-y-3">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Promo Code</p>
                  <h2 className="mt-1 text-xl font-black tracking-tight text-foreground">Reduce platform commission only</h2>
                  <p className="mt-2 text-sm leading-6 text-muted-foreground">
                    Promo discounts are absorbed by Shopixy. Your product prices, vendor earnings, and checkout total stay exactly the same.
                  </p>
                </div>

                <div className="flex flex-col gap-3 sm:flex-row">
                  <Input
                    value={promoInput}
                    onChange={(event) => setPromoInput(event.target.value.toUpperCase())}
                    placeholder="Enter Shopixy promo code"
                    autoCapitalize="characters"
                    className="sm:flex-1"
                    disabled={isCartLocked}
                  />
                  <Button type="button" onClick={handleApplyPromo} disabled={!canApplyPromo}>
                    Apply
                  </Button>
                </div>

                {appliedPromo ? (
                  <div className="rounded-[1.5rem] border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-950">
                    <p className="font-semibold">
                      Promo applied. You saved {pricedSummary ? formatCurrency(appliedPromo.discountTotal, pricedSummary.currency) : "--"}
                    </p>
                    <p className="mt-1">
                      Code <span className="font-semibold">{appliedPromo.code}</span> reduces platform commission only. Order total and vendor payouts are unchanged.
                    </p>
                  </div>
                ) : null}

                {promoNotice ? (
                  <div className="rounded-[1.5rem] border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-950">
                    <p className="font-semibold">Promo removed.</p>
                    <p className="mt-1">{promoNotice}</p>
                  </div>
                ) : null}

                {pricingError && appliedPromoCode ? (
                  <div className="rounded-[1.5rem] border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-900">
                    <p className="font-semibold">Promo could not be applied.</p>
                    <p className="mt-1">{pricingError}</p>
                  </div>
                ) : null}

                {appliedPromoCode ? (
                  <div className="flex justify-end">
                    <Button type="button" variant="outline" size="sm" onClick={handleRemovePromo} disabled={isPricingRefreshing}>
                      Remove promo
                    </Button>
                  </div>
                ) : null}
              </div>
            </div>
          </div>

          <CartSummary
            summary={pricedSummary}
            isCartLocked={isCartLocked}
            isCheckoutDisabled={isCheckoutDisabled}
            checkoutMessage={checkoutMessage}
            issues={validation.issues}
            isInventoryRefreshing={isInventoryRefreshing || isPricingRefreshing}
            onProceedToCheckout={handleProceedToCheckout}
            onClearCart={() => {
              void clearCart().catch(() => undefined);
            }}
          />
        </div>
      </div>
    </div>
  );
};
