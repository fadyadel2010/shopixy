import { describe, expect, it } from 'vitest'
import { canRewriteInvoice } from './InvoiceSearchModal'

describe('InvoiceSearchModal sale rewrite policy', () => {
    it('allows draft sales invoices only', () => {
        expect(canRewriteInvoice('sales', { status: 'Draft' })).toBe(true)
        expect(canRewriteInvoice('sales', { status: 0 })).toBe(true)
        expect(canRewriteInvoice('sales', { status: 'Completed' })).toBe(false)
        expect(canRewriteInvoice('sales', { status: 'PartiallyReturned' })).toBe(false)
    })

    it('does not change purchase invoice behavior', () => {
        expect(canRewriteInvoice('purchases', { status: 'Completed' })).toBe(true)
    })
})
