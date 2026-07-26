import type { StorefrontSection } from '@/types/storeConfig'

export type HomepageSectionData = Record<string, unknown>

export interface HomepageSection {
  id: string
  type: string
  order: number
  enabled: boolean
  data: HomepageSectionData
}

export type SectionRendererInputSection = HomepageSection | StorefrontSection

export function normalizeSection(section: SectionRendererInputSection): HomepageSection {
  const rawData = 'data' in section ? section.data : section.settings
  const enabled = 'enabled' in section ? section.enabled : true

  return {
    id: section.id,
    type: section.type,
    order: section.order ?? 0,
    enabled: enabled !== false,
    data: rawData ?? {},
  }
}

export function normalizeSections(sections: SectionRendererInputSection[]): HomepageSection[] {
  return sections.map(normalizeSection).sort((left, right) => left.order - right.order)
}
