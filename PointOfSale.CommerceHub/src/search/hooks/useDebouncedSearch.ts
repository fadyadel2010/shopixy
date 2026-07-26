import { startTransition, useEffect, useState } from "react";

import { normalizeSearchInput, normalizeSearchQuery } from "../utils/search-query";

type UseDebouncedSearchOptions = {
  delay?: number;
  initialValue?: string;
};

export const useDebouncedSearch = ({
  delay = 350,
  initialValue = ""
}: UseDebouncedSearchOptions = {}) => {
  const [query, setQueryState] = useState(() => normalizeSearchInput(initialValue));
  const [debouncedQuery, setDebouncedQuery] = useState(() => normalizeSearchQuery(initialValue));
  const [isDebouncing, setIsDebouncing] = useState(false);

  useEffect(() => {
    setQueryState(normalizeSearchInput(initialValue));
    setDebouncedQuery(normalizeSearchQuery(initialValue));
  }, [initialValue]);

  useEffect(() => {
    if (query === debouncedQuery) {
      return;
    }

    setIsDebouncing(true);

    const timeoutId = window.setTimeout(() => {
      startTransition(() => {
        setDebouncedQuery(normalizeSearchQuery(query));
        setIsDebouncing(false);
      });
    }, delay);

    return () => {
      window.clearTimeout(timeoutId);
    };
  }, [debouncedQuery, delay, query]);

  return {
    query,
    setQuery: (value: string) => setQueryState(normalizeSearchInput(value)),
    debouncedQuery,
    isDebouncing
  };
};
