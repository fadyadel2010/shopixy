import { Check, PackageCheck, Tag } from "lucide-react";

import type { CommerceHubSearchFacetCategory, CommerceHubSearchFacets } from "@/types";
import { cn } from "@/utils";
import { formatFilterLabel } from "@/features/discovery/filter-formatting";
import { sortAttributeFacets } from "@/features/discovery/filter-utils";
import type { DiscoveryAttributeFilter, DiscoveryFiltersState } from "../types";
import { FilterCheckboxGroup } from "./FilterCheckboxGroup";
import { FilterRange } from "./FilterRange";
import { FilterSection } from "./FilterSection";

type FiltersSidebarProps = {
  facets: CommerceHubSearchFacets;
  filters: DiscoveryFiltersState;
  onCategoryChange: (categoryId: string) => void;
  onInStockChange: (inStock: boolean) => void;
  onPriceChange: (range: { min?: number; max?: number }) => void;
  onAttributeTermsChange: (key: string, values: string[]) => void;
  onAttributeRangeChange: (key: string, range: { min?: number; max?: number }) => void;
  onClear: () => void;
};

const CategoryTree = ({
  categories,
  selectedCategoryId,
  onSelect,
  depth = 0
}: {
  categories: CommerceHubSearchFacetCategory[];
  selectedCategoryId: string;
  onSelect: (categoryId: string) => void;
  depth?: number;
}) => (
  <div className="space-y-2">
    {categories.map((category) => {
      const isSelected = selectedCategoryId === category.categoryId;

      return (
        <div key={category.categoryId} className="space-y-2">
          <button
            type="button"
            onClick={() => onSelect(isSelected ? "" : category.categoryId)}
            className={cn(
              "flex w-full items-center gap-3 rounded-2xl border px-3 py-2.5 text-left transition-colors",
              isSelected
                ? "border-primary/30 bg-primary/8"
                : "border-border/70 bg-white hover:border-primary/20 hover:bg-primary/5"
            )}
            style={{ paddingLeft: `${depth * 16 + 12}px` }}
          >
            <span
              className={cn(
                "flex h-4 w-4 shrink-0 items-center justify-center rounded border",
                isSelected ? "border-primary bg-primary text-primary-foreground" : "border-border bg-background"
              )}
              aria-hidden="true"
            >
              {isSelected ? <Check className="h-3 w-3" /> : null}
            </span>
            <span className="min-w-0 flex-1 text-sm font-medium text-foreground">{category.name}</span>
            <span className="shrink-0 text-xs text-muted-foreground">{category.count}</span>
          </button>

          {category.children.length > 0 ? (
            <CategoryTree
              categories={category.children}
              selectedCategoryId={selectedCategoryId}
              onSelect={onSelect}
              depth={depth + 1}
            />
          ) : null}
        </div>
      );
    })}
  </div>
);

const getTermFilterValues = (filter?: DiscoveryAttributeFilter) =>
  filter?.type === "terms" ? filter.values : [];

const getRangeFilterValue = (filter?: DiscoveryAttributeFilter) =>
  filter?.type === "range"
    ? {
        min: filter.min,
        max: filter.max
      }
    : {
        min: undefined,
        max: undefined
      };

export const FiltersSidebar = ({
  facets,
  filters,
  onCategoryChange,
  onInStockChange,
  onPriceChange,
  onAttributeTermsChange,
  onAttributeRangeChange,
  onClear
}: FiltersSidebarProps) => {
  const attributeEntries = sortAttributeFacets(facets.attributes);

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
        <Tag className="h-4 w-4" />
        Filters
      </div>

      <FilterSection title="Category" defaultOpen forceOpen={Boolean(filters.categoryId)}>
        <div className="space-y-2">
          <button
            type="button"
            onClick={() => onCategoryChange("")}
            className={cn(
              "flex w-full items-center gap-3 rounded-2xl border px-3 py-2.5 text-left transition-colors",
              filters.categoryId
                ? "border-border/70 bg-white hover:border-primary/20 hover:bg-primary/5"
                : "border-primary/30 bg-primary/8"
            )}
          >
            <span
              className={cn(
                "flex h-4 w-4 shrink-0 items-center justify-center rounded border",
                filters.categoryId
                  ? "border-border bg-background"
                  : "border-primary bg-primary text-primary-foreground"
              )}
              aria-hidden="true"
            >
              {!filters.categoryId ? <Check className="h-3 w-3" /> : null}
            </span>
            <span className="text-sm font-medium text-foreground">All categories</span>
          </button>

          {facets.categories.length > 0 ? (
            <CategoryTree
              categories={facets.categories}
              selectedCategoryId={filters.categoryId}
              onSelect={onCategoryChange}
            />
          ) : (
            <p className="text-xs text-muted-foreground">Categories update dynamically with the current result set.</p>
          )}
        </div>
      </FilterSection>

      <FilterSection
        title="Price"
        defaultOpen
        forceOpen={filters.priceMin !== undefined || filters.priceMax !== undefined}
      >
        <FilterRange
          min={filters.priceMin}
          max={filters.priceMax}
          availableMin={facets.price.min}
          availableMax={facets.price.max}
          onChange={onPriceChange}
        />
      </FilterSection>

      <FilterSection title="Availability" defaultOpen forceOpen={filters.inStock}>
        <label className="flex items-center gap-3 rounded-2xl border border-border/70 bg-white px-3 py-2.5 text-sm text-foreground">
          <input
            type="checkbox"
            checked={filters.inStock}
            onChange={(event) => onInStockChange(event.target.checked)}
            className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
          />
          <PackageCheck className="h-4 w-4 text-muted-foreground" />
          In stock only
        </label>
      </FilterSection>

      {attributeEntries.map(([key, facet]) => {
        const isActive = filters.attributes[key] !== undefined;
        const defaultOpen = facet.sortOrder <= 2 || attributeEntries.findIndex(([entryKey]) => entryKey === key) < 2;

        if (facet.type === "number") {
          const selectedRange = getRangeFilterValue(filters.attributes[key]);

          return (
            <FilterSection key={key} title={facet.label} defaultOpen={defaultOpen} forceOpen={isActive}>
              <FilterRange
                min={selectedRange.min}
                max={selectedRange.max}
                availableMin={facet.range?.min}
                availableMax={facet.range?.max}
                onChange={(range) => onAttributeRangeChange(key, range)}
              />
            </FilterSection>
          );
        }

        return (
          <FilterSection key={key} title={facet.label} defaultOpen={defaultOpen} forceOpen={isActive}>
            <FilterCheckboxGroup
              options={facet.values.map((value) => ({
                value: value.value,
                label: formatFilterLabel(value.value),
                count: value.count,
                disabled: value.count <= 0
              }))}
              selectedValues={getTermFilterValues(filters.attributes[key])}
              onChange={(values) => onAttributeTermsChange(key, values)}
            />
          </FilterSection>
        );
      })}

      <button
        type="button"
        onClick={onClear}
        className="w-full rounded-2xl border border-dashed border-border/80 px-4 py-3 text-sm font-semibold text-muted-foreground transition-colors hover:border-primary/30 hover:text-foreground"
      >
        Clear filters
      </button>
    </div>
  );
};
