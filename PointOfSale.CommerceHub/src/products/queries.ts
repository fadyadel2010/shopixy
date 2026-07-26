import { infiniteQueryOptions, queryOptions, type QueryClient } from "@tanstack/react-query";

import { commerceHubService } from "@/services/commercehub-service";
import type { CommerceHubProductsQuery } from "@/types";

export const commerceHubProductKeys = {
  all: ["commercehub", "products"] as const,
  list: (query: CommerceHubProductsQuery) => [...commerceHubProductKeys.all, "list", query] as const,
  detail: (id: string) => [...commerceHubProductKeys.all, "detail", id] as const
};

export const productsQueryOptions = (query: CommerceHubProductsQuery) =>
  queryOptions({
    queryKey: commerceHubProductKeys.list(query),
    queryFn: () => commerceHubService.getProducts(query)
  });

export const productsPageQueryOptions = (
  query: CommerceHubProductsQuery,
  page: number,
  pageSize = 12
) =>
  productsQueryOptions({
    ...query,
    page,
    pageSize
  });

export const featuredProductsQueryOptions = (limit = 8) =>
  queryOptions({
    queryKey: commerceHubProductKeys.list({ page: 1, pageSize: limit }),
    queryFn: () =>
      commerceHubService.getProducts({
        page: 1,
        pageSize: limit
      })
  });

export const infiniteProductsQueryOptions = (
  queryClient: QueryClient,
  query: CommerceHubProductsQuery,
  pageSize = 12
) =>
  infiniteQueryOptions({
    queryKey: [...commerceHubProductKeys.list(query), "infinite", pageSize] as const,
    initialPageParam: 1,
    queryFn: ({ pageParam }) =>
      queryClient.ensureQueryData(productsPageQueryOptions(query, pageParam as number, pageSize)),
    getNextPageParam: (lastPage, allPages) => {
      const loadedItemCount = allPages.reduce((total, page) => total + page.items.length, 0);
      return loadedItemCount < lastPage.totalCount ? allPages.length + 1 : undefined;
    }
  });

export const productDetailQueryOptions = (id: string) =>
  queryOptions({
    queryKey: commerceHubProductKeys.detail(id),
    queryFn: () => commerceHubService.getProductById(id),
    enabled: Boolean(id)
  });
