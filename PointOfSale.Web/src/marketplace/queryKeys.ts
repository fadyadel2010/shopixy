import type { ServiceSearchRequest } from './types';
import { getMarketplaceAuthorityScope } from './authority';

/**
 * Standardized query keys for marketplace.
 * Ensures consistent cache invalidation.
 */
export const marketplaceKeys = {
  all: ['marketplace'] as const,
  scope: (scope = getMarketplaceAuthorityScope()) => [...marketplaceKeys.all, scope] as const,
  myProfile: (scope = getMarketplaceAuthorityScope()) => [...marketplaceKeys.scope(scope), 'my-profile'] as const,
  myProfileStats: (scope = getMarketplaceAuthorityScope()) => [...marketplaceKeys.scope(scope), 'my-profile-stats'] as const,
  search: (params: ServiceSearchRequest, scope = getMarketplaceAuthorityScope()) => [...marketplaceKeys.scope(scope), 'search', params] as const,
  service: (serviceId: string, scope = getMarketplaceAuthorityScope()) => [...marketplaceKeys.scope(scope), 'service', serviceId] as const,
  myServices: (scope = getMarketplaceAuthorityScope()) => [...marketplaceKeys.scope(scope), 'my-services'] as const,
  myRequests: (scope = getMarketplaceAuthorityScope()) => [...marketplaceKeys.scope(scope), 'my-requests'] as const,
  incomingRequests: (scope = getMarketplaceAuthorityScope()) => [...marketplaceKeys.scope(scope), 'incoming-requests'] as const,
  request: (requestId: string, scope = getMarketplaceAuthorityScope()) => [...marketplaceKeys.scope(scope), 'request', requestId] as const,
  chat: (requestId: string, scope = getMarketplaceAuthorityScope()) => [...marketplaceKeys.scope(scope), 'chat', requestId] as const,
};
