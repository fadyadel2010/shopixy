import { afterEach, describe, expect, it } from 'vitest'

import { sectionRegistry } from '../builder'
import { useBuilderStore } from './builderStore'

const runtimeHero = {
    id: 'section_runtime_hero_test',
    type: 'product-hero',
    enabled: true,
    order: 0,
    settings: { layoutVariant: 'default' },
}

const supplemental = {
    id: 'section_supplemental_test',
    type: 'product-specs',
    enabled: true,
    order: 1,
    settings: { layoutVariant: 'default', title: 'Details' },
}

afterEach(() => {
    useBuilderStore.getState().reset()
})

describe('Builder Runtime section ownership', () => {
    it('never admits Product Hero into authored Builder state', () => {
        useBuilderStore.getState().admitSections([runtimeHero, supplemental])

        expect(useBuilderStore.getState().sections).toEqual([{ ...supplemental }])
    })

    it('cannot add Product Hero from the registry', () => {
        useBuilderStore.getState().addSection('product-hero', undefined, sectionRegistry)

        expect(useBuilderStore.getState().sections).toEqual([])
    })

    it('cannot disable, duplicate, remove, edit, or reorder an injected Runtime hero', () => {
        useBuilderStore.setState({ sections: [runtimeHero, supplemental], selectedSectionId: null })
        const store = useBuilderStore.getState()

        store.toggleSection(runtimeHero.id)
        store.duplicateSection(runtimeHero.id)
        store.removeSection(runtimeHero.id)
        store.updateSectionSettings(runtimeHero.id, { layoutVariant: 'fashion' })
        store.reorderSections(0, 1)

        expect(useBuilderStore.getState().sections).toEqual([runtimeHero, supplemental])
    })
})
