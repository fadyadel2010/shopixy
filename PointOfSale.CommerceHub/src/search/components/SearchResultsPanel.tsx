import { memo } from "react";
import { ArrowUpRight, Store } from "lucide-react";

import type { CommerceHubProduct } from "@/types";
import { formatCurrency } from "@/utils/formatCurrency";

type SearchResultItemProps = {
  product: CommerceHubProduct;
  onSelect: (product: CommerceHubProduct) => void;
};

const SearchResultItem = memo(({ product, onSelect }: SearchResultItemProps) => {
  return (
    <button
      type="button"
      className="flex w-full items-center justify-between gap-3 rounded-2xl border border-transparent bg-white px-3 py-3 text-left transition-colors hover:border-primary/20 hover:bg-primary/5"
      onMouseDown={(event) => event.preventDefault()}
      onClick={() => onSelect(product)}
    >
      <div className="min-w-0 space-y-2">
        <p className="line-clamp-2 text-sm font-semibold text-foreground">{product.productName}</p>
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          <Store className="h-3.5 w-3.5 shrink-0" />
          <span className="truncate">{product.storeName}</span>
        </div>
      </div>
      <div className="shrink-0 text-right">
        <p className="text-sm font-extrabold text-foreground">
          {formatCurrency(product.price, product.currency)}
        </p>
        <ArrowUpRight className="ml-auto mt-2 h-4 w-4 text-muted-foreground" />
      </div>
    </button>
  );
});

SearchResultItem.displayName = "SearchResultItem";

type SearchResultsPanelProps = {
  results: CommerceHubProduct[];
  onSelect: (product: CommerceHubProduct) => void;
};

export const SearchResultsPanel = ({ results, onSelect }: SearchResultsPanelProps) => {
  return (
    <div className="space-y-2 p-3">
      {results.map((result) => (
        <SearchResultItem key={result.id} product={result} onSelect={onSelect} />
      ))}
    </div>
  );
};
