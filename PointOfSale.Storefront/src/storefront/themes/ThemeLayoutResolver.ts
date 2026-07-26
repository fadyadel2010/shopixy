import type { ComponentType } from 'react';

type LayoutComponentModule = {
    default?: ComponentType;
};

type LayoutSlot = 'header' | 'footer';

const themedLayoutModules = import.meta.glob<LayoutComponentModule>('./*/layout/*.tsx');
const defaultLayoutModules = import.meta.glob<LayoutComponentModule>('../../components/layout/default/*.tsx', {
    eager: true,
});

const layoutCache = new Map<string, ComponentType | null>();

function getDefaultLayoutComponent(slot: LayoutSlot): ComponentType | null {
    const module = defaultLayoutModules[`../../components/layout/default/${slot}.tsx`];
    return module?.default ?? null;
}

async function resolveLayoutComponent(slot: LayoutSlot, themeId: string): Promise<ComponentType | null> {
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

export function getDefaultHeaderComponent(): ComponentType | null {
    return getDefaultLayoutComponent('header');
}

export function getDefaultFooterComponent(): ComponentType | null {
    return getDefaultLayoutComponent('footer');
}

export function resolveHeaderComponent(themeId: string): Promise<ComponentType | null> {
    return resolveLayoutComponent('header', themeId);
}

export function resolveFooterComponent(themeId: string): Promise<ComponentType | null> {
    return resolveLayoutComponent('footer', themeId);
}
