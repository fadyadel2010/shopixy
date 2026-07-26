import { memo, useMemo, useState } from "react";
import { ChevronDown, Store } from "lucide-react";

import { CartItem } from "@/cart/CartItem";
import { selectIsCartLocked, useCartStore } from "@/store";
import { getCartStoreSummary } from "@/types/cart";
import { Badge } from "@/ui/badge";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { formatCurrency } from "@/utils";
import { COMMERCEHUB_AUTHORITY_CURRENCY } from "@/utils/commerceHubCurrencyAuthority";

type StoreGroupProps = {
  storeId: string;
};

const StoreGroupComponent = ({ storeId }: StoreGroupProps) => {
  const [isExpanded, setIsExpanded] = useState(true);
  const store = useCartStore((state) => state.stores[storeId]);
  const removeItem = useCartStore((state) => state.removeItem);
  const updateQuantity = useCartStore((state) => state.updateQuantity);
  const clearStore = useCartStore((state) => state.clearStore);
  const isCartLocked = useCartStore(selectIsCartLocked);

  const storeSummary = useMemo(() => (store ? getCartStoreSummary(store) : null), [store]);

  if (!store || !storeSummary) {
    return null;
  }

  return (
    <Card className="border-white/70 bg-white/88 shadow-card">
      <CardContent className="space-y-5 p-6 md:p-7">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-3">
            <div className="flex items-start gap-3">
              <div className="brand-gradient-soft rounded-[1.4rem] p-3 text-primary">
                <Store className="h-5 w-5" />
              </div>
              <div className="space-y-1">
                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-muted-foreground">Vendor group</p>
                <h2 className="text-2xl font-black tracking-tight text-foreground">{store.storeName}</h2>
                <p className="text-sm text-muted-foreground">
                  {storeSummary.itemCount} item{storeSummary.itemCount === 1 ? "" : "s"} from this store. Orders stay grouped cleanly for fulfillment.
                </p>
              </div>
            </div>

            <div className="flex flex-wrap gap-2">
              <Badge variant="muted">{storeSummary.itemCount} item{storeSummary.itemCount === 1 ? "" : "s"}</Badge>
              <Badge variant="accent">{formatCurrency(storeSummary.subtotal, COMMERCEHUB_AUTHORITY_CURRENCY)}</Badge>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <Button
              type="button"
              variant="secondary"
              size="sm"
              className="gap-2"
              onClick={() => setIsExpanded((current) => !current)}
              aria-expanded={isExpanded}
              aria-controls={`store-group-${storeId}`}
            >
              {isExpanded ? "Collapse" : "Expand"}
              <ChevronDown className={`h-4 w-4 transition-transform ${isExpanded ? "rotate-180" : ""}`} />
            </Button>

            <Button type="button" variant="outline" size="sm" disabled={isCartLocked} onClick={() => clearStore(storeId)}>
              Remove store
            </Button>
          </div>
        </div>

        <div
          id={`store-group-${storeId}`}
          className={`grid overflow-hidden transition-all duration-300 ease-out ${isExpanded ? "grid-rows-[1fr] opacity-100" : "grid-rows-[0fr] opacity-0"}`}
        >
          <div className="min-h-0 space-y-5">
            <div className="divide-y divide-border/70 rounded-[1.75rem] border border-white/80 bg-white/72 px-5">
              {store.items.map((item) => (
                <CartItem
                  key={item.variantId}
                  currency={COMMERCEHUB_AUTHORITY_CURRENCY}
                  item={item}
                  isLocked={isCartLocked}
                  onDecrease={() => updateQuantity(storeId, item.variantId, item.quantity - 1)}
                  onIncrease={() => updateQuantity(storeId, item.variantId, item.quantity + 1)}
                  onRemove={() => removeItem(storeId, item.variantId)}
                />
              ))}
            </div>

            <div className="grid gap-3 rounded-[1.75rem] border border-white/80 bg-secondary/55 p-5 md:grid-cols-2">
              <div className="space-y-1">
                <p className="text-sm text-muted-foreground">Store subtotal</p>
                <p className="text-xl font-bold text-foreground">{formatCurrency(storeSummary.subtotal, COMMERCEHUB_AUTHORITY_CURRENCY)}</p>
              </div>

              <div className="space-y-1 md:text-right">
                <p className="text-sm text-muted-foreground">Store items</p>
                <p className="text-2xl font-black tracking-tight text-foreground">
                  {storeSummary.itemCount} item{storeSummary.itemCount === 1 ? "" : "s"}
                </p>
              </div>
            </div>
          </div>
        </div>
      </CardContent>
    </Card>
  );
};

export const StoreGroup = memo(StoreGroupComponent);
