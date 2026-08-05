import type { ComponentType } from 'react';
import type { FooterPresentationProps, HeaderPresentationProps } from '@/storefront/presentation/models';

type LayoutComponentModule = {
    default?: ComponentType<any>;
};

type LayoutSlot = 'header' | 'footer';

const themedLayoutModules = import.meta.glob<LayoutComponentModule>('./*/layout/*.tsx');
const defaultLayoutModules = import.meta.glob<LayoutComponentModule>('../../components/layout/default/*.tsx', {
    eager: true,
});

const layoutCache = new Map<string, ComponentType<any> | null>();

function getDefaultLayoutComponent(slot: LayoutSlot): ComponentType<any> | null {
    const module = defaultLayoutModules[`../../components/layout/default/${slot}.tsx`];
    return module?.default ?? null;
}

async function resolveLayoutComponent(slot: LayoutSlot, themeId: string): Promise<ComponentType<any> | null> {
    const cacheKey = `${themeId}:${slot}`;

    if (layoutCache.has(cacheKey)) {
        return layoutCache.get(cacheKey) ?? null;
    }

    const themedModulePath = `./${themeId}/layout/${slot}.tsx`;
    const defaultComponent = getDefaultLayoutComponent(slot);
    const themedImporter = themedLayoutModules[themedModulePath];

    if (!themedImporter) {
        layoutCache.set(cacheKey, defaultComponent);
        return defaultComponent;
    }

    try {
        const themedModule = await themedImporter();
        const resolvedComponent = themedModule.default ?? defaultComponent;
        layoutCache.set(cacheKey, resolvedComponent);
        return resolvedComponent;
    } catch (error) {
        console.error(
            `[ThemeLayoutResolver] Failed to load themed ${slot} for theme "${themeId}". Falling back to default layout.`,
            error
        );
        layoutCache.set(cacheKey, defaultComponent);
        return defaultComponent;
    }
}

export function getDefaultHeaderComponent(): ComponentType<HeaderPresentationProps> | null {
    return getDefaultLayoutComponent('header');
}

export function getDefaultFooterComponent(): ComponentType<FooterPresentationProps> | null {
    return getDefaultLayoutComponent('footer');
}

export function resolveHeaderComponent(themeId: string): Promise<ComponentType<HeaderPresentationProps> | null> {
    return resolveLayoutComponent('header', themeId);
}

export function resolveFooterComponent(themeId: string): Promise<ComponentType<FooterPresentationProps> | null> {
    return resolveLayoutComponent('footer', themeId);
}
