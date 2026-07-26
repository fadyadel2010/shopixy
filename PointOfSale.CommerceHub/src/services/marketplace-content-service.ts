import { marketplaceContentEndpoints } from "@/api/endpoints";
import { httpClient } from "@/api/http-client";
import type { MarketplaceCmsPage, MarketplaceFooter } from "@/types";

export const marketplaceContentService = {
  async getFooter(signal?: AbortSignal) {
    const response = await httpClient.get<MarketplaceFooter>(marketplaceContentEndpoints.footer, { signal });
    return response.data;
  },

  async getPage(slug: string, signal?: AbortSignal) {
    const response = await httpClient.get<MarketplaceCmsPage>(marketplaceContentEndpoints.page(slug), { signal });
    return response.data;
  }
};
