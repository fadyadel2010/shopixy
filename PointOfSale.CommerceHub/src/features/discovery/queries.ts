import { infiniteQueryOptions, queryOptions, type QueryClient } from "@tanstack/react-query";

import { commerceHubService } from "@/services/commercehub-service";
import type { CommerceHubSearchQuery } from "@/types";
import { categoriesService } from "@/services/categories-service";

export const categoryKeys = {
  all: ["commercehub", "categories"] as const
};

export const commerceHubSearchKeys = {
  all: ["commercehub", "search"] as const,
  list: (query: CommerceHubSearchQuery) => [...commerceHubSearchKeys.all, "list", query] as const
};

export const categoryTreeQueryOptions = () =>
  queryOptions({
    queryKey: categoryKeys.all,
    queryFn: () => categoriesService.getCategoryTree(),
    staleTime: 5 * 60 * 1000
  });

export const searchResultsQueryOptions = (query: CommerceHubSearchQuery) =>
  queryOptions({
    queryKey: commerceHubSearchKeys.list(query),
    queryFn: ({ signal }) => commerceHubService.search(query, signal)
  });

export const searchResultsPageQueryOptions = (
  query: CommerceHubSearchQuery,
  page: number,
  pageSize = 12
) =>
  searchResultsQueryOptions({
    ...query,
    page,
    pageSize
  });

export const infiniteSearchResultsQueryOptions = (
  queryClient: QueryClient,
  query: CommerceHubSearchQuery,
  pageSize = 12
) =>
  infiniteQueryOptions({
    queryKey: [...commerceHubSearchKeys.list(query), "infinite", pageSize] as const,
    initialPageParam: 1,
    queryFn: ({ pageParam }) =>
      queryClient.fetchQuery(searchResultsPageQueryOptions(query, pageParam as number, pageSize)),
    getNextPageParam: (lastPage, allPages) => {
      const loadedItemCount = allPages.reduce((total, page) => total + page.items.length, 0);
      return loadedItemCount < lastPage.total ? allPages.length + 1 : undefined;
    }
  });
