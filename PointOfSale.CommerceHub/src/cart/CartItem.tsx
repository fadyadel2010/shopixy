import { memo } from "react";
import { AlertTriangle, Package2, Trash2 } from "lucide-react";

import { QuantitySelector } from "@/features/product-details/components/QuantitySelector";
import type { CartItem as CartLineItem } from "@/types";
import { Badge } from "@/ui/badge";
import { Button } from "@/ui/button";
import { formatCurrency } from "@/utils";
import { sanitizePriceSnapshot } from "@/utils/price";

type CartItemProps = {
  currency: string;
  item: CartLineItem;
  isLocked: boolean;
  onDecrease: () => void;
  onIncrease: () => void;
  onRemove: () => void;
};

const formatVariantDetails = (item: CartLineItem) => {
  if (item.variantAttributes && item.variantAttributes.length > 0) {
    return item.variantAttributes.map((attribute) => `${attribute.name}: ${attribute.value}`).join(", ");
  }

  return item.variantLabel;
};

const CartItemComponent = ({ currency, item, isLocked, onDecrease, onIncrease, onRemove }: CartItemProps) => {
  const variantDetails = formatVariantDetails(item);
  const isOutOfStock = !item.isInStock || (typeof item.maxQuantity === "number" && item.maxQuantity < 1);
  const maxQuantity = typeof item.maxQuantity === "number" ? Math.max(1, item.maxQuantity) : item.quantity + 99;

  return (
    <div className="grid gap-4 py-5 sm:grid-cols-[7.5rem_minmax(0,1fr)]">
      <div className="overflow-hidden rounded-[1.5rem] border border-white/80 bg-secondary/55">
        {item.imageUrl ? (
          <img src={item.imageUrl} alt={item.name} className="h-32 w-full object-cover" loading="lazy" />
        ) : (
          <div className="flex h-32 items-center justify-center text-muted-foreground">
            <Package2 className="h-6 w-6" />
          </div>
        )}
      </div>

      <div className="flex flex-col gap-4">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="space-y-2">
            <div className="flex flex-wrap gap-2">
              <Badge variant={isOutOfStock ? "warning" : "success"}>
                {isOutOfStock ? "Needs review" : "Ready to ship"}
              </Badge>
              {variantDetails ? <Badge variant="muted">{variantDetails}</Badge> : null}
            </div>
            <h3 className="text-lg font-bold tracking-tight text-foreground">{item.name}</h3>
            <p className="text-sm font-semibold text-foreground">{formatCurrency(sanitizePriceSnapshot(item.priceSnapshot), currency)} each</p>
          </div>

          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="gap-2 px-0 text-muted-foreground"
            disabled={isLocked}
            onClick={onRemove}
          >
            <Trash2 className="h-4 w-4" />
            Remove
          </Button>
        </div>

        <div className="flex flex-wrap items-center justify-between gap-3">
          <QuantitySelector
            value={item.quantity}
            max={maxQuantity}
            disabled={isLocked || isOutOfStock}
            onChange={(nextValue) => {
              if (nextValue > item.quantity) {
                onIncrease();
              } else if (nextValue < item.quantity) {
                onDecrease();
              }
            }}
          />

          <div className="rounded-[1.35rem] bg-secondary/70 px-4 py-3 text-right">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">Line total</p>
            <p className="mt-1 text-lg font-black tracking-tight text-foreground">
              {formatCurrency(sanitizePriceSnapshot(item.priceSnapshot) * item.quantity, currency)}
            </p>
            {typeof item.maxQuantity === "number" ? (
              <p className="text-xs text-muted-foreground">Max available: {item.maxQuantity}</p>
            ) : null}
          </div>
        </div>

        {item.inventoryNotice ? (
          <div className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
            <div className="flex items-start gap-2">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <p>{item.inventoryNotice}</p>
            </div>
          </div>
        ) : null}
      </div>
    </div>
  );
};

export const CartItem = memo(CartItemComponent);
