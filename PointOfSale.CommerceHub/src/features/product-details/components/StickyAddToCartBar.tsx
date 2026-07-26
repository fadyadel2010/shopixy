import { ShoppingBag } from "lucide-react";

import { QuantitySelector } from "@/features/product-details/components/QuantitySelector";
import { Button } from "@/ui/button";

type StickyAddToCartBarProps = {
  priceLabel: string;
  subtitle: string;
  quantity: number;
  maxQuantity: number;
  disabled: boolean;
  buttonLabel: string;
  lowStockLabel?: string;
  onQuantityChange: (value: number) => void;
  onAddToCart: () => void;
};

export const StickyAddToCartBar = ({
  priceLabel,
  subtitle,
  quantity,
  maxQuantity,
  disabled,
  buttonLabel,
  lowStockLabel,
  onQuantityChange,
  onAddToCart
}: StickyAddToCartBarProps) => {
  return (
    <div className="fixed inset-x-0 bottom-0 z-[65] border-t border-white/70 bg-background/80 px-4 pb-[calc(env(safe-area-inset-bottom)+1rem)] pt-3 backdrop-blur-xl lg:hidden">
      <div className="surface-panel mx-auto flex max-w-3xl items-center gap-3 px-4 py-3">
        <div className="min-w-0 flex-1">
          <p className="text-lg font-black tracking-tight text-foreground">{priceLabel}</p>
          <p className="truncate text-sm text-muted-foreground">{subtitle}</p>
          {lowStockLabel ? <p className="mt-1 text-xs font-semibold text-amber-600">{lowStockLabel}</p> : null}
        </div>

        <QuantitySelector value={quantity} max={maxQuantity} disabled={disabled} onChange={onQuantityChange} />

        <Button type="button" className="h-12 gap-2 px-5" disabled={disabled} onClick={onAddToCart}>
          <ShoppingBag className="h-4 w-4" />
          {buttonLabel}
        </Button>
      </div>
    </div>
  );
};
