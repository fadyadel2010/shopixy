import {
  Clock3,
  FolderTree,
  PackageSearch,
  Search,
  Sparkles,
  TrendingUp
} from "lucide-react";
import { memo, useEffect, useId, useMemo, useRef, type MutableRefObject } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";

import { saveProductsScrollPosition } from "@/features/discovery/hooks/useProductsScrollRestoration";
import { useAutocomplete } from "@/search/hooks/useAutocomplete";
import { analyticsService, commerceHubService } from "@/services";
import { brand } from "@/shared/brand";
import { useSearchAutocompleteStore } from "@/store";
import { Input } from "@/ui/input";
import { getSearchSessionId } from "../utils/search-session";
import { AutocompleteDropdown, type AutocompleteEntry, type AutocompleteSectionData } from "./AutocompleteDropdown";
import {
  MIN_AUTOCOMPLETE_QUERY_LENGTH,
  normalizeSearchQuery
} from "../utils/search-query";

type SearchAutocompleteProps = {
  onSearchChange?: (value: string) => void;
  placeholder?: string;
};

const rememberTrackingKey = (ref: MutableRefObject<Set<string>>, key: string) => {
  if (ref.current.has(key)) {
    return false;
  }

  if (ref.current.size >= 200) {
    ref.current.clear();
  }

  ref.current.add(key);
  return true;
};

export const SearchAutocomplete = memo(({
  onSearchChange,
  placeholder = "Search Shopixy products, brands, and categories"
}: SearchAutocompleteProps) => {
  const dropdownId = useId();
  const location = useLocation();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const containerRef = useRef<HTMLDivElement>(null);
  const clickTrackingKeysRef = useRef(new Set<string>());
  const impressionTrackingKeysRef = useRef(new Set<string>());
  const syncedSearchParamRef = useRef<string | null>(null);
  const {
    isOpen,
    open,
    close,
    selectedIndex,
    setSelectedIndex,
    resetSelection
  } = useSearchAutocompleteStore();

  const normalizedSearchParam = normalizeSearchQuery(searchParams.get("search"));
  const {
    query,
    setQuery,
    debouncedQuery,
    normalizedQuery,
    isDebouncing,
    autocomplete,
    popularSearches,
    recentSearches,
    rememberSearch,
    clearRecentSearches,
    isLoading,
    isError,
    error,
    refetch
  } = useAutocomplete({
    isOpen
  });

  useEffect(() => {
    onSearchChange?.(
      debouncedQuery.length >= MIN_AUTOCOMPLETE_QUERY_LENGTH || debouncedQuery.length === 0
        ? debouncedQuery
        : ""
    );
  }, [debouncedQuery, onSearchChange]);

  useEffect(() => {
    if (location.pathname !== "/products") {
      return;
    }

    if (syncedSearchParamRef.current === normalizedSearchParam) {
      return;
    }

    syncedSearchParamRef.current = normalizedSearchParam;
    if (normalizeSearchQuery(query) !== normalizedSearchParam) {
      setQuery(normalizedSearchParam);
    }
  }, [location.pathname, normalizedSearchParam, query, setQuery]);

  useEffect(() => {
    if (location.pathname !== "/products") {
      return;
    }

    const nextSearchValue =
      debouncedQuery.length >= MIN_AUTOCOMPLETE_QUERY_LENGTH ? debouncedQuery : "";
    if (nextSearchValue === normalizedSearchParam) {
      return;
    }

    syncedSearchParamRef.current = nextSearchValue;
    const nextParams = new URLSearchParams(searchParams);
    if (nextSearchValue) {
      nextParams.set("search", nextSearchValue);
    } else {
      nextParams.delete("search");
    }

    setSearchParams(nextParams, { replace: true });
  }, [debouncedQuery, location.pathname, normalizedSearchParam, searchParams, setSearchParams]);

  useEffect(() => {
    const handlePointerDown = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) {
        close();
      }
    };

    document.addEventListener("mousedown", handlePointerDown);
    return () => {
      document.removeEventListener("mousedown", handlePointerDown);
    };
  }, [close]);

  useEffect(() => {
    if (
      !isOpen ||
      isLoading ||
      normalizedQuery.length < MIN_AUTOCOMPLETE_QUERY_LENGTH ||
      autocomplete.products.length === 0
    ) {
      return;
    }

    const trackingQuery = normalizeSearchQuery(autocomplete.query || normalizedQuery);
    const productIds = autocomplete.products.map((product) => product.productId).filter(Boolean);
    if (trackingQuery.length < MIN_AUTOCOMPLETE_QUERY_LENGTH || productIds.length === 0) {
      return;
    }

    const sessionId = getSearchSessionId();
    const trackingKey = `${sessionId}:${trackingQuery}:${productIds.join(",")}`;
    if (!rememberTrackingKey(impressionTrackingKeysRef, trackingKey)) {
      return;
    }

    void commerceHubService.trackSearchImpression({
      query: trackingQuery,
      productIds,
      sessionId,
      timestamp: new Date().toISOString()
    });
  }, [autocomplete.products, autocomplete.query, isLoading, isOpen, normalizedQuery]);

  const sections = useMemo<AutocompleteSectionData[]>(() => {
    if (normalizedQuery.length >= MIN_AUTOCOMPLETE_QUERY_LENGTH) {
      const nextSections: AutocompleteSectionData[] = [];

      if (autocomplete.products.length > 0) {
        nextSections.push({
          key: "products",
          title: "Products",
          icon: <PackageSearch className="h-3.5 w-3.5" />,
          items: autocomplete.products.map((product) => ({
            id: `autocomplete-product-${product.id}`,
            kind: "product",
            product
          }))
        });
      }

      if (autocomplete.categories.length > 0) {
        nextSections.push({
          key: "categories",
          title: "Categories",
          icon: <FolderTree className="h-3.5 w-3.5" />,
          items: autocomplete.categories.map((category) => ({
            id: `autocomplete-category-${category.categoryId}`,
            kind: "category",
            category
          }))
        });
      }

      if (autocomplete.suggestions.length > 0) {
        nextSections.push({
          key: "suggestions",
          title: "Suggestions",
          icon: <Sparkles className="h-3.5 w-3.5" />,
          items: autocomplete.suggestions.map((value, index) => ({
            id: `autocomplete-suggestion-${index}-${value}`,
            kind: "suggestion",
            value
          }))
        });
      }

      return nextSections;
    }

    if (normalizedQuery.length > 0) {
      return [];
    }

    const nextSections: AutocompleteSectionData[] = [];

    if (recentSearches.length > 0) {
      nextSections.push({
        key: "recent",
        title: "Recent Searches",
        icon: <Clock3 className="h-3.5 w-3.5" />,
        actionLabel: "Clear",
        onAction: clearRecentSearches,
        items: recentSearches.map((value, index) => ({
          id: `autocomplete-recent-${index}-${value}`,
          kind: "recent",
          value
        }))
      });
    }

    if (popularSearches.length > 0) {
      nextSections.push({
        key: "popular",
        title: "Trending Searches",
        icon: <TrendingUp className="h-3.5 w-3.5" />,
        items: popularSearches.map((value, index) => ({
          id: `autocomplete-popular-${index}-${value}`,
          kind: "popular",
          value
        }))
      });
    }

    return nextSections;
  }, [
    autocomplete.categories,
    autocomplete.products,
    autocomplete.suggestions,
    clearRecentSearches,
    normalizedQuery.length,
    popularSearches,
    recentSearches
  ]);

  const items = useMemo(() => sections.flatMap((section) => section.items), [sections]);
  const activeItemId = selectedIndex >= 0 ? items[selectedIndex]?.id : undefined;

  useEffect(() => {
    if (items.length === 0) {
      if (selectedIndex !== -1) {
        resetSelection();
      }
      return;
    }

    if (selectedIndex >= items.length) {
      setSelectedIndex(items.length - 1);
    }
  }, [items, resetSelection, selectedIndex, setSelectedIndex]);

  useEffect(() => {
    if (selectedIndex !== -1) {
      resetSelection();
    }
  }, [debouncedQuery, resetSelection, selectedIndex]);

  const navigateToProducts = (params: URLSearchParams) => {
    if (location.pathname === "/products") {
      setSearchParams(params);
      return;
    }

    const queryString = params.toString();
    navigate(queryString ? `/products?${queryString}` : "/products");
  };

  const handleSearchSubmit = (value = normalizedQuery) => {
    const normalizedValue = normalizeSearchQuery(value);
    if (normalizedValue.length < 2) {
      return;
    }

    rememberSearch(normalizedValue);
    close();
    resetSelection();
    analyticsService.trackSearchSubmitted(normalizedValue, "header_search");

    const nextParams = new URLSearchParams(location.pathname === "/products" ? searchParams : undefined);
    nextParams.set("search", normalizedValue);
    navigateToProducts(nextParams);
  };

  const handleProductSelect = (item: Extract<AutocompleteEntry, { kind: "product" }>, index: number) => {
    const trackingQuery = normalizeSearchQuery(autocomplete.query || normalizedQuery);
    const sessionId = getSearchSessionId();
    const productPosition = autocomplete.products.findIndex((product) => product.productId === item.product.productId);
    const trackingPosition = productPosition >= 0 ? productPosition : index;

    close();
    resetSelection();
    rememberSearch(trackingQuery);

    if (location.pathname === "/products") {
      saveProductsScrollPosition(`${location.pathname}${location.search}`);
    }

    analyticsService.trackProductClicked(item.product.productId, item.product.variantId, "search_autocomplete");

    const clickTrackingKey = `${sessionId}:${trackingQuery}:${item.product.productId}`;
    if (trackingQuery && rememberTrackingKey(clickTrackingKeysRef, clickTrackingKey)) {
      void commerceHubService.trackSearchClick({
        query: trackingQuery,
        productId: item.product.productId,
        variantId: item.product.variantId,
        position: trackingPosition,
        sessionId
      });
    }

    navigate(`/product/${item.product.id}`);
  };

  const handleCategorySelect = (item: Extract<AutocompleteEntry, { kind: "category" }>) => {
    close();
    resetSelection();
    analyticsService.trackCategorySelected(item.category.categoryId, item.category.categoryName, "search_autocomplete");

    const nextParams = new URLSearchParams();
    nextParams.set("category", item.category.categoryId);
    navigateToProducts(nextParams);
  };

  const handleTextSelect = (value: string) => {
    const normalizedValue = normalizeSearchQuery(value);
    setQuery(normalizedValue);
    handleSearchSubmit(normalizedValue);
  };

  const handleSelect = (item: AutocompleteEntry, index: number) => {
    if (item.kind === "product") {
      handleProductSelect(item, index);
      return;
    }

    if (item.kind === "category") {
      handleCategorySelect(item);
      return;
    }

    handleTextSelect(item.value);
  };

  const showDropdown =
    isOpen &&
    (
      sections.length > 0 ||
      (normalizedQuery.length >= MIN_AUTOCOMPLETE_QUERY_LENGTH && (isLoading || isError)) ||
      normalizedQuery.length === 0
    );

  return (
    <div className="relative w-full" ref={containerRef}>
      <div className="relative">
        <Search className="pointer-events-none absolute left-4 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          role="combobox"
          aria-autocomplete="list"
          aria-expanded={showDropdown}
          aria-controls={dropdownId}
          aria-activedescendant={activeItemId}
          aria-label={`Search ${brand.fullName}`}
          className="h-12 rounded-full border-white/80 bg-white/95 pl-11 pr-12 text-sm shadow-soft placeholder:text-muted-foreground/90 md:h-11"
          value={query}
          onChange={(event) => {
            open();
            setQuery(event.target.value);
          }}
          onFocus={() => open()}
          onKeyDown={(event) => {
            if (event.key === "Escape") {
              event.preventDefault();
              close();
              return;
            }

            if (event.key === "ArrowDown") {
              event.preventDefault();
              open();

              if (items.length > 0) {
                setSelectedIndex(selectedIndex >= items.length - 1 ? 0 : selectedIndex + 1);
              }

              return;
            }

            if (event.key === "ArrowUp") {
              event.preventDefault();
              open();

              if (items.length > 0) {
                setSelectedIndex(selectedIndex <= 0 ? items.length - 1 : selectedIndex - 1);
              }

              return;
            }

            if (event.key === "Enter") {
              event.preventDefault();

              if (selectedIndex >= 0 && items[selectedIndex]) {
                handleSelect(items[selectedIndex], selectedIndex);
                return;
              }

              handleSearchSubmit();
            }
          }}
          placeholder={placeholder}
        />
        <button
          type="button"
          onClick={() => handleSearchSubmit()}
          className="absolute right-2 top-1/2 -translate-y-1/2 rounded-full bg-accent/15 p-2 text-accent-foreground transition-colors hover:bg-accent/30"
          aria-label="Search products"
        >
          <Sparkles className="h-4 w-4" />
        </button>
      </div>

      <div className="mt-2 min-h-2 px-3">
        {isDebouncing && normalizedQuery.length >= MIN_AUTOCOMPLETE_QUERY_LENGTH ? (
          <div className="skeleton-wave h-1.5 w-full rounded-full" aria-hidden="true" />
        ) : (
          <p className="text-xs text-muted-foreground">
            {normalizedQuery.length === 0
              ? "Focus the field to see recent and trending searches."
              : normalizedQuery.length < MIN_AUTOCOMPLETE_QUERY_LENGTH
                ? `Type at least ${MIN_AUTOCOMPLETE_QUERY_LENGTH} characters for live suggestions.`
                : "Use arrow keys to navigate and Enter to open the highlighted result."}
          </p>
        )}
      </div>

      {showDropdown ? (
        <div className="absolute left-1/2 top-[calc(100%+0.75rem)] z-50 w-[min(100vw-1rem,100%)] -translate-x-1/2 overflow-hidden rounded-[1.4rem] border border-white/70 bg-background/95 shadow-card backdrop-blur-xl sm:left-0 sm:w-full sm:translate-x-0 sm:rounded-[1.75rem]">
          <AutocompleteDropdown
            id={dropdownId}
            query={normalizedQuery}
            sections={sections}
            selectedIndex={selectedIndex}
            isLoading={isLoading}
            isError={isError}
            errorMessage={error instanceof Error ? error.message : "Search suggestions are currently unavailable."}
            onRetry={() => {
              void refetch();
            }}
            onSelect={handleSelect}
            onHover={setSelectedIndex}
          />
        </div>
      ) : null}
    </div>
  );
});

SearchAutocomplete.displayName = "SearchAutocomplete";
