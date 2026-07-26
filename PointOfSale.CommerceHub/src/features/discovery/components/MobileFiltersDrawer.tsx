import { useEffect, useState } from "react";
import { SlidersHorizontal, X } from "lucide-react";

import type { CommerceHubSearchFacets } from "@/types";
import { Button } from "@/ui/button";
import type { DiscoveryAttributeFilter, DiscoveryFiltersState } from "../types";
import { FiltersSidebar } from "./FiltersSidebar";

type MobileFiltersDrawerProps = {
  open: boolean;
  facets: CommerceHubSearchFacets;
  filters: DiscoveryFiltersState;
  onClose: () => void;
  onApply: (filters: DiscoveryFiltersState) => void;
};

const cloneFilters = (filters: DiscoveryFiltersState): DiscoveryFiltersState => ({
  ...filters,
  attributes: Object.fromEntries(
    Object.entries(filters.attributes).map(([key, value]) => [
      key,
      value.type === "terms"
        ? { type: "terms", values: [...value.values] }
        : { type: "range", min: value.min, max: value.max }
    ])
  ) as Record<string, DiscoveryAttributeFilter>
});

export const MobileFiltersDrawer = ({
  open,
  facets,
  filters,
  onClose,
  onApply
}: MobileFiltersDrawerProps) => {
  const [draftFilters, setDraftFilters] = useState<DiscoveryFiltersState>(() => cloneFilters(filters));

  useEffect(() => {
    if (open) {
      setDraftFilters(cloneFilters(filters));
    }
  }, [filters, open]);

  if (!open) {
    return null;
  }

  return (
    <div className="fixed inset-0 z-[60] bg-slate-950/45 lg:hidden">
      <button type="button" className="absolute inset-0 h-full w-full cursor-default" onClick={onClose} />
      <div className="absolute inset-0 bg-background">
        <div className="flex items-center justify-between border-b border-border/70 px-5 py-4">
          <div className="flex items-center gap-2">
            <SlidersHorizontal className="h-4 w-4 text-primary" />
            <p className="text-base font-semibold text-foreground">Filters</p>
          </div>
          <Button type="button" variant="ghost" size="icon" onClick={onClose} aria-label="Close filters">
            <X className="h-4 w-4" />
          </Button>
        </div>

        <div className="h-[calc(100vh-8rem)] overflow-y-auto px-5 py-5">
          <FiltersSidebar
            facets={facets}
            filters={draftFilters}
            onCategoryChange={(categoryId) => setDraftFilters((current) => ({ ...current, categoryId }))}
            onInStockChange={(inStock) => setDraftFilters((current) => ({ ...current, inStock }))}
            onPriceChange={(range) =>
              setDraftFilters((current) => ({
                ...current,
                priceMin: range.min,
                priceMax: range.max
              }))
            }
            onAttributeTermsChange={(key, values) =>
              setDraftFilters((current) => ({
                ...current,
                attributes: values.length > 0
                  ? {
                      ...current.attributes,
                      [key]: {
                        type: "terms",
                        values
                      }
                    }
                  : Object.fromEntries(
                      Object.entries(current.attributes).filter(([attributeKey]) => attributeKey !== key)
                    )
              }))
            }
            onAttributeRangeChange={(key, range) =>
              setDraftFilters((current) => {
                const nextAttributes = { ...current.attributes };
                if (range.min === undefined && range.max === undefined) {
                  delete nextAttributes[key];
                } else {
                  nextAttributes[key] = {
                    type: "range",
                    min: range.min,
                    max: range.max
                  };
                }

                return {
                  ...current,
                  attributes: nextAttributes
                };
              })
            }
            onClear={() =>
              setDraftFilters((current) => ({
                ...current,
                categoryId: "",
                inStock: false,
                priceMin: undefined,
                priceMax: undefined,
                attributes: {}
              }))
            }
          />
        </div>

        <div className="grid grid-cols-2 gap-3 border-t border-border/70 px-5 py-4">
          <Button
            type="button"
            variant="outline"
            onClick={() =>
              setDraftFilters((current) => ({
                ...current,
                categoryId: "",
                inStock: false,
                priceMin: undefined,
                priceMax: undefined,
                attributes: {}
              }))
            }
          >
            Clear all
          </Button>
          <Button
            type="button"
            onClick={() => {
              onApply(draftFilters);
              onClose();
            }}
          >
            Apply filters
          </Button>
        </div>
      </div>
    </div>
  );
};
