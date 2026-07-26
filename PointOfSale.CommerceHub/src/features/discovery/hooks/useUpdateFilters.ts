import { useCallback, useEffect, useRef } from "react";
import { useSearchParams } from "react-router-dom";

import { getAddedFilterTrackingEntries } from "@/features/discovery/filter-utils";
import { normalizeSearchQuery } from "@/search/utils/search-query";
import { getSearchSessionId } from "@/search/utils/search-session";
import { analyticsService, commerceHubService } from "@/services";
import { useDebouncedCallback } from "@/utils";
import type { DiscoveryAttributeFilter, DiscoveryFiltersState } from "../types";
import { normalizeDiscoveryAttributeKey, normalizeFilterRange, parseFiltersFromSearchParams } from "./useFiltersFromUrl";

const buildSearchParamsFromFilters = (filters: DiscoveryFiltersState) => {
  const params = new URLSearchParams();
  const normalizedPrice = normalizeFilterRange(filters.priceMin, filters.priceMax);

  if (filters.search) {
    params.set("search", filters.search);
  }

  if (filters.categoryId) {
    params.set("category", filters.categoryId);
  }

  if (filters.inStock) {
    params.set("inStock", "true");
  }

  if (normalizedPrice.min !== undefined) {
    params.set("price_min", normalizedPrice.min.toString());
  }

  if (normalizedPrice.max !== undefined) {
    params.set("price_max", normalizedPrice.max.toString());
  }

  if (filters.sort) {
    params.set("sort", filters.sort);
  }

  Object.entries(filters.attributes)
    .sort(([left], [right]) => left.localeCompare(right))
    .forEach(([key, filter]) => {
      if (filter.type === "terms") {
        const normalizedValues = Array.from(new Set(filter.values.map((value) => value.trim()).filter(Boolean)));
        if (normalizedValues.length > 0) {
          normalizedValues
            .sort((leftValue, rightValue) => leftValue.localeCompare(rightValue))
            .forEach((value) => {
              params.append(key, value);
            });
        }

        return;
      }

      const normalizedRange = normalizeFilterRange(filter.min, filter.max);

      if (normalizedRange.min !== undefined) {
        params.set(`${key}_min`, normalizedRange.min.toString());
      }

      if (normalizedRange.max !== undefined) {
        params.set(`${key}_max`, normalizedRange.max.toString());
      }
    });

  return params;
};

export const useUpdateFilters = (allowedAttributeKeys?: ReadonlySet<string>) => {
  const [searchParams, setSearchParams] = useSearchParams();
  const filters = parseFiltersFromSearchParams(searchParams, allowedAttributeKeys);
  const draftFiltersRef = useRef(filters);

  useEffect(() => {
    draftFiltersRef.current = filters;
  }, [filters]);

  const applyFilters = useCallback(
    (nextFilters: DiscoveryFiltersState, options?: { replace?: boolean }) => {
      const currentParams = searchParams.toString();
      const nextParams = buildSearchParamsFromFilters(nextFilters);
      const nextParamsString = nextParams.toString();

      draftFiltersRef.current = nextFilters;
      if (currentParams === nextParamsString) {
        return;
      }

      const sessionId = getSearchSessionId();
      const normalizedQuery = normalizeSearchQuery(nextFilters.search);
      const addedEntries = getAddedFilterTrackingEntries(filters, nextFilters);
      const timestamp = new Date().toISOString();

      addedEntries.forEach((entry) => {
        analyticsService.trackFilterApplied(entry.key, entry.value, "products_filters", normalizedQuery || undefined);
        void commerceHubService.trackSearchFilterEvent({
          filterKey: entry.key,
          filterValue: entry.value,
          query: normalizedQuery || undefined,
          sessionId,
          timestamp
        });
      });

      setSearchParams(nextParams, { replace: options?.replace });
    },
    [filters, searchParams, setSearchParams]
  );

  const applyFiltersDebounced = useDebouncedCallback(
    (nextFilters: DiscoveryFiltersState, options?: { replace?: boolean }) => {
      applyFilters(nextFilters, options);
    },
    200
  );

  const patchFilters = useCallback(
    (
      patch: Partial<DiscoveryFiltersState>,
      options?: { replace?: boolean; debounced?: boolean }
    ) => {
      const baseFilters = draftFiltersRef.current;
      const nextFilters = {
        ...baseFilters,
        ...patch
      };

      draftFiltersRef.current = nextFilters;

      if (options?.debounced) {
        applyFiltersDebounced(nextFilters, { replace: options.replace ?? true });
        return;
      }

      applyFilters(nextFilters, options);
    },
    [applyFilters, applyFiltersDebounced, filters]
  );

  const setAttributeFilter = useCallback(
    (
      key: string,
      filter?: DiscoveryAttributeFilter,
      options?: { replace?: boolean; debounced?: boolean }
    ) => {
      const normalizedKey = normalizeDiscoveryAttributeKey(key);
      const nextAttributes = { ...draftFiltersRef.current.attributes };
      if (!normalizedKey || (allowedAttributeKeys && !allowedAttributeKeys.has(normalizedKey))) {
        return;
      }

      if (
        !filter ||
        (filter.type === "terms" && filter.values.length === 0) ||
        (filter.type === "range" && filter.min === undefined && filter.max === undefined)
      ) {
        delete nextAttributes[normalizedKey];
      } else {
        nextAttributes[normalizedKey] = filter;
      }

      patchFilters(
        {
          attributes: nextAttributes
        },
        options
      );
    },
    [allowedAttributeKeys, patchFilters]
  );

  const clearAllFilters = useCallback(() => {
    const baseFilters = draftFiltersRef.current;
    applyFilters(
      {
        ...baseFilters,
        categoryId: "",
        inStock: false,
        priceMin: undefined,
        priceMax: undefined,
        attributes: {}
      },
      { replace: false }
    );
  }, [applyFilters]);

  return {
    filters,
    applyFilters,
    applyFiltersDebounced,
    patchFilters,
    setAttributeFilter,
    clearAllFilters
  };
};
