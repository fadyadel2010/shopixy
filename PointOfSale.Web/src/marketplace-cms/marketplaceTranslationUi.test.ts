import { describe, expect, it } from 'vitest'
import { getMarketplaceTranslationState } from './marketplaceTranslationUi'

describe('marketplaceTranslationUi', () => {
    it('shows a partial translation warning when Arabic and English content are mixed', () => {
        const state = getMarketplaceTranslationState(
            {
                isAutoTranslated: true,
                description: '\u0648\u0635\u0641 \u0645\u062a\u0631\u062c\u0645',
                category: 'Shoes',
                features: ['Breathable upper'],
                faqs: [],
            },
            'ar',
        )

        expect(state.showAutoTranslationBadge).toBe(true)
        expect(state.showPartialTranslationWarning).toBe(true)
        expect(state.categoryUsesFallback).toBe(true)
    })

    it('does not show a partial warning when translated fields are consistently Arabic', () => {
        const state = getMarketplaceTranslationState(
            {
                isAutoTranslated: false,
                description: '\u0648\u0635\u0641 \u0639\u0631\u0628\u064a',
                category: '\u0623\u062d\u0630\u064a\u0629',
                features: ['\u062e\u0627\u0645\u0629 \u062e\u0641\u064a\u0641\u0629'],
                faqs: [
                    {
                        question: '\u0647\u0644 \u0647\u0648 \u0645\u0642\u0627\u0648\u0645 \u0644\u0644\u0645\u0627\u0621\u061f',
                        answer: '\u0646\u0639\u0645',
                    },
                ],
            },
            'ar',
        )

        expect(state.showAutoTranslationBadge).toBe(false)
        expect(state.showPartialTranslationWarning).toBe(false)
        expect(state.categoryUsesFallback).toBe(false)
    })
})
