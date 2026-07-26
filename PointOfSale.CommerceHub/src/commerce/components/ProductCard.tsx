import { memo } from "react";
import { ArrowUpRight, ImageOff, Store } from "lucide-react";
import { Link } from "react-router-dom";

import type { CommerceHubProduct } from "@/types";
import { Badge } from "@/ui/badge";
import { Card, CardContent } from "@/ui/card";
import { formatCurrency } from "@/utils/formatCurrency";

type ProductCardProps = {
  product: CommerceHubProduct;
  onClick?: (product: CommerceHubProduct) => void;
};

export const ProductCard = memo(({ product, onClick }: ProductCardProps) => {
  return (
    <Link
      to={`/product/${product.id}`}
      className="block h-full"
      aria-label={`View ${product.productName}`}
      onClick={() => onClick?.(product)}
    >
      <Card className="group h-full overflow-hidden border-white/60 bg-white/92 transition-all duration-300 hover:-translate-y-1.5 hover:shadow-glow">
        <div className="relative aspect-[4/3] overflow-hidden bg-muted">
          {product.image ? (
            <img
              src={product.image}
              alt={product.productName}
              className="h-full w-full object-cover transition-transform duration-500 group-hover:scale-[1.06]"
              loading="lazy"
            />
          ) : (
            <div className="flex h-full flex-col items-center justify-center gap-3 bg-gradient-to-br from-primary/10 via-white to-accent/10 text-muted-foreground">
              <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-white/80 shadow-sm">
                <ImageOff className="h-5 w-5" />
              </div>
              <span className="text-sm font-semibold">Image coming soon</span>
            </div>
          )}

          <div className="pointer-events-none absolute inset-0 bg-gradient-to-t from-slate-950/35 via-transparent to-transparent opacity-60" />

          <div className="absolute left-4 top-4 flex flex-wrap gap-2">
            {product.categoryName ? <Badge variant="muted">{product.categoryName}</Badge> : null}
            <Badge variant={product.inStock ? "success" : "warning"}>
              {product.inStock ? "In stock" : "Out of stock"}
            </Badge>
          </div>
        </div>

        <CardContent className="flex h-[11.25rem] flex-col gap-4 p-5">
          <div className="space-y-3">
            <p className="line-clamp-2 min-h-[3.25rem] text-lg font-bold leading-6 text-foreground">
              {product.productName}
            </p>
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <Store className="h-4 w-4 shrink-0" />
              <span className="truncate">{product.storeName}</span>
            </div>
          </div>

          <div className="mt-auto flex items-end justify-between gap-3">
            <div>
              <p className="text-2xl font-extrabold tracking-tight text-foreground">
                {formatCurrency(product.price, product.currency)}
              </p>
            </div>
            <div className="flex items-center gap-1 rounded-full bg-secondary px-3 py-2 text-xs font-semibold uppercase tracking-[0.18em] text-secondary-foreground transition-colors group-hover:bg-primary/10 group-hover:text-primary">
              View
              <ArrowUpRight className="h-3.5 w-3.5" />
            </div>
          </div>
        </CardContent>
      </Card>
    </Link>
  );
});

ProductCard.displayName = "ProductCard";
