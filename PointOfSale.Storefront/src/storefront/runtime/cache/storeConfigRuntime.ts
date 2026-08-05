import { fetchStoreConfig } from '@/services/api/storefrontApi';
import { useStoreConfigStore } from '@/store/storeConfigStore';
import { applyTheme, logLayoutVariants } from '@/theme';
import type { StoreConfig } from '@/types';
import {
    getRuntimeCacheIdentity,
    isRuntimeCacheFresh,
    type RuntimeCacheLoadOptions,
} from './runtimeCachePolicy';

export interface StoreConfigRefreshOptions extends RuntimeCacheLoadOptions {
    allowPreviewOverride?: boolean;
}

interface StoreConfigRequest {
    generation: number;
    identity: string;
    promise: Promise<StoreConfig>;
}

let generation = 0;
let loadedAt: number | null = null;
let loadedIdentity: string | null = null;
let inFlightRequest: StoreConfigRequest | null = null;

function getStoreConfigIdentity(): string {
    return getRuntimeCacheIdentity('store-config', { includeThemePreview: true });
}

export function invalidateStoreConfigRuntimeCache(): void {
    generation += 1;
    loadedAt = null;
    loadedIdentity = null;
}

export async function refreshStoreConfig(
    options: StoreConfigRefreshOptions = {}
): Promise<StoreConfig> {
    const store = useStoreConfigStore.getState();

    // Editor/Builder preview candidates are authoritative for their controlled
    // preview surface. An ordinary runtime refresh must not overwrite them.
    if (store.configSource === 'preview' && !options.allowPreviewOverride) {
        return store.config;
    }

    const identity = getStoreConfigIdentity();

    if (!options.force
        && store.isLoaded
        && isRuntimeCacheFresh(loadedAt, loadedIdentity, identity)) {
        return store.config;
    }

    if (inFlightRequest
        && inFlightRequest.identity === identity
        && inFlightRequest.generation === generation) {
        return inFlightRequest.promise;
    }

    if (options.force) {
        invalidateStoreConfigRuntimeCache();
    }

    const requestGeneration = generation;
    store.setLoading(true);

    let requestRecord!: StoreConfigRequest;
    const promise = (async () => {
        try {
            const config = await fetchStoreConfig();

            if (inFlightRequest === requestRecord && generation === requestGeneration) {
                applyTheme(config.theme);
                logLayoutVariants(config.theme.layout);
                useStoreConfigStore.getState().setRuntimeConfig(config);
                loadedAt = Date.now();
                loadedIdentity = getStoreConfigIdentity();
            }

            return config;
        } catch (error) {
            if (inFlightRequest === requestRecord && generation === requestGeneration) {
                const message = error instanceof Error
                    ? error.message
                    : 'Failed to load store configuration';
                useStoreConfigStore.getState().setError(message);
            }

            throw error;
        } finally {
            if (inFlightRequest === requestRecord) {
                inFlightRequest = null;
            }
        }
    })();

    requestRecord = {
        generation: requestGeneration,
        identity,
        promise,
    };
    inFlightRequest = requestRecord;

    return promise;
}

export function forceRefreshStoreConfig(
    options: Omit<StoreConfigRefreshOptions, 'force'> = {}
): Promise<StoreConfig> {
    return refreshStoreConfig({ ...options, force: true });
}

export function getStoreConfigRuntimeCacheState(): Readonly<{
    generation: number;
    loadedAt: number | null;
    loadedIdentity: string | null;
    isRefreshing: boolean;
}> {
    return {
        generation,
        loadedAt,
        loadedIdentity,
        isRefreshing: inFlightRequest !== null,
    };
}
