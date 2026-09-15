import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const readSource = (path: string) => readFileSync(resolve(process.cwd(), 'src', path), 'utf8')
const readLocale = (lang: string) =>
    JSON.parse(readFileSync(resolve(process.cwd(), 'src', `i18n/locales/${lang}.json`), 'utf8')) as Record<string, Record<string, unknown>>

const REQUIRED_TREASURY_KEYS = [
    'hub', 'hubSubtitle', 'financeTitle', 'financeSubtitle', 'availableNow', 'availableNowExplanation', 'inTransit',
    'inTransitExplanation', 'catalogEmpty', 'catalogEmptyHint', 'catalogLoadFailed', 'availableCashTotal',
    'pendingClearingTotal', 'cashboxRole', 'bankRole', 'clearingRole', 'cashPositionExplanation',
    'clearingSeparate', 'accountUnavailable', 'accountUnavailableHint', 'backToHub',
    'transferableOnly', 'transferableSourceRequired', 'transferableDestinationRequired',
    'clearingEvidenceBoundary', 'settlementRequiresEvidence', 'awaitingProviderSettlement',
    'cashFlowPerimeter', 'balanceSheetPerimeter', 'trialBalancePerimeter', 'generalLedgerPerimeter',
    'supplierCardRestricted', 'clearingNav', 'generalLedgerNav', 'retry',
    'periodFilters', 'currentClassification', 'accountsLateCount', 'oldestDate', 'ageDays',
    'clearingAccount',
]

const TREASURY_SURFACES = [
    'TreasuryDashboard.jsx',
    'TreasuryHub.jsx',
    'TreasuryAccountViewer.jsx',
    'TreasuryTransfers.jsx',
    'TreasuryTrialBalance.jsx',
    'TreasuryProfitLoss.jsx',
    'TreasuryBalanceSheet.jsx',
    'TreasuryCashFlow.jsx',
    'TreasuryGeneralLedger.jsx',
    'TreasuryClearingAging.jsx',
]

describe('treasury catalog presentation authority', () => {
    it('keeps the Hub dynamic with no fixed card catalog', () => {
        const hub = readSource('pages/treasury/TreasuryHub.jsx')
        expect(hub).toContain('getTreasuryAccounts')
        expect(hub).toContain('partitionTreasuryCatalog')
        expect(hub).not.toContain('ACCOUNT_CARDS')
        expect(hub).not.toContain('getTreasuryBalances')
        expect(hub).not.toContain('1010')
        expect(hub).not.toContain('1030')
        expect(hub).not.toContain('1110')
    })

    it('classifies Dashboard cash by accountType with no numeric code ranges', () => {
        const dashboard = readSource('pages/treasury/TreasuryDashboard.jsx')
        expect(dashboard).toContain('getTreasuryAccounts')
        expect(dashboard).toContain('partitionTreasuryCatalog')
        expect(dashboard).not.toContain('parseInt')
        expect(dashboard).not.toContain('code >= 1000')
        expect(dashboard).not.toContain('code < 1050')
        expect(dashboard).not.toContain('1100')
    })

    it('backs the Viewer identity with the catalog and fails closed', () => {
        const viewer = readSource('pages/treasury/TreasuryAccountViewer.jsx')
        expect(viewer).toContain('getTreasuryAccounts')
        expect(viewer).toContain('findTreasuryAccountByCode')
        expect(viewer).toContain('accountUnavailable')
        expect(viewer).not.toContain('ACCOUNT_NAMES')
        expect(viewer).not.toContain('Account ${accountCode}')
        expect(viewer).not.toMatch(/case treasuryApi\.ACCOUNT_CODES/)
    })

    it('restricts Transfers to exact Cashbox/Bank rows', () => {
        const transfers = readSource('pages/treasury/TreasuryTransfers.jsx')
        expect(transfers).toContain('getTreasuryAccounts')
        expect(transfers).toContain('isTransferEligibleAccount')
        expect(transfers).toContain('transferableOnly')
        expect(transfers).toContain("navigate('/treasury/general-ledger')")
        expect(transfers).toContain("navigate('/treasury/trial-balance')")
        expect(transfers).not.toContain("navigate('/reports/")
    })

    it('keeps Clearing Aging read-only with the evidence boundary', () => {
        const aging = readSource('pages/treasury/TreasuryClearingAging.jsx')
        expect(aging).toContain('disabled')
        expect(aging).toContain('clearingEvidenceBoundary')
        expect(aging).toContain('awaitingProviderSettlement')
        expect(aging).not.toContain('api.post')
        expect(aging).not.toContain('/treasury/operations/')
    })

    it('explains report perimeters without changing report data', () => {
        expect(readSource('pages/treasury/TreasuryCashFlow.jsx')).toContain('cashFlowPerimeter')
        expect(readSource('pages/treasury/TreasuryBalanceSheet.jsx')).toContain('balanceSheetPerimeter')
        expect(readSource('pages/treasury/TreasuryTrialBalance.jsx')).toContain('trialBalancePerimeter')
        expect(readSource('pages/treasury/TreasuryGeneralLedger.jsx')).toContain('generalLedgerPerimeter')
        expect(readSource('pages/treasury/TreasuryGeneralLedger.jsx')).toContain('selectAccount')
    })

    it('keeps Treasury screens direction-aware and free of physical text alignment utilities', () => {
        for (const file of TREASURY_SURFACES) {
            const source = readSource(`pages/treasury/${file}`)
            expect(source, file).toContain('dir={dir}')
            expect(source, file).not.toMatch(/\btext-(left|right)\b/)
        }
    })

    it('uses local calendar dates for report queries instead of UTC date truncation', () => {
        const reportsApi = readSource('services/financialReportsApi.js')
        expect(reportsApi).toContain('formatDateInputValue')
        expect(reportsApi).not.toContain("toISOString().split('T')[0]")
    })

    it('keeps narrow Treasury layouts stackable and wide tables scrollable', () => {
        expect(readSource('pages/treasury/TreasuryDashboard.jsx')).toContain('grid grid-cols-1 gap-4 sm:grid-cols-2')
        expect(readSource('pages/treasury/TreasuryHub.jsx')).toContain('grid grid-cols-1 gap-4 sm:grid-cols-2')
        expect(readSource('pages/treasury/TreasuryTransfers.jsx')).toContain('flex flex-col gap-3 pt-4 sm:flex-row')
        expect(readSource('pages/treasury/TreasuryGeneralLedger.jsx')).toContain('overflow-x-auto')
        expect(readSource('pages/treasury/TreasuryClearingAging.jsx')).toContain('overflow-x-auto')
    })

    it('labels supplier-card balances as restricted non-cash assets', () => {
        expect(readSource('pages/SupplierCardFundingInstruments.jsx')).toContain('supplierCardRestricted')
    })

    it('removes fixed Treasury account-code navigation dependencies', () => {
        const sidebar = readSource('components/Sidebar.jsx')
        expect(sidebar).not.toContain('/treasury/account/1010')
        expect(sidebar).not.toContain('/treasury/account/1030')
        expect(sidebar).not.toContain('/treasury/account/1110')
        expect(sidebar).toContain('/treasury/clearing-aging')
        expect(sidebar).toContain('/treasury/general-ledger')

        const app = readSource('App.jsx')
        expect(app).not.toContain('treasury/account/1010')
        expect(app).not.toContain('treasury/account/1030')
        expect(app).not.toContain('treasury/account/1110')
        expect(app).toContain('path="treasury/hub"')
    })

    it('keeps English and Arabic treasury locale keys structurally valid', () => {
        const en = readLocale('en')
        const ar = readLocale('ar')
        expect(en.treasury).toBeDefined()
        expect(ar.treasury).toBeDefined()
        for (const key of REQUIRED_TREASURY_KEYS) {
            expect(en.treasury[key], `en treasury.${key}`).toBeDefined()
            expect(ar.treasury[key], `ar treasury.${key}`).toBeDefined()
            expect(typeof en.treasury[key]).toBe('string')
            expect(typeof ar.treasury[key]).toBe('string')
            expect((en.treasury[key] as string).trim(), `en treasury.${key}`).not.toBe('')
            expect((ar.treasury[key] as string).trim(), `ar treasury.${key}`).not.toBe('')
            expect(ar.treasury[key], `Arabic treasury.${key} must not use the English copy`).not.toBe(en.treasury[key])
        }
    })
})
