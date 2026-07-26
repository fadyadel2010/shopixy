import { useQuery } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";

import { commerceHubService } from "@/services/commercehub-service";
import type { CommerceHubAutocompleteResponse } from "@/types";
import { useDebouncedSearch } from "./useDebouncedSearch";
import {
  MIN_AUTOCOMPLETE_QUERY_LENGTH,
  normalizeSearchQuery
} from "../utils/search-query";

const RECENT_SEARCHES_STORAGE_KEY = "commercehub_recent_searches";
const MAX_RECENT_SEARCHES = 5;

const loadRecentSearches = () => {
  if (typeof window === "undefined") {
    return [] as string[];
  }

  try {
    const rawValue = window.localStorage.getItem(RECENT_SEARCHES_STORAGE_KEY);
    if (!rawValue) {
      return [] as string[];
    }

    const parsed = JSON.parse(rawValue);
    if (!Array.isArray(parsed)) {
      return [] as string[];
    }

    const recentSearches: string[] = [];
    for (const item of parsed) {
      if (typeof item !== "string") {
        continue;
      }

      const normalizedItem = normalizeSearchQuery(item);
      if (
        normalizedItem.length === 0 ||
        recentSearches.some((value) => value.toLowerCase() === normalizedItem.toLowerCase())
      ) {
        continue;
      }

      recentSearches.push(normalizedItem);
      if (recentSearches.length >= MAX_RECENT_SEARCHES) {
        break;
      }
    }

    return recentSearches;
  } catch {
    return [] as string[];
  }
};

const persistRecentSearches = (searches: string[]) => {
  if (typeof window === "undefined") {
    return;
  }

  window.localStorage.setItem(RECENT_SEARCHES_STORAGE_KEY, JSON.stringify(searches));
};

type UseAutocompleteOptions = {
  initialValue?: string;
  isOpen: boolean;
  categoryId?: string;
  tenantId?: string;
};

export const useAutocomplete = ({
  initialValue = "",
  isOpen,
  categoryId,
  tenantId
}: UseAutocompleteOptions) => {
  const { query, setQuery, debouncedQuery, isDebouncing } = useDebouncedSearch({
    delay: 250,
    initialValue
  });
  const [recentSearches, setRecentSearches] = useState<string[]>([]);

  useEffect(() => {
    setRecentSearches(loadRecentSearches());
  }, []);

  const normalizedQuery = normalizeSearchQuery(query);
  const normalizedDebouncedQuery = normalizeSearchQuery(debouncedQuery);
  const isAutocompleteSettled = normalizedQuery === normalizedDebouncedQuery;
  const shouldLoadAutocomplete =
    isOpen && normalizedDebouncedQuery.length >= MIN_AUTOCOMPLETE_QUERY_LENGTH;
  const shouldLoadPopular = isOpen && normalizedQuery.length === 0;

  const autocompleteQuery = useQuery({
    queryKey: [
      "commercehub",
      "search",
      "autocomplete",
      {
        q: normalizedDebouncedQuery,
        categoryId,
        tenantId
      }
    ],
    queryFn: ({ signal }) =>
      commerceHubService.getAutocomplete(
        {
          q: normalizedDebouncedQuery,
          categoryId,
          tenantId
        },
        signal
      ),
    enabled: shouldLoadAutocomplete,
    staleTime: 30 * 1000,
    gcTime: 5 * 60 * 1000
  });

  const popularQuery = useQuery({
    queryKey: ["commercehub", "search", "popular", { limit: 5 }],
    queryFn: ({ signal }) => commerceHubService.getPopularSearches({ limit: 5 }, signal),
    enabled: shouldLoadPopular,
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000
  });

  const popularSearches = useMemo(
    () =>
      Array.from(
        new Set((popularQuery.data?.items.map((item) => normalizeSearchQuery(item.query)) ?? []).filter(Boolean))
      ),
    [popularQuery.data]
  );

  const rememberSearch = (value: string) => {
    const normalizedValue = normalizeSearchQuery(value);
    if (normalizedValue.length < 2) {
      return;
    }

    setRecentSearches((current) => {
      const next = [
        normalizedValue,
        ...current.filter((item) => item.toLowerCase() !== normalizedValue.toLowerCase())
      ].slice(0, MAX_RECENT_SEARCHES);

      persistRecentSearches(next);
      return next;
    });
  };

  const clearRecentSearches = () => {
    setRecentSearches([]);
    persistRecentSearches([]);
  };

  const emptyAutocomplete = useMemo<CommerceHubAutocompleteResponse>(
    () => ({
      query: normalizedDebouncedQuery,
      intent: "unknown",
      products: [],
      categories: [],
      suggestions: [],
      popular: []
    }),
    [normalizedDebouncedQuery]
  );

  const resolvedAutocomplete =
    normalizedQuery.length >= MIN_AUTOCOMPLETE_QUERY_LENGTH && isAutocompleteSettled
      ? autocompleteQuery.data ?? emptyAutocomplete
      : emptyAutocomplete;

  const isShowingAutocomplete = normalizedQuery.length >= MIN_AUTOCOMPLETE_QUERY_LENGTH;
  const isShowingPopular = normalizedQuery.length === 0;

  return {
    query,
    setQuery,
    debouncedQuery: normalizedDebouncedQuery,
    normalizedQuery,
    isDebouncing,
    autocomplete: resolvedAutocomplete,
    popularSearches,
    recentSearches,
    rememberSearch,
    clearRecentSearches,
    isLoading: isShowingPopular ? popularQuery.isFetching : isShowingAutocomplete ? isDebouncing || autocompleteQuery.isFetching : false,
    isError: isShowingPopular ? popularQuery.isError : isShowingAutocomplete && isAutocompleteSettled ? autocompleteQuery.isError : false,
    error: isShowingPopular ? popularQuery.error : isShowingAutocomplete && isAutocompleteSettled ? autocompleteQuery.error : null,
    refetch: isShowingPopular ? popularQuery.refetch : autocompleteQuery.refetch
  };
};
