import { describe, expect, it } from 'vitest'
import { admitCarrierOperations } from './shippingAdministrationApi'
import en from '../../i18n/locales/en.json'
import ar from '../../i18n/locales/ar.json'

describe('operation readiness boundary', () => {
    it('does not let stored evidence or reconciliation claim execution readiness', () => {
        const operations = admitCarrierOperations([
            { operation: 'label', implementation: 'StoredOnly', accountReady: true },
            { operation: 'recovery', implementation: 'ReconciliationOnly', accountReady: true },
            { operation: 'create', implementation: 'Implemented', accountReady: true, requiresShipmentEvidence: true },
        ])
        expect(operations.map(o => o.accountReady)).toEqual([false, false, true])
        expect(operations.every(o => o.requiresShipmentEvidence)).toBe(true)
        expect(admitCarrierOperations(undefined)).toEqual([])
    })
    it('rejects duplicates, unknown operations, and fabricated implementation states', () => {
        const valid = { operation: 'quote', implementation: 'Implemented' }
        expect(() => admitCarrierOperations([valid, valid])).toThrow()
        expect(() => admitCarrierOperations([{ operation: 'purchaseLabel', implementation: 'Implemented' }])).toThrow()
        expect(() => admitCarrierOperations([{ operation: 'create', implementation: 'ProductionActivated' }])).toThrow()
    })
    it('localizes every operation and containment state in English and Arabic', () => {
        for (const locale of [en, ar]) {
            expect(Object.keys(locale.shippingAdministration.operations.names)).toHaveLength(6)
            expect(Object.keys(locale.shippingAdministration.operations.states)).toHaveLength(5)
            expect(locale.shippingAdministration.operations.boundary.length).toBeGreaterThan(50)
        }
    })
})
