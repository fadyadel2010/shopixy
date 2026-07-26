import { SlidersHorizontal, Tag } from "lucide-react";
import { useEffect, useRef } from "react";

import type { CategoryOption } from "@/utils";
import { Button } from "@/ui/button";
import { Input } from "@/ui/input";

type ProductFiltersPanelProps = {
  categories: CategoryOption[];
  selectedCategoryId: string;
  inStock: boolean;
  minPrice: string;
  maxPrice: string;
  onCategoryChange: (categoryId: string) => void;
  onPriceRangeChange: (value: { minPrice: string; maxPrice: string }) => void;
  onInStockChange: (checked: boolean) => void;
  onClear: () => void;
};

export const ProductFiltersPanel = ({
  categories,
  selectedCategoryId,
  inStock,
  minPrice,
  maxPrice,
  onCategoryChange,
  onPriceRangeChange,
  onInStockChange,
  onClear
}: ProductFiltersPanelProps) => {
  const minPriceRef = useRef<HTMLInputElement>(null);
  const maxPriceRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (minPriceRef.current && minPriceRef.current.value !== minPrice) {
      minPriceRef.current.value = minPrice;
    }
  }, [minPrice]);

  useEffect(() => {
    if (maxPriceRef.current && maxPriceRef.current.value !== maxPrice) {
      maxPriceRef.current.value = maxPrice;
    }
  }, [maxPrice]);

  const emitPriceRangeChange = () => {
    onPriceRangeChange({
      minPrice: minPriceRef.current?.value ?? "",
      maxPrice: maxPriceRef.current?.value ?? ""
    });
  };

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
        <SlidersHorizontal className="h-4 w-4" />
        Filters
      </div>

      <div>
        <label className="mb-2 block text-sm font-semibold text-foreground">Category</label>
        <select
          className="h-11 w-full rounded-2xl border border-input bg-white px-4 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
          value={selectedCategoryId}
          onChange={(event) => onCategoryChange(event.target.value)}
        >
          <option value="">All categories</option>
          {categories.map((category) => (
            <option key={category.id} value={category.id}>
              {category.label}
            </option>
          ))}
        </select>
      </div>

      <div className="space-y-3">
        <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
          <Tag className="h-4 w-4" />
          Price range
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Input
            ref={minPriceRef}
            inputMode="decimal"
            placeholder="Min"
            defaultValue={minPrice}
            onChange={emitPriceRangeChange}
          />
          <Input
            ref={maxPriceRef}
            inputMode="decimal"
            placeholder="Max"
            defaultValue={maxPrice}
            onChange={emitPriceRangeChange}
          />
        </div>
        <p className="text-xs text-muted-foreground">Price updates automatically after a short pause.</p>
      </div>

      <label className="flex items-center gap-3 rounded-2xl border border-border bg-white px-4 py-3 text-sm text-foreground">
        <input
          type="checkbox"
          checked={inStock}
          onChange={(event) => onInStockChange(event.target.checked)}
          className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
        />
        In stock only
      </label>

      <Button type="button" variant="ghost" className="w-full" onClick={onClear}>
        Clear filters
      </Button>
    </div>
  );
};
