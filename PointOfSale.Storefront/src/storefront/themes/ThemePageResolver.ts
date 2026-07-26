import type { ComponentType } from 'react';

type PageComponentModule = {
    default?: ComponentType;
};

const themedPageModules = import.meta.glob<PageComponentModule>('./*/pages/*.tsx');
const defaultPageModules = import.meta.glob<PageComponentModule>('../pages/default/*.tsx', {
    eager: true,
});

const pageCache = new Map<string, ComponentType | null>();

export function getDefaultPageComponent(pageName: string): ComponentType | null {
    const module = defaultPageModules[`../pages/default/${pageName}.tsx`];
    return module?.default ?? null;
}

export async function resolvePageComponent(themeId: string, pageName: string): Promise<ComponentType | null> {
    const cacheKey = `${themeId}:${pageName}`;

    if (pageCache.has(cacheKey)) {
        return pageCache.get(cacheKey) ?? null;
    }

    const themedModulePath = `./${themeId}/pages/${pageName}.tsx`;
    const defaultComponent = getDefaultPageComponent(pageName);
    const themedImporter = themedPageModules[themedModulePath];

    if (!themedImporter) {
        pageCache.set(cacheKey, defaultComponent);
        return defaultComponent;
    }

    try {
        const themedModule = await themedImporter();
        const resolvedComponent = themedModule.default ?? defaultComponent;
        pageCache.set(cacheKey, resolvedComponent);
        return resolvedComponent;
    } catch (error) {
        console.error(
            `[ThemePageResolver] Failed to load themed page "${pageName}" for theme "${themeId}". Falling back to default page.`,
            error
        );
        pageCache.set(cacheKey, defaultComponent);
        return defaultComponent;
    }
}
