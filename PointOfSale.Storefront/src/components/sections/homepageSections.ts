import type { StorefrontSection } from '@/types/storeConfig';
import {
    getSectionContract,
    normalizeSectionType,
    validateSectionSettingsStrict,
} from '@/storefront/runtime/sectionContract';

export type HomepageSectionData = Record<string, any>;

export interface HomepageSection {
    id: string;
    type: string;
    order: number;
    enabled: boolean;
    data: HomepageSectionData;
}

export type SectionRendererInputSection = HomepageSection | StorefrontSection;

export const SectionRegistry = {
    hero: ['hero-banner'],
    products: ['product-grid'],
    banner: ['banner'],
    categories: ['category-grid'],
} as const;

export type RegisteredHomepageSectionType = keyof typeof SectionRegistry;

export function isRegisteredHomepageSectionType(sectionType: string): sectionType is RegisteredHomepageSectionType {
    return Object.prototype.hasOwnProperty.call(SectionRegistry, sectionType);
}

export function getSectionResolutionCandidates(sectionType: string): string[] {
    const normalizedType = sectionType.trim().toLowerCase();
    if (!normalizedType) {
        return [];
    }

    if (isRegisteredHomepageSectionType(normalizedType)) {
        return Array.from(new Set(SectionRegistry[normalizedType]));
    }

    return [normalizedType];
}

export function normalizeSection(section: SectionRendererInputSection): HomepageSection {
    const rawData = 'data' in section ? section.data : section.settings;
    const enabled = 'enabled' in section ? section.enabled : true;
    const candidateData = rawData && typeof rawData === 'object' && !Array.isArray(rawData)
        ? rawData
        : {};
    const canonicalType = normalizeSectionType(section.type);
    const contract = getSectionContract(canonicalType);

    validateSectionSettingsStrict(canonicalType, candidateData);

    return {
        id: section.id,
        type: canonicalType,
        order: section.order ?? 0,
        enabled: enabled !== false,
        data: Object.fromEntries(
            contract.fields.map((field) => [field.key, candidateData[field.key]])
        ),
    };
}

export function normalizeSections(sections: SectionRendererInputSection[]): HomepageSection[] {
    return sections
        .map((section) => {
            try {
                return normalizeSection(section);
            } catch (error) {
                console.error('[SectionContract] Invalid runtime section skipped.', {
                    sectionId: section?.id,
                    sectionType: section?.type,
                    error,
                });
                return null;
            }
        })
        .filter((section): section is HomepageSection => Boolean(section))
        .sort((left, right) => left.order - right.order);
}
