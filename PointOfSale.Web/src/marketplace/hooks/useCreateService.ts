import { useMutation, useQueryClient } from '@tanstack/react-query';
import { marketplaceApi } from '../api/marketplaceApi';
import { marketplaceKeys } from '../queryKeys';
import { useMarketplaceAuthorityScope } from '../authority';
import { CreateServiceRequest } from '../types';

/**
 * Hook for creating a service (provider).
 * 
 * HARDENING:
 * - Global invalidation on success
 * - No selective cache patching
 */
export function useCreateService() {
  const queryClient = useQueryClient();
  const authorityScope = useMarketplaceAuthorityScope();

  return useMutation({
    mutationFn: (data: CreateServiceRequest) => marketplaceApi.createService(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) });
    },
  });
}
