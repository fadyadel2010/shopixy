import { ShoppingCart } from "lucide-react";
import { Link } from "react-router-dom";
import { shallow } from "zustand/shallow";

import { selectCartItemCount, useCartStore } from "@/store";
import { Button } from "@/ui/button";
import { cn } from "@/utils/cn";

type CartButtonProps = {
  compact?: boolean;
};

export const CartButton = ({ compact = false }: CartButtonProps) => {
  const itemCount = useCartStore(selectCartItemCount, shallow);

  return (
    <Button
      asChild
      variant="outline"
      size={compact ? "icon" : "default"}
      className={cn("relative rounded-full", compact ? "h-11 w-11 px-0" : "px-4")}
    >
      <Link to="/cart" aria-label="Open cart">
        <ShoppingCart className="h-4 w-4" />
        {!compact ? "Cart" : null}
        {itemCount > 0 ? (
          <span
            className={cn(
              "inline-flex min-w-6 items-center justify-center rounded-full bg-primary px-1.5 py-0.5 text-xs text-primary-foreground",
              compact ? "absolute -right-1 -top-1" : "ml-1"
            )}
          >
            {itemCount}
          </span>
        ) : null}
      </Link>
    </Button>
  );
};
