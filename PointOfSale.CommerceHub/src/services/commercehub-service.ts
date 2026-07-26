import { commerceHubEndpoints } from "@/api/endpoints";
import { httpClient } from "@/api/http-client";
import type {
  CommerceHubAutocompleteQuery,
  CommerceHubAutocompleteResponse,
  CommerceHubPopularSearchesQuery,
  CommerceHubPopularSearchesResponse,
  CommerceHubProductDetail,
  CommerceHubProductListResponse,
  CommerceHubProductsQuery,
  CommerceHubSearchClickRequest,
  CommerceHubSearchFilterEventRequest,
  CommerceHubSearchImpressionRequest,
  CommerceHubSearchQuery,
  CommerceHubSearchResponse,
  CommerceHubVariantResolutionRequest,
  CommerceHubVariantResolutionResponse
} from "@/types";
import { requireCommerceHubCatalogCurrency } from "@/utils/commerceHubCurrencyAuthority";

type CurrencyEvidence = { currency?: string | null };

const requireCatalogCurrency = (item: CurrencyEvidence) =>
  requireCommerceHubCatalogCurrency(item.currency);

const requireCatalogCurrencies = (items: CurrencyEvidence[]) => {
  items.forEach(requireCatalogCurrency);
};

const requireCatalogSearchResponseCurrency = (response: CommerceHubSearchResponse) => {
  requireCatalogCurrencies(response.items);
  requireCatalogCurrencies(response.zeroResultSuggestions?.products ?? []);
  return response;
};

export const commerceHubService = {
  async getProducts(query: CommerceHubProductsQuery = {}, signal?: AbortSignal) {
    const response = await httpClient.get<CommerceHubProductListResponse>(commerceHubEndpoints.products, {
      params: query,
      signal
    });

    requireCatalogCurrencies(response.data.items);
    return response.data;
  },

  async getProductById(id: string, signal?: AbortSignal) {
    const response = await httpClient.get<CommerceHubProductDetail>(commerceHubEndpoints.productById(id), {
      signal
    });
    requireCatalogCurrency(response.data);
    return response.data;
  },

  async resolveProductVariant(id: string, request: CommerceHubVariantResolutionRequest, signal?: AbortSignal) {
    const response = await httpClient.post<CommerceHubVariantResolutionResponse>(
      commerceHubEndpoints.resolveProductVariant(id),
      request,
      { signal }
    );

    return response.data;
  },

  async search(query: CommerceHubSearchQuery, signal?: AbortSignal) {
    const response = await httpClient.get<CommerceHubSearchResponse>(commerceHubEndpoints.search, {
      params: query,
      signal
    });

    return requireCatalogSearchResponseCurrency(response.data);
  },

  async getAutocomplete(query: CommerceHubAutocompleteQuery, signal?: AbortSignal) {
    const response = await httpClient.get<CommerceHubAutocompleteResponse>(commerceHubEndpoints.searchAutocomplete, {
      params: query,
      signal
    });

    requireCatalogCurrencies(response.data.products);
    return response.data;
  },

  async getPopularSearches(query: CommerceHubPopularSearchesQuery = {}, signal?: AbortSignal) {
    const response = await httpClient.get<CommerceHubPopularSearchesResponse>(commerceHubEndpoints.searchPopular, {
      params: query,
      signal
    });

    return response.data;
  },

  async trackSearchClick(payload: CommerceHubSearchClickRequest, signal?: AbortSignal) {
    await httpClient.post(commerceHubEndpoints.searchClick, payload, { signal });
  },

  async trackSearchImpression(payload: CommerceHubSearchImpressionRequest, signal?: AbortSignal) {
    await httpClient.post(commerceHubEndpoints.searchImpression, payload, { signal });
  },

  async trackSearchFilterEvent(payload: CommerceHubSearchFilterEventRequest, signal?: AbortSignal) {
    await httpClient.post(commerceHubEndpoints.searchFilterEvent, payload, { signal });
  }
};
