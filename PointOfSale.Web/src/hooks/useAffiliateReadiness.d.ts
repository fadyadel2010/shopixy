export function useAffiliateReadiness(): {
    loading: boolean; isReady: boolean; storefrontLive: boolean; hasPaymentMethod: boolean; hasShippingMethod: boolean;
    missingRequirements: string[]; error: unknown; refetch(options?: { showLoading?: boolean }): Promise<void>;
};
