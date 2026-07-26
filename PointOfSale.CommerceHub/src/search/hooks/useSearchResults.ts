import { useQuery } from "@tanstack/react-query";

import { commerceHubProductKeys } from "@/products";
import { commerceHubService } from "@/services/commercehub-service";

type UseSearchResultsOptions = {
  limit?: number;
};

export const useSearchResults = (query: string, { limit = 6 }: UseSearchResultsOptions = {}) => {
  const normalizedQuery = query.trim();

  return useQuery({
    queryKey: commerceHubProductKeys.list({
      search: normalizedQuery,
      page: 1,
      pageSize: limit
    }),
    queryFn: () =>
      commerceHubService.getProducts({
        search: normalizedQuery,
        page: 1,
        pageSize: limit
      }),
    enabled: normalizedQuery.length >= 2,
    staleTime: 15000
  });
};
