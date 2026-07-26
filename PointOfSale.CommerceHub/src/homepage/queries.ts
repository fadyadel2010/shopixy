import { queryOptions } from "@tanstack/react-query";

import { homepageService } from "@/services/homepage-service";

export const homepageKeys = {
  all: ["homepage", "sections"] as const,
  list: () => [...homepageKeys.all, "published"] as const
};

export const homepageSectionsQueryOptions = () =>
  queryOptions({
    queryKey: homepageKeys.list(),
    queryFn: ({ signal }) => homepageService.getSections(signal),
    staleTime: 30 * 1000
  });
