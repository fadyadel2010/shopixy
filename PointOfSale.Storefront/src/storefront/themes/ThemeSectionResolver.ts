import type { ComponentType } from 'react';
import { getSectionResolutionCandidates } from '@/components/sections/homepageSections';

type SectionModule = {
    default: ComponentType<any>;
};

const defaultSectionModules = import.meta.glob<SectionModule>(
    '../../components/sections/defaults/*.tsx',
    { eager: true }
);

const themedSectionModules = import.meta.glob<SectionModule>('./*/sections/*.tsx');

const resolvedSectionCache = new Map<string, Promise<ComponentType<any> | null>>();

export function getDefaultSectionComponent(sectionType: string): ComponentType<any> | null {
    const candidates = getSectionResolutionCandidates(sectionType);

    for (const candidate of candidates) {
        const module = defaultSectionModules[`../../components/sections/defaults/${candidate}.tsx`];
        if (module?.default) {
            return module.default;
        }
    }

    return null;
}

export async function resolveSectionComponent(
    sectionType: string,
    themeId: string
): Promise<ComponentType<any> | null> {
    const cacheKey = `${themeId}:${sectionType}`;
    const cachedResolution = resolvedSectionCache.get(cacheKey);

    if (cachedResolution) {
        return cachedResolution;
    }

    const resolution = (async () => {
        for (const candidate of getSectionResolutionCandidates(sectionType)) {
            const themedModulePath = `./${themeId}/sections/${candidate}.tsx`;
            const themedModuleLoader = themedSectionModules[themedModulePath];

            if (!themedModuleLoader) {
                continue;
            }

            try {
                const themedModule = await themedModuleLoader();
                if (themedModule.default) {
                    return themedModule.default;
                }
            } catch (error) {
                console.error(
                    `[ThemeSectionResolver] Failed to load themed section "${candidate}" for theme "${themeId}". Falling back to default section.`,
                    error
                );
            }
        }

        const defaultSectionComponent = getDefaultSectionComponent(sectionType);

        if (defaultSectionComponent) {
            return defaultSectionComponent;
        }

        console.error(
            `[ThemeSectionResolver] Failed to resolve section "${sectionType}" for theme "${themeId}". No themed or default section implementation was found.`
        );

        return null;
    })();

    resolvedSectionCache.set(cacheKey, resolution);

    return resolution;
}
