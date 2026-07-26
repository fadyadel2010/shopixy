import { useMemo } from "react";
import { useSearchParams } from "react-router-dom";

import type { CommerceHubSearchQuery } from "@/types";
import { isCommerceHubProductSort } from "@/features/discovery/constants";
import type { DiscoveryAttributeFilter, DiscoveryFiltersState } from "../types";

const RESERVED_FILTER_KEYS = new Set([
  "category",
  "search",
  "sort",
  "inStock",
  "price_min",
  "price_max",
  "minPrice",
  "maxPrice",
  "page",
  "pageSize"
]);

const RANGE_SUFFIXES = [
  { suffix: "_min", field: "min" as const },
  { suffix: "_max", field: "max" as const },
  { suffix: ".min", field: "min" as const },
  { suffix: ".max", field: "max" as const }
];

const parseNumber = (value: string | null) => {
  if (!value) {
    return undefined;
  }

  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : undefined;
};

export const normalizeFilterRange = (min?: number, max?: number) => {
  if (min === undefined || max === undefined) {
    return { min, max };
  }

  return min <= max ? { min, max } : { min: max, max: min };
};

const splitFilterValues = (values: string[]) =>
  values
    .flatMap((value) => value.split(","))
    .map((value) => value.trim())
    .filter(Boolean);

export const normalizeDiscoveryAttributeKey = (value: string) =>
  value
    .trim()
    .toLowerCase()
    .replace(/[\s\-.]+/g, "_")
    .replace(/[^a-z0-9_]/g, "")
    .replace(/_{2,}/g, "_")
    .replace(/^_+|_+$/g, "");

const resolveAllowedAttributeKey = (rawKey: string, allowedAttributeKeys?: ReadonlySet<string>) => {
  if (!allowedAttributeKeys || allowedAttributeKeys.size === 0) {
    return null;
  }

  const candidate = rawKey.toLowerCase().startsWith("attr.") ? rawKey.slice("attr.".length) : rawKey;
  const normalized = normalizeDiscoveryAttributeKey(candidate);
  return normalized && allowedAttributeKeys.has(normalized) ? normalized : null;
};

export const parseFiltersFromSearchParams = (
  searchParams: URLSearchParams,
  allowedAttributeKeys?: ReadonlySet<string>
): DiscoveryFiltersState => {
  const search = (searchParams.get("search") ?? "").trim();
  const categoryId = (searchParams.get("category") ?? "").trim();
  const inStock = searchParams.get("inStock") === "true";
  const rawSort = searchParams.get("sort");
  const sort = isCommerceHubProductSort(rawSort) ? rawSort : "";
  const normalizedPrice = normalizeFilterRange(
    parseNumber(searchParams.get("price_min") ?? searchParams.get("minPrice")),
    parseNumber(searchParams.get("price_max") ?? searchParams.get("maxPrice"))
  );

  const groupedValues = new Map<string, string[]>();
  for (const [key, value] of searchParams.entries()) {
    if (!groupedValues.has(key)) {
      groupedValues.set(key, []);
    }

    groupedValues.get(key)!.push(value);
  }

  const attributes: Record<string, DiscoveryAttributeFilter> = {};

  for (const [rawKey, values] of groupedValues.entries()) {
    if (RESERVED_FILTER_KEYS.has(rawKey)) {
      continue;
    }

    const rangeSuffix = RANGE_SUFFIXES.find((item) => rawKey.endsWith(item.suffix));
    if (rangeSuffix) {
      const baseKey = rawKey.slice(0, -rangeSuffix.suffix.length);
      const attributeKey = resolveAllowedAttributeKey(baseKey, allowedAttributeKeys);
      if (!attributeKey || RESERVED_FILTER_KEYS.has(attributeKey)) {
        continue;
      }

      const parsedValue = parseNumber(values.length > 0 ? values[values.length - 1] : null);
      if (parsedValue === undefined) {
        continue;
      }

      const existing = attributes[attributeKey];
      const rangeFilter =
        existing?.type === "range"
          ? { ...existing }
          : {
              type: "range" as const
            };

      rangeFilter[rangeSuffix.field] = parsedValue;
      attributes[attributeKey] = rangeFilter;
      continue;
    }

    const attributeKey = resolveAllowedAttributeKey(rawKey, allowedAttributeKeys);
    if (!attributeKey) {
      continue;
    }

    const terms = Array.from(new Set(splitFilterValues(values)));
    if (terms.length === 0) {
      continue;
    }

    attributes[attributeKey] = {
      type: "terms",
      values: terms
    };
  }

  return {
    categoryId,
    search,
    inStock,
    sort,
    priceMin: normalizedPrice.min,
    priceMax: normalizedPrice.max,
    attributes
  };
};

export const buildSearchQueryFromFilters = (
  filters: DiscoveryFiltersState,
  page: number,
  pageSize: number
): CommerceHubSearchQuery => {
  const normalizedPrice = normalizeFilterRange(filters.priceMin, filters.priceMax);
  const dynamicFilters = Object.entries(filters.attributes)
    .sort(([leftKey], [rightKey]) => leftKey.localeCompare(rightKey))
    .reduce<Record<string, unknown>>((result, [key, filter]) => {
      if (filter.type === "terms") {
        const normalizedValues = Array.from(
          new Set(filter.values.map((value) => value.trim()).filter(Boolean))
        ).sort((leftValue, rightValue) => leftValue.localeCompare(rightValue));
        if (normalizedValues.length > 0) {
          result[key] = normalizedValues;
        }

        return result;
      }

      const normalizedRange = normalizeFilterRange(filter.min, filter.max);
      if (normalizedRange.min === undefined && normalizedRange.max === undefined) {
        return result;
      }

      result[key] = {
        ...(normalizedRange.min !== undefined ? { min: normalizedRange.min } : {}),
        ...(normalizedRange.max !== undefined ? { max: normalizedRange.max } : {})
      };

      return result;
    }, {});

  return {
    q: filters.search || undefined,
    categoryId: filters.categoryId || undefined,
    inStock: filters.inStock || undefined,
    minPrice: normalizedPrice.min,
    maxPrice: normalizedPrice.max,
    sort: filters.sort || undefined,
    page,
    pageSize,
    filters: Object.keys(dynamicFilters).length > 0 ? JSON.stringify(dynamicFilters) : undefined
  };
};

export const useFiltersFromUrl = (allowedAttributeKeys?: ReadonlySet<string>) => {
  const [searchParams] = useSearchParams();

  return useMemo(
    () => parseFiltersFromSearchParams(searchParams, allowedAttributeKeys),
    [allowedAttributeKeys, searchParams]
  );
};
