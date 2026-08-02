import type { SectionRendererInputSection } from '@/components/sections/homepageSections';

export type ProductCatalogSectionSlot = 'top' | 'bottom';

export interface ProductCatalogSectionGroups {
    top: SectionRendererInputSection[];
    bottom: SectionRendererInputSection[];
}

const TOP_SLOT_VALUES = new Set(['top', 'before', 'before-catalog', 'above', 'header']);
const BOTTOM_SLOT_VALUES = new Set(['bottom', 'after', 'after-catalog', 'below', 'footer']);

function readSlotValue(section: SectionRendererInputSection): string | undefined {
    const source = ('settings' in section ? section.settings : section.data) || {};
    const rawValue = source.catalogSlot ?? source.catalogPlacement ?? source.placement ?? source.position;
    return typeof rawValue === 'string' ? rawValue.trim().toLowerCase() : undefined;
}

export function getProductCatalogSectionSlot(section: SectionRendererInputSection): ProductCatalogSectionSlot {
    const slot = readSlotValue(section);

    if (slot && TOP_SLOT_VALUES.has(slot)) {
        return 'top';
    }

    if (slot && BOTTOM_SLOT_VALUES.has(slot)) {
        return 'bottom';
    }

    return 'bottom';
}

export function splitProductCatalogSections(
    sections: SectionRendererInputSection[] = []
): ProductCatalogSectionGroups {
    const groups: ProductCatalogSectionGroups = {
        top: [],
        bottom: [],
    };

    const enabledSections = [...sections]
        .filter((section) => section.enabled !== false)
        .sort((a, b) => a.order - b.order);

    for (const section of enabledSections) {
        groups[getProductCatalogSectionSlot(section)].push(section);
    }

    return groups;
}
