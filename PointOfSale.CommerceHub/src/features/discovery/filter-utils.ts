import type { DiscoveryFiltersState } from "./types";
import type { CommerceHubAttributeFacet } from "@/types";

export type FilterTrackingEntry = {
  key: string;
  value: string;
};

const normalizeTrackingValue = (value: string) => value.trim().toLowerCase();

const serializeRangeTrackingValue = (min?: number, max?: number) => {
  if (min === undefined && max === undefined) {
    return "";
  }

  return `min:${min ?? ""}|max:${max ?? ""}`;
};

export const getFacetValueCount = (facet: CommerceHubAttributeFacet) =>
  typeof facet.valueCount === "number"
    ? facet.valueCount
    : facet.type === "number"
      ? 0
      : facet.values.reduce((total, value) => total + value.count, 0);

export const sortAttributeFacets = (attributes: Record<string, CommerceHubAttributeFacet>) =>
  Object.entries(attributes)
    .filter(([, facet]) => {
      if (facet.type === "number") {
        return facet.range?.min !== undefined || facet.range?.max !== undefined;
      }

      return facet.values.some((value) => value.count > 0);
    })
    .sort(([, leftFacet], [, rightFacet]) => {
      const leftSortOrder = leftFacet.sortOrder > 0 ? leftFacet.sortOrder : Number.MAX_SAFE_INTEGER;
      const rightSortOrder = rightFacet.sortOrder > 0 ? rightFacet.sortOrder : Number.MAX_SAFE_INTEGER;
      if (leftSortOrder !== rightSortOrder) {
        return leftSortOrder - rightSortOrder;
      }

      const valueCountDifference = getFacetValueCount(rightFacet) - getFacetValueCount(leftFacet);
      if (valueCountDifference !== 0) {
        return valueCountDifference;
      }

      return leftFacet.label.localeCompare(rightFacet.label);
    });

export const serializeFilterTrackingEntries = (filters: DiscoveryFiltersState): FilterTrackingEntry[] => {
  const entries: FilterTrackingEntry[] = [];

  if (filters.categoryId) {
    entries.push({
      key: "category",
      value: normalizeTrackingValue(filters.categoryId)
    });
  }

  if (filters.inStock) {
    entries.push({
      key: "availability",
      value: "in_stock"
    });
  }

  const priceValue = serializeRangeTrackingValue(filters.priceMin, filters.priceMax);
  if (priceValue) {
    entries.push({
      key: "price",
      value: priceValue
    });
  }

  Object.entries(filters.attributes)
    .sort(([leftKey], [rightKey]) => leftKey.localeCompare(rightKey))
    .forEach(([key, filter]) => {
      if (filter.type === "terms") {
        Array.from(new Set(filter.values.map((value) => value.trim()).filter(Boolean)))
          .sort((leftValue, rightValue) => leftValue.localeCompare(rightValue))
          .forEach((value) => {
            entries.push({
              key,
              value: normalizeTrackingValue(value)
            });
          });

        return;
      }

      const rangeValue = serializeRangeTrackingValue(filter.min, filter.max);
      if (!rangeValue) {
        return;
      }

      entries.push({
        key,
        value: rangeValue
      });
    });

  return entries;
};

export const getAddedFilterTrackingEntries = (
  previousFilters: DiscoveryFiltersState,
  nextFilters: DiscoveryFiltersState
) => {
  const previousEntries = new Set(
    serializeFilterTrackingEntries(previousFilters).map((entry) => `${entry.key}:${entry.value}`)
  );

  return serializeFilterTrackingEntries(nextFilters).filter(
    (entry) => !previousEntries.has(`${entry.key}:${entry.value}`)
  );
};
