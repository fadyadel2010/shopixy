import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => {
    const languageState = { language: 'en' as 'en' | 'ar' };
    const languageStore = Object.assign(
        (selector: (state: typeof languageState) => unknown) => selector(languageState),
        {
            getState: () => languageState,
            setState: (next: Partial<typeof languageState>) => Object.assign(languageState, next),
        }
    );

    return {
        applyTheme: vi.fn(),
        fetchCategories: vi.fn(),
        fetchHomepageProductsByIds: vi.fn(),
        fetchNavigation: vi.fn(),
        fetchProductsDiscover: vi.fn(),
        fetchStoreConfig: vi.fn(),
        fetchStorefrontFooter: vi.fn(),
        languageState,
        languageStore,
        logLayoutVariants: vi.fn(),
    };
});

vi.mock('@/services/api/storefrontApi', () => ({
    fetchCategories: mocks.fetchCategories,
    fetchHomepageProductsByIds: mocks.fetchHomepageProductsByIds,
    fetchNavigation: mocks.fetchNavigation,
    fetchProductsDiscover: mocks.fetchProductsDiscover,
    fetchStoreConfig: mocks.fetchStoreConfig,
    fetchStorefrontFooter: mocks.fetchStorefrontFooter,
}));

vi.mock('@/i18n/languageStore', () => ({
    useLanguageStore: mocks.languageStore,
}));

vi.mock('@/theme', () => ({
    applyTheme: mocks.applyTheme,
    logLayoutVariants: mocks.logLayoutVariants,
}));

import { setResolvedTenant } from '@/bootstrap/tenantResolver';
import {
    invalidateCategoryCache,
    refreshCategoryCache,
} from '@/hooks/useCategories';
import {
    invalidateHomepageSectionProductsCache,
    refreshHomepageSectionProductsCache,
} from '@/hooks/useHomepageSectionProducts';
import { useStoreConfigStore } from '@/store/storeConfigStore';
import {
    forceRefreshStoreConfig,
    invalidateStoreConfigRuntimeCache,
    refreshStoreConfig,
} from '@/storefront/runtime/cache/storeConfigRuntime';
import {
    BROWSER_RUNTIME_CACHE_MAX_AGE_MS,
    isRuntimeCacheFresh,
} from '@/storefront/runtime/cache/runtimeCachePolicy';
import { useFooterStore } from '@/stores/footerStore';
import { useNavigationStore } from '@/stores/navigationStore';
import { DEFAULT_STORE_CONFIG } from '@/types/storeConfig';
import type {
    CategoryTreeDto,
    Navigation,
    ProductListItemDto,
    StoreConfig,
    StorefrontFooter,
} from '@/types';

const TENANT_ID = '11111111-1111-4111-8111-111111111111';
const PRODUCT_ID = '22222222-2222-4222-8222-222222222222';

function createStoreConfig(storeName: string): StoreConfig {
    return {
        ...DEFAULT_STORE_CONFIG,
        tenantId: TENANT_ID,
        storeName,
        updatedAtUtc: new Date().toISOString(),
    };
}

function createNavigation(label: string): Navigation {
    return {
        header: {
            links: [{
                label,
                labelAr: `${label}-ar`,
                type: 'internal',
                path: '/',
                openInNewTab: false,
            }],
        },
        footer: { links: [] },
        updatedAtUtc: new Date().toISOString(),
    };
}

function createFooter(title: string): StorefrontFooter {
    return {
        footerSource: 'Tenant',
        socialSource: 'None',
        sections: [{ title, sortOrder: 0, links: [] }],
        socialLinks: [],
        generatedAtUtc: new Date().toISOString(),
    };
}

function createCategories(name: string): CategoryTreeDto[] {
    return [{ id: TENANT_ID, name, slug: name.toLowerCase(), children: [] }];
}

function createProducts(name: string): ProductListItemDto[] {
    return [{
        id: PRODUCT_ID,
        name,
        price: 10,
        effectivePrice: 10,
        pricing: { currentPrice: 10 },
        slug: name.toLowerCase(),
        isAvailable: true,
        inStock: true,
        stockQuantity: 1,
        hasVariants: false,
    }];
}

beforeEach(() => {
    vi.clearAllMocks();
    setResolvedTenant(TENANT_ID, { overwrite: true });
    mocks.languageStore.setState({ language: 'en' });

    invalidateStoreConfigRuntimeCache();
    useStoreConfigStore.getState().reset();

    useNavigationStore.setState({
        navigation: null,
        loading: false,
        error: null,
        cacheIdentity: null,
        loadedAt: null,
    });
    useNavigationStore.getState().invalidateNavigation();

    useFooterStore.setState({
        footer: null,
        loading: false,
        error: null,
        cacheIdentity: null,
        loadedAt: null,
    });
    useFooterStore.getState().invalidateFooter();

    invalidateCategoryCache();
    invalidateHomepageSectionProductsCache();
});

describe('browser runtime cache recovery', () => {
    it('bounds successful entries with a lazy max-age policy and no timer', () => {
        const loadedAt = 1_000;

        expect(isRuntimeCacheFresh(loadedAt, 'same', 'same', loadedAt + 1)).toBe(true);
        expect(isRuntimeCacheFresh(
            loadedAt,
            'same',
            'same',
            loadedAt + BROWSER_RUNTIME_CACHE_MAX_AGE_MS
        )).toBe(false);
        expect(isRuntimeCacheFresh(loadedAt, 'old', 'new', loadedAt + 1)).toBe(false);
    });

    it('refreshes, invalidates, and force-refreshes StoreConfig', async () => {
        const first = createStoreConfig('First');
        const second = createStoreConfig('Second');
        const third = createStoreConfig('Third');
        mocks.fetchStoreConfig
            .mockResolvedValueOnce(first)
            .mockResolvedValueOnce(second)
            .mockResolvedValueOnce(third);

        await refreshStoreConfig();
        await refreshStoreConfig();
        expect(mocks.fetchStoreConfig).toHaveBeenCalledTimes(1);

        await forceRefreshStoreConfig();
        expect(useStoreConfigStore.getState().config.storeName).toBe('Second');

        invalidateStoreConfigRuntimeCache();
        await refreshStoreConfig();
        expect(useStoreConfigStore.getState().config.storeName).toBe('Third');
        expect(mocks.fetchStoreConfig).toHaveBeenCalledTimes(3);
    });

    it('deduplicates concurrent StoreConfig refreshes', async () => {
        let resolveRequest!: (config: StoreConfig) => void;
        mocks.fetchStoreConfig.mockReturnValue(new Promise<StoreConfig>((resolveConfig) => {
            resolveRequest = resolveConfig;
        }));

        const first = refreshStoreConfig();
        const second = refreshStoreConfig();
        expect(mocks.fetchStoreConfig).toHaveBeenCalledTimes(1);

        resolveRequest(createStoreConfig('Shared'));
        await Promise.all([first, second]);
        expect(useStoreConfigStore.getState().config.storeName).toBe('Shared');
    });

    it('does not let runtime refresh overwrite a controlled preview candidate', async () => {
        const preview = createStoreConfig('Preview');
        const runtime = createStoreConfig('Runtime');
        useStoreConfigStore.getState().setConfig(preview);
        mocks.fetchStoreConfig.mockResolvedValue(runtime);

        const preserved = await forceRefreshStoreConfig();
        expect(preserved.storeName).toBe('Preview');
        expect(mocks.fetchStoreConfig).not.toHaveBeenCalled();

        await forceRefreshStoreConfig({ allowPreviewOverride: true });
        expect(useStoreConfigStore.getState().config.storeName).toBe('Runtime');
    });

    it('keeps the newest Navigation refresh when an invalidated request completes late', async () => {
        let resolveOld!: (navigation: Navigation) => void;
        const oldRequest = new Promise<Navigation>((resolveRequest) => {
            resolveOld = resolveRequest;
        });
        mocks.fetchNavigation
            .mockReturnValueOnce(oldRequest)
            .mockResolvedValueOnce(createNavigation('New'));

        const staleLoad = useNavigationStore.getState().loadNavigation();
        useNavigationStore.getState().invalidateNavigation();
        const currentLoad = useNavigationStore.getState().loadNavigation();
        await currentLoad;

        resolveOld(createNavigation('Old'));
        await staleLoad;

        expect(mocks.fetchNavigation).toHaveBeenCalledTimes(2);
        expect(useNavigationStore.getState().navigation?.header.links[0].label).toBe('New');
    });

    it('deduplicates concurrent Navigation force refreshes', async () => {
        let resolveRequest!: (navigation: Navigation) => void;
        mocks.fetchNavigation.mockReturnValue(new Promise<Navigation>((resolveNavigation) => {
            resolveRequest = resolveNavigation;
        }));

        const first = useNavigationStore.getState().refreshNavigation();
        const second = useNavigationStore.getState().refreshNavigation();
        expect(mocks.fetchNavigation).toHaveBeenCalledTimes(1);

        resolveRequest(createNavigation('Shared'));
        await Promise.all([first, second]);
    });

    it('refreshes Footer when the language identity changes', async () => {
        mocks.fetchStorefrontFooter
            .mockResolvedValueOnce(createFooter('English'))
            .mockResolvedValueOnce(createFooter('Arabic'));

        await useFooterStore.getState().loadFooter();
        mocks.languageStore.setState({ language: 'ar' });
        await useFooterStore.getState().loadFooter();

        expect(mocks.fetchStorefrontFooter).toHaveBeenCalledTimes(2);
        expect(useFooterStore.getState().footer?.sections[0].title).toBe('Arabic');
    });

    it('deduplicates concurrent Footer force refreshes', async () => {
        let resolveRequest!: (footer: StorefrontFooter) => void;
        mocks.fetchStorefrontFooter.mockReturnValue(new Promise<StorefrontFooter>((resolveFooter) => {
            resolveRequest = resolveFooter;
        }));

        const first = useFooterStore.getState().refreshFooter();
        const second = useFooterStore.getState().refreshFooter();
        expect(mocks.fetchStorefrontFooter).toHaveBeenCalledTimes(1);

        resolveRequest(createFooter('Shared'));
        await Promise.all([first, second]);
    });

    it('deduplicates Category requests and reloads after invalidation', async () => {
        let resolveRequest!: (categories: CategoryTreeDto[]) => void;
        mocks.fetchCategories
            .mockReturnValueOnce(new Promise<CategoryTreeDto[]>((resolveCategories) => {
                resolveRequest = resolveCategories;
            }))
            .mockResolvedValueOnce(createCategories('Refreshed'));

        const first = refreshCategoryCache();
        const second = refreshCategoryCache();
        expect(mocks.fetchCategories).toHaveBeenCalledTimes(1);

        resolveRequest(createCategories('Initial'));
        await Promise.all([first, second]);

        invalidateCategoryCache();
        const refreshed = await refreshCategoryCache({ force: false });
        expect(refreshed[0].name).toBe('Refreshed');
        expect(mocks.fetchCategories).toHaveBeenCalledTimes(2);
    });

    it('deduplicates Homepage requests and scopes them by language', async () => {
        mocks.fetchHomepageProductsByIds
            .mockResolvedValueOnce(createProducts('English'))
            .mockResolvedValueOnce(createProducts('Arabic'))
            .mockResolvedValueOnce(createProducts('Invalidated'));

        const options = { productIds: [PRODUCT_ID], source: 'curated' as const };
        const first = refreshHomepageSectionProductsCache(options);
        const duplicate = refreshHomepageSectionProductsCache(options);
        await Promise.all([first, duplicate]);
        expect(mocks.fetchHomepageProductsByIds).toHaveBeenCalledTimes(1);

        mocks.languageStore.setState({ language: 'ar' });
        const localized = await refreshHomepageSectionProductsCache({ ...options, force: false });
        expect(localized[0].name).toBe('Arabic');
        expect(mocks.fetchHomepageProductsByIds).toHaveBeenCalledTimes(2);

        invalidateHomepageSectionProductsCache();
        const invalidated = await refreshHomepageSectionProductsCache({ ...options, force: false });
        expect(invalidated[0].name).toBe('Invalidated');
        expect(mocks.fetchHomepageProductsByIds).toHaveBeenCalledTimes(3);
    });

    it('contains no polling, timer, websocket, or SignalR mechanism in recovered sources', () => {
        const files = [
            'src/bootstrap/bootstrap.ts',
            'src/storefront/runtime/cache/runtimeCachePolicy.ts',
            'src/storefront/runtime/cache/storeConfigRuntime.ts',
            'src/stores/navigationStore.ts',
            'src/stores/footerStore.ts',
            'src/hooks/useCategories.ts',
            'src/hooks/useHomepageSectionProducts.ts',
        ];
        const source = files
            .map((file) => readFileSync(resolve(process.cwd(), file), 'utf8'))
            .join('\n');

        expect(source).not.toMatch(/\bsetInterval\b|\bsetTimeout\b/);
        expect(source).not.toMatch(/\bWebSocket\b|\bEventSource\b|\bSignalR\b|\bHubConnectionBuilder\b/i);
    });
});
