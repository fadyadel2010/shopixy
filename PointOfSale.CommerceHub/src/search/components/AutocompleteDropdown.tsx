import type { ReactNode } from "react";
import {
  ArrowUpRight,
  Clock3,
  FolderTree,
  PackageSearch,
  Search,
  TrendingUp
} from "lucide-react";

import type {
  CommerceHubAutocompleteCategory,
  CommerceHubAutocompleteProduct
} from "@/types";
import { InlineErrorState } from "@/shared/InlineErrorState";
import { cn, formatCurrency } from "@/utils";
import { AutocompleteSection } from "./AutocompleteSection";
import { HighlightText } from "./HighlightText";
import { SearchSkeleton } from "./SearchSkeleton";
import { MIN_AUTOCOMPLETE_QUERY_LENGTH } from "../utils/search-query";

export type AutocompleteEntry =
  | {
      id: string;
      kind: "product";
      product: CommerceHubAutocompleteProduct;
    }
  | {
      id: string;
      kind: "category";
      category: CommerceHubAutocompleteCategory;
    }
  | {
      id: string;
      kind: "suggestion" | "recent" | "popular";
      value: string;
    };

export type AutocompleteSectionData = {
  key: string;
  title: string;
  icon: ReactNode;
  items: AutocompleteEntry[];
  actionLabel?: string;
  onAction?: () => void;
};

type AutocompleteDropdownProps = {
  id: string;
  query: string;
  sections: AutocompleteSectionData[];
  selectedIndex: number;
  isLoading: boolean;
  isError: boolean;
  errorMessage?: string;
  onRetry: () => void;
  onSelect: (item: AutocompleteEntry, index: number) => void;
  onHover: (index: number) => void;
};

const TextSuggestionIcon = ({ kind }: { kind: "suggestion" | "recent" | "popular" }) => {
  if (kind === "recent") {
    return <Clock3 className="h-4 w-4" />;
  }

  if (kind === "popular") {
    return <TrendingUp className="h-4 w-4" />;
  }

  return <Search className="h-4 w-4" />;
};

export const AutocompleteDropdown = ({
  id,
  query,
  sections,
  selectedIndex,
  isLoading,
  isError,
  errorMessage,
  onRetry,
  onSelect,
  onHover
}: AutocompleteDropdownProps) => {
  const flatItems = sections.flatMap((section) => section.items);

  if (isError) {
    return (
      <div className="p-3">
        <InlineErrorState
          title="Search unavailable"
          message={errorMessage ?? "Something went wrong while loading suggestions."}
          actionLabel="Retry search"
          onRetry={onRetry}
        />
      </div>
    );
  }

  if (isLoading && flatItems.length === 0) {
    return <SearchSkeleton rows={query.length >= MIN_AUTOCOMPLETE_QUERY_LENGTH ? 4 : 2} />;
  }

  if (flatItems.length === 0) {
    return (
      <div className="px-4 py-6 text-center text-sm text-muted-foreground">
        {query.length >= MIN_AUTOCOMPLETE_QUERY_LENGTH
          ? "No suggestions found yet."
          : "Recent and popular searches will appear here."}
      </div>
    );
  }

  let optionIndex = -1;

  return (
    <div
      id={id}
      role="listbox"
      aria-label="Search suggestions"
      className="max-h-[min(72vh,32rem)] overflow-x-hidden overflow-y-auto overscroll-contain p-2 sm:p-3"
    >
      <div className="space-y-4">
        {sections.map((section) => (
          <AutocompleteSection
            key={section.key}
            title={section.title}
            icon={section.icon}
            actionLabel={section.actionLabel}
            onAction={section.onAction}
          >
            {section.items.map((item) => {
              optionIndex += 1;
              const itemIndex = optionIndex;
              const isSelected = itemIndex === selectedIndex;

              if (item.kind === "product") {
                const { product } = item;

                return (
                  <button
                    key={item.id}
                    id={item.id}
                    type="button"
                    role="option"
                    aria-selected={isSelected}
                    className={cn(
                      "flex min-h-[4.5rem] w-full touch-manipulation items-center gap-3 rounded-[1.4rem] border px-3.5 py-3.5 text-left transition-colors sm:min-h-[4rem] sm:px-3 sm:py-3",
                      isSelected
                        ? "border-primary/30 bg-primary/8"
                        : "border-transparent bg-white hover:border-primary/20 hover:bg-primary/5"
                    )}
                    onMouseDown={(event) => event.preventDefault()}
                    onMouseEnter={() => onHover(itemIndex)}
                    onClick={() => onSelect(item, itemIndex)}
                  >
                    <div className="flex h-12 w-12 shrink-0 items-center justify-center overflow-hidden rounded-2xl bg-muted">
                      {product.image ? (
                        <img
                          src={product.image}
                          alt={product.productName}
                          className="h-full w-full object-cover"
                          loading="lazy"
                        />
                      ) : (
                        <PackageSearch className="h-5 w-5 text-muted-foreground" />
                      )}
                    </div>

                    <div className="min-w-0 flex-1 space-y-1">
                      <HighlightText
                        text={product.productName}
                        query={query}
                        highlightedText={product.productNameHighlight}
                        className="line-clamp-2 text-sm font-semibold text-foreground"
                      />
                      <div className="flex flex-wrap items-center gap-1 text-xs text-muted-foreground">
                        {product.categoryName ? (
                          <HighlightText
                            text={product.categoryName}
                            query={query}
                            highlightedText={product.categoryNameHighlight}
                            className="max-w-full truncate"
                          />
                        ) : null}
                        {product.categoryName && product.storeName ? <span>|</span> : null}
                        {product.storeName ? <span className="truncate">{product.storeName}</span> : null}
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
              }

              if (item.kind === "category") {
                const { category } = item;

                return (
                  <button
                    key={item.id}
                    id={item.id}
                    type="button"
                    role="option"
                    aria-selected={isSelected}
                    className={cn(
                      "flex min-h-[4rem] w-full touch-manipulation items-center gap-3 rounded-[1.4rem] border px-3.5 py-3.5 text-left transition-colors sm:px-3 sm:py-3",
                      isSelected
                        ? "border-primary/30 bg-primary/8"
                        : "border-transparent bg-white hover:border-primary/20 hover:bg-primary/5"
                    )}
                    onMouseDown={(event) => event.preventDefault()}
                    onMouseEnter={() => onHover(itemIndex)}
                    onClick={() => onSelect(item, itemIndex)}
                  >
                    <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-primary/8 text-primary">
                      <FolderTree className="h-4 w-4" />
                    </div>

                    <div className="min-w-0 flex-1">
                      <HighlightText
                        text={category.categoryName}
                        query={query}
                        className="truncate text-sm font-semibold text-foreground"
                      />
                      <p className="text-xs text-muted-foreground">
                        {category.count} matching {category.count === 1 ? "product" : "products"}
                      </p>
                    </div>

                    <ArrowUpRight className="h-4 w-4 shrink-0 text-muted-foreground" />
                  </button>
                );
              }

              return (
                <button
                  key={item.id}
                  id={item.id}
                  type="button"
                  role="option"
                  aria-selected={isSelected}
                  className={cn(
                    "flex min-h-[4rem] w-full touch-manipulation items-center gap-3 rounded-[1.4rem] border px-3.5 py-3.5 text-left transition-colors sm:px-3 sm:py-3",
                    isSelected
                      ? "border-primary/30 bg-primary/8"
                      : "border-transparent bg-white hover:border-primary/20 hover:bg-primary/5"
                  )}
                  onMouseDown={(event) => event.preventDefault()}
                  onMouseEnter={() => onHover(itemIndex)}
                  onClick={() => onSelect(item, itemIndex)}
                >
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-muted text-muted-foreground">
                    <TextSuggestionIcon kind={item.kind} />
                  </div>

                  <div className="min-w-0 flex-1">
                    <HighlightText
                      text={item.value}
                      query={query}
                      className="truncate text-sm font-semibold text-foreground"
                    />
                    <p className="text-xs text-muted-foreground">
                      {item.kind === "recent"
                        ? "Recent search"
                        : item.kind === "popular"
                          ? "Trending search"
                          : "Search suggestion"}
                    </p>
                  </div>

                  <ArrowUpRight className="h-4 w-4 shrink-0 text-muted-foreground" />
                </button>
              );
            })}
          </AutocompleteSection>
        ))}
      </div>
    </div>
  );
};
