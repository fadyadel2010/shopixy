import i18next from 'i18next'
import { describe, expect, it } from 'vitest'
import { buildI18nCertificationAudit, readLocaleResources } from '../../scripts/i18n-certification-audit.mjs'

describe('i18n certification audit', () => {
    it('keeps locale files structurally aligned and free of extracted source artifacts', () => {
        const audit = buildI18nCertificationAudit()

        expect(audit.structural.missingInEn).toEqual([])
        expect(audit.structural.missingInAr).toEqual([])
        expect(audit.structural.placeholderMismatches).toEqual([])
        expect(audit.structural.suspiciousEntries).toEqual([])
    })

    it('keeps the audited screens fully resolved in both locale files', () => {
        const audit = buildI18nCertificationAudit()

        expect(audit.auditedPages).toEqual(
            expect.arrayContaining(
                audit.auditedPages.map((page) => ({
                    ...page,
                    missingInEn: [],
                    missingInAr: [],
                })),
            ),
        )
    })

    it('restores the critical audited Arabic values instead of corrupted placeholders', () => {
        const { ar } = readLocaleResources()

        expect(ar.orders.returnManagedByVendor).not.toContain('?')
        expect(ar.orders.whatsappDefaultShippingMethod).not.toContain('?')
        expect(ar.orders.whatsappDefaultCustomerName).not.toContain('?')
        expect(ar.orders.whatsappMessage).not.toContain('?')
        expect(ar.openingBalance.stockSaved).toContain('{{count}}')
        expect(ar.CommerceHubFinance.filters.activeCount_one).toContain('{{count}}')
        expect(ar.CommerceHubFinance.statement.matchingTransactions_one).toContain('{{count}}')
        expect(ar.CommerceHubFinance.statement.pageSummary_one).toContain('{{count}}')
    })

    it('falls back to English for missing or empty Arabic values without crashing', async () => {
        const instance = i18next.createInstance()

        await instance.init({
            resources: {
                en: {
                    translation: {
                        smoke: {
                            missing: 'English fallback',
                            empty: 'English fallback for empty value',
                        },
                    },
                },
                ar: {
                    translation: {
                        smoke: {
                            empty: '',
                        },
                    },
                },
            },
            lng: 'ar',
            fallbackLng: 'en',
            returnNull: false,
            returnEmptyString: false,
            interpolation: {
                escapeValue: false,
            },
        })

        expect(instance.t('smoke.missing')).toBe('English fallback')
        expect(instance.t('smoke.empty')).toBe('English fallback for empty value')
    })
})
