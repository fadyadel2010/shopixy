import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '../../core/apiClient';
import { marketplaceKeys } from '../queryKeys';
import { marketplaceAuthorityQueryOptions, useMarketplaceAuthorityScope } from '../authority';

export interface MarketplaceProfile {
    profileId: string;
    displayName: string;
    bio?: string;
    isActive: boolean;
    hasMarketplaceProfile: boolean;
    profileImageUrl?: string;
    coverImageUrl?: string;
    location?: string;
    skills?: string; // JSON array
    portfolioLinks?: string; // JSON array
}

export interface CreateProfileRequest {
    displayName: string;
    bio?: string;
    profileImageUrl?: string;
    coverImageUrl?: string;
    location?: string;
    skills?: string; // JSON array
    portfolioLinks?: string; // JSON array
}

export interface UpdateProfileRequest {
    displayName: string;
    bio?: string;
    profileImageUrl?: string;
    coverImageUrl?: string;
    location?: string;
    skills?: string; // JSON array
    portfolioLinks?: string; // JSON array
}

async function fetchMyProfile(): Promise<MarketplaceProfile> {
    return await apiClient.get<MarketplaceProfile>('/marketplace/me');
}

async function createProfile(data: CreateProfileRequest): Promise<MarketplaceProfile> {
    return await apiClient.post<MarketplaceProfile>('/marketplace/profile', data);
}

async function updateProfile(data: UpdateProfileRequest): Promise<MarketplaceProfile> {
    return await apiClient.put<MarketplaceProfile>('/marketplace/profile', data);
}

export function useMarketplaceProfile() {
    const queryClient = useQueryClient();
    const authorityScope = useMarketplaceAuthorityScope();
    const profileQueryKey = marketplaceKeys.myProfile(authorityScope);

    const query = useQuery({
        queryKey: profileQueryKey,
        queryFn: fetchMyProfile,
        ...marketplaceAuthorityQueryOptions,
    });

    const createMutation = useMutation({
        mutationFn: createProfile,
        onSuccess: (newProfile) => {
            queryClient.setQueryData(profileQueryKey, {
                ...newProfile,
                hasMarketplaceProfile: true
            });
            queryClient.invalidateQueries({ queryKey: ['marketplace'] });
        },
    });

    const updateMutation = useMutation({
        mutationFn: updateProfile,
        onSuccess: (updatedProfile) => {
            queryClient.setQueryData(profileQueryKey, {
                ...updatedProfile,
                hasMarketplaceProfile: true
            });
            queryClient.invalidateQueries({ queryKey: ['marketplace'] });
        },
    });

    return {
        profile: query.data,
        isLoading: query.isLoading,
        error: query.error,
        createProfile: createMutation.mutateAsync,
        isCreating: createMutation.isPending,
        updateProfile: updateMutation.mutateAsync,
        isUpdating: updateMutation.isPending,
    };
}
