import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import ShippingHealthPage from './ShippingHealthPage'

const state = vi.hoisted(() => ({ health: { status: 'healthy', timestamp: '2026-10-15T10:00:00Z', supportedCarriers: ['manual', 'bosta', 'fedex'] }, loading: false, error: null as null | string, refetch: vi.fn() }))
vi.mock('../hooks/useShipmentsApi', () => ({ useShippingHealth: () => state }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (_: string, fallback: string) => fallback, i18n: { language: 'en' } }) }))

describe('shipping health scope', () => {
    it('does not infer carrier readiness or webhook verification from static endpoint liveness', () => {
        state.error = null
        render(<ShippingHealthPage />)
        expect(screen.queryByText('All Systems Operational')).not.toBeInTheDocument()
        expect(screen.getByText(/does not verify carrier credentials/i)).toBeInTheDocument()
        expect(screen.queryByText('/api/webhooks/shipping/manual')).not.toBeInTheDocument()
        expect(screen.queryByText('Every 30 minutes')).not.toBeInTheDocument()
    })

    it('does not present stale healthy evidence after refresh failed', () => {
        state.error = 'Read unavailable'
        render(<ShippingHealthPage />)
        expect(screen.getByText('Health Check Failed')).toBeInTheDocument()
        expect(screen.queryByText('Webhook endpoint reachable')).not.toBeInTheDocument()
        expect(screen.queryByRole('heading', { name: /Supported Carriers|Registered carrier adapters/ })).not.toBeInTheDocument()
    })
})
