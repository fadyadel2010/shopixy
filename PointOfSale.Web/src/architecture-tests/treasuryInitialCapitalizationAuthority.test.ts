import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const readSource = (path: string) => readFileSync(resolve(process.cwd(), 'src', path), 'utf8')

describe('treasury initial capitalization frontend authority', () => {
    it('keeps the canonical workspace Owner-only and retires legacy Treasury opening routes', () => {
        const app = readSource('App.jsx')

        expect(app).toContain('<Route element={<PosPermissionRouteGuard permission="finance.initialize" />}>')
        expect(app).toContain('path="treasury/initial-capitalization" element={<TreasuryInitialCapitalization />}')
        expect(app).toContain('path="opening-balance/cashbox" element={<Navigate to="/treasury/initial-capitalization" replace />}')
        expect(app).toContain('path="opening-balance/banks" element={<Navigate to="/treasury/initial-capitalization" replace />}')
        expect(app).not.toContain("import CashboxOpeningBalance from './pages/CashboxOpeningBalance'")
    })

    it('exposes capitalization navigation only to Owner', () => {
        const sidebar = readSource('components/Sidebar.jsx')

        expect(sidebar).toContain("path: '/treasury/initial-capitalization'")
        expect(sidebar).toContain("label: t('nav.treasuryInitialCapitalization')")
        expect(sidebar).toContain("permission: 'finance.initialize'")
        expect(sidebar).not.toContain("path: '/opening-balance/cashbox'")
    })
})
