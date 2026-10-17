import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, cleanup } from '@testing-library/react'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import ShipmentActions from './components/ShipmentActions'
import { OrderDetailsPanel } from '../../pages/Orders'
import { cancelAdminShipment } from '../../services/adminOrdersApi'
import { useAuthStore } from '../../store/authStore'
import { admitCancellationOutcome, requestShipmentCancellation } from './cancellationOutcome'
import en from '../../i18n/locales/en.json'
import ar from '../../i18n/locales/ar.json'
import type { CancellationOutcome } from './shippingTypes'
import { StrictMode, type PropsWithChildren } from 'react'

const api = vi.hoisted(() => ({ post: vi.fn(), get: vi.fn() }))
vi.mock('../../services/api', () => ({ default: api, getSafeApiErrorMessage: (_: unknown, fallback: string) => fallback }))

const authority = { orderId: 'order-1', availableActions: [{ code: 'cancel', label: 'Cancel', targetStatus: 'Cancelled', requiresConfirmation: true, requiredFields: [] }] }
const shipment = { shipmentId: 'shipment-1', orderId: 'order-1', status: 'ReadyForPickup' }
const confirmed: CancellationOutcome = { shipmentId: 'shipment-1', operationId: 'op-1', outcome: 'Confirmed', confirmationBasis: 'CarrierConfirmed', confirmedAtUtc: '2026-10-09T12:00:00Z', currentStatus: 'Cancelled', externalConfirmationExists: true }
const outcomes = ['Confirmed', 'Requested', 'Unsupported', 'ReconciliationRequired'] as const

async function environment(language = 'en') {
    const i18n = createInstance()
    await i18n.init({ lng: language, fallbackLng: 'en', resources: { en: { translation: en }, ar: { translation: ar } }, interpolation: { escapeValue: false } })
    return { i18n, wrapper: ({ children }: PropsWithChildren) => <I18nextProvider i18n={i18n}><MemoryRouter>{children}</MemoryRouter></I18nextProvider> }
}

beforeEach(() => { cleanup(); vi.clearAllMocks(); useAuthStore.setState({ user: { tenantId: 'tenant-fixture' } }) })

describe('Shipment detail cancellation truth', () => {
    it.each(['order-1', 'order-2'])('does not send a cancellation after the shipment changes during authority refresh (next order %s)', async nextOrderId => {
        const env = await environment()
        let resolveAuthority!: (value: typeof authority) => void
        const refresh = vi.fn(() => new Promise<typeof authority>(resolve => { resolveAuthority = resolve }))
        const completed = vi.fn()
        api.post.mockResolvedValue({ data: confirmed })
        const props = { actionAuthority: authority, refetchActionAuthority: refresh, onActionComplete: completed }
        const view = render(<ShipmentActions {...props} shipment={shipment} />, { wrapper: env.wrapper })
        fireEvent.click(screen.getByRole('button', { name: 'Cancel Shipment' }))
        fireEvent.change(screen.getByPlaceholderText('Cancellation Reason'), { target: { value: 'synthetic stale target' } })
        fireEvent.click(screen.getAllByRole('button', { name: 'Cancel Shipment' })[1])
        expect(refresh).toHaveBeenCalledTimes(1)
        view.rerender(<ShipmentActions {...props} shipment={{ ...shipment, shipmentId: 'shipment-2', orderId: nextOrderId }} />)
        await act(async () => { resolveAuthority(authority) })
        expect(api.post).not.toHaveBeenCalled()
        expect(completed).not.toHaveBeenCalled()
        expect(screen.queryByRole('status')).not.toBeInTheDocument()
    })

    it('admits only one confirmation while authority refresh is in flight', async () => {
        const env = await environment()
        let resolveAuthority!: (value: typeof authority) => void
        const refresh = vi.fn(() => new Promise<typeof authority>(resolve => { resolveAuthority = resolve }))
        api.post.mockResolvedValue({ data: confirmed })
        render(<ShipmentActions shipment={shipment} actionAuthority={authority} refetchActionAuthority={refresh} />, { wrapper: env.wrapper })
        fireEvent.click(screen.getByRole('button', { name: 'Cancel Shipment' }))
        fireEvent.change(screen.getByPlaceholderText('Cancellation Reason'), { target: { value: 'synthetic single confirmation' } })
        const confirm = screen.getAllByRole('button', { name: 'Cancel Shipment' })[1]
        fireEvent.click(confirm)
        fireEvent.click(confirm)
        expect(refresh).toHaveBeenCalledTimes(1)
        expect(confirm).toBeDisabled()
        await act(async () => { resolveAuthority(authority) })
        expect(api.post).toHaveBeenCalledTimes(1)
    })

    it('does not send a cancellation after the action surface unmounts during authority refresh', async () => {
        const env = await environment()
        let resolveAuthority!: (value: typeof authority) => void
        const refresh = vi.fn(() => new Promise<typeof authority>(resolve => { resolveAuthority = resolve }))
        api.post.mockResolvedValue({ data: confirmed })
        const view = render(<ShipmentActions shipment={shipment} actionAuthority={authority} refetchActionAuthority={refresh} />, { wrapper: env.wrapper })
        fireEvent.click(screen.getByRole('button', { name: 'Cancel Shipment' }))
        fireEvent.change(screen.getByPlaceholderText('Cancellation Reason'), { target: { value: 'synthetic unmounted target' } })
        fireEvent.click(screen.getAllByRole('button', { name: 'Cancel Shipment' })[1])
        view.unmount()
        await act(async () => { resolveAuthority(authority) })
        expect(api.post).not.toHaveBeenCalled()
    })

    it('does not apply an already submitted previous-target result to the new shipment', async () => {
        const env = await environment()
        let resolveCancellation!: (value: { data: CancellationOutcome }) => void
        api.post.mockImplementation(() => new Promise(resolve => { resolveCancellation = resolve }))
        const completed = vi.fn()
        const props = { actionAuthority: authority, refetchActionAuthority: async () => authority, onActionComplete: completed }
        const view = render(<ShipmentActions {...props} shipment={shipment} />, { wrapper: env.wrapper })
        fireEvent.click(screen.getByRole('button', { name: 'Cancel Shipment' }))
        fireEvent.change(screen.getByPlaceholderText('Cancellation Reason'), { target: { value: 'synthetic current target' } })
        fireEvent.click(screen.getAllByRole('button', { name: 'Cancel Shipment' })[1])
        await waitFor(() => expect(api.post).toHaveBeenCalledTimes(1))
        view.rerender(<ShipmentActions {...props} shipment={{ ...shipment, shipmentId: 'shipment-2', orderId: 'order-2' }} />)
        await act(async () => { resolveCancellation({ data: confirmed }) })
        expect(api.post).toHaveBeenCalledTimes(1)
        expect(completed).not.toHaveBeenCalled()
        expect(screen.queryByRole('status')).not.toBeInTheDocument()
    })

    it('does not show a previous-target mutation error on the newly selected shipment', async () => {
        const env = await environment()
        let rejectCancellation!: (value: Error) => void
        api.post.mockImplementation(() => new Promise((_, reject) => { rejectCancellation = reject }))
        const props = { actionAuthority: authority, refetchActionAuthority: async () => authority }
        const view = render(<ShipmentActions {...props} shipment={shipment} />, { wrapper: env.wrapper })
        fireEvent.click(screen.getByRole('button', { name: 'Cancel Shipment' }))
        fireEvent.change(screen.getByPlaceholderText('Cancellation Reason'), { target: { value: 'synthetic previous target' } })
        fireEvent.click(screen.getAllByRole('button', { name: 'Cancel Shipment' })[1])
        await waitFor(() => expect(api.post).toHaveBeenCalledTimes(1))
        view.rerender(<ShipmentActions {...props} shipment={{ ...shipment, shipmentId: 'shipment-2', orderId: 'order-2' }} />)
        await act(async () => { rejectCancellation(new Error('synthetic failed previous request')) })
        expect(screen.queryByText('Failed to cancel shipment')).not.toBeInTheDocument()
    })

    it('preserves mutation error reporting under the normal StrictMode effect replay', async () => {
        const env = await environment()
        api.post.mockRejectedValue(new Error('synthetic mutation rejection'))
        render(<StrictMode><ShipmentActions shipment={shipment} actionAuthority={authority} refetchActionAuthority={async () => authority} /></StrictMode>, { wrapper: env.wrapper })
        fireEvent.click(screen.getByRole('button', { name: 'Cancel Shipment' }))
        fireEvent.change(screen.getByPlaceholderText('Cancellation Reason'), { target: { value: 'synthetic strict mode boundary' } })
        await act(async () => { fireEvent.click(screen.getAllByRole('button', { name: 'Cancel Shipment' })[1]) })
        expect(api.post).toHaveBeenCalledTimes(1)
        expect(screen.getAllByText('Failed to cancel shipment').length).toBeGreaterThan(0)
    })

    it.each(['refused', 'failed'])('does not mutate or retry when refreshed authority is %s', async result => {
        const env = await environment()
        const refresh = result === 'failed'
            ? vi.fn().mockRejectedValue(new Error('synthetic authority failure'))
            : vi.fn().mockResolvedValue({ ...authority, availableActions: [] })
        render(<ShipmentActions shipment={shipment} actionAuthority={authority} refetchActionAuthority={refresh} />, { wrapper: env.wrapper })
        fireEvent.click(screen.getByRole('button', { name: 'Cancel Shipment' }))
        fireEvent.change(screen.getByPlaceholderText('Cancellation Reason'), { target: { value: 'synthetic authority boundary' } })
        await act(async () => { fireEvent.click(screen.getAllByRole('button', { name: 'Cancel Shipment' })[1]) })
        expect(refresh).toHaveBeenCalledTimes(1)
        expect(api.post).not.toHaveBeenCalled()
    })

    for (const language of ['en', 'ar']) for (const outcome of outcomes) {
        it(`${language}: ${outcome} is displayed truthfully and reload retains it`, async () => {
            const env = await environment(language)
            const result = outcome === 'Confirmed' ? confirmed : { shipmentId: 'shipment-1', operationId: 'op-1', outcome, currentStatus: 'ReadyForPickup' }
            api.post.mockImplementation(async () => {
                if (outcome === 'Unsupported' || outcome === 'ReconciliationRequired') throw { response: { status: 409, data: result } }
                return { data: result, status: outcome === 'Requested' ? 202 : 200 }
            })
            const refreshed = vi.fn()
            const { unmount } = render(<ShipmentActions shipment={shipment} actionAuthority={authority} refetchActionAuthority={async () => authority} onActionComplete={refreshed} />, { wrapper: env.wrapper })
            fireEvent.click(screen.getByRole('button', { name: env.i18n.t('shipping.cancelShipment') }))
            fireEvent.change(screen.getByPlaceholderText(env.i18n.t('shipping.cancellationReason')), { target: { value: 'customer request' } })
            fireEvent.click(screen.getAllByRole('button', { name: env.i18n.t('shipping.cancelShipment') })[1])
            await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(env.i18n.t(`shipping.cancellation${outcome}`)))
            expect(api.post).toHaveBeenCalledTimes(1)
            expect(refreshed).toHaveBeenCalledTimes(1)
            if (outcome === 'Confirmed') expect(screen.queryByPlaceholderText(env.i18n.t('shipping.cancellationReason'))).not.toBeInTheDocument()
            else expect(screen.queryByText(env.i18n.t('shipping.cancellationConfirmed'))).not.toBeInTheDocument()
            expect(screen.queryByText(/notify the carrier/i)).not.toBeInTheDocument()
            unmount()
            render(<ShipmentActions shipment={{ ...shipment, cancellation: result }} actionAuthority={authority} />, { wrapper: env.wrapper })
            expect(screen.getByRole('status')).toHaveTextContent(env.i18n.t(`shipping.cancellation${outcome}`))
            expect(screen.getByRole('button', { name: env.i18n.t('shipping.cancelShipment') })).toBeDisabled()
        })
    }
})

describe('Orders drawer cancellation truth', () => {
    for (const language of ['en', 'ar']) for (const outcome of ['Confirmed', 'ReconciliationRequired'] as const) {
        it(`${language}: submitted drawer action closes only on confirmed and cannot repeat an unresolved command`, async () => {
            const env = await environment(language)
            const execute = vi.fn()
            const order = { orderId: 'order-1', orderNumber: 'ORD-1', source: 'Ecommerce', status: 'Confirmed', items: [], payments: [],
                availableOrderActions: [{ code: 'cancel-order', label: 'Cancel Order' }],
                availableShipmentActions: [{ code: 'cancel-shipment', label: 'Cancel Shipment' }], shipment }
            const { rerender } = render(<OrderDetailsPanel order={order} onClose={vi.fn()} onAdministrativeAction={execute} />, { wrapper: env.wrapper })
            const action = env.i18n.t('orders.management.actions.cancel-shipment')
            fireEvent.click(screen.getByRole('button', { name: action }))
            const reasonLabel = env.i18n.t('orders.management.details.reason')
            fireEvent.change(screen.getAllByRole('textbox', { name: reasonLabel }).find(element => !element.hasAttribute('disabled'))!, { target: { value: 'customer request' } })
            const confirmLabel = env.i18n.t('orders.management.details.confirmNamed', { action })
            fireEvent.click(screen.getByRole('button', { name: confirmLabel }))
            expect(execute).toHaveBeenCalledExactlyOnceWith({ actionCode: 'cancel-shipment', reason: 'customer request' })
            const result = outcome === 'Confirmed' ? confirmed : { outcome, operationId: 'op-1', shipmentId: 'shipment-1' }
            rerender(<OrderDetailsPanel order={order} onClose={vi.fn()} onAdministrativeAction={execute} administrativeActionResult={result} />)
            expect(screen.getByRole('status')).toHaveTextContent(env.i18n.t(`shipping.cancellation${outcome}`))
            if (outcome === 'Confirmed') expect(screen.queryAllByRole('textbox', { name: reasonLabel }).filter(element => !element.hasAttribute('disabled'))).toHaveLength(0)
            else {
                fireEvent.click(screen.getByRole('button', { name: confirmLabel }))
                expect(execute).toHaveBeenCalledTimes(1)
                expect(screen.queryByText(env.i18n.t('shipping.cancellationConfirmed'))).not.toBeInTheDocument()
            }
        })
    }
    for (const language of ['en', 'ar']) for (const outcome of outcomes) {
        it(`${language}: caller and drawer preserve ${outcome} without converting HTTP completion to success`, async () => {
            const env = await environment(language)
            const result = outcome === 'Confirmed' ? confirmed : { shipmentId: 'shipment-1', operationId: 'op-1', outcome, currentStatus: 'ReadyForPickup' }
            api.post.mockImplementation(async () => {
                if (outcome === 'Unsupported' || outcome === 'ReconciliationRequired') throw { response: { status: 409, data: result } }
                return { data: result }
            })
            const returned = await cancelAdminShipment('shipment-1', 'customer request')
            expect(returned.outcome).toBe(outcome)
            const order = { orderId: 'order-1', orderNumber: 'ORD-1', source: 'Ecommerce', status: 'Confirmed', items: [], payments: [], availableOrderActions: [], availableShipmentActions: [{ code: 'cancel-shipment', label: 'Cancel Shipment' }], shipment }
            const { rerender } = render(<OrderDetailsPanel order={order} onClose={vi.fn()} administrativeActionResult={returned} />, { wrapper: env.wrapper })
            expect(screen.getByRole('status')).toHaveTextContent(env.i18n.t(`shipping.cancellation${outcome}`))
            if (outcome !== 'Confirmed') expect(screen.queryByText(env.i18n.t('shipping.cancellationConfirmed'))).not.toBeInTheDocument()
            rerender(<OrderDetailsPanel order={{ ...order, shipment: { ...shipment, cancellation: returned } }} onClose={vi.fn()} />)
            expect(screen.getByRole('status')).toHaveTextContent(env.i18n.t(`shipping.cancellation${outcome}`))
        })
    }

    it('concurrent calls from both surfaces share one request and never generate replacement IDs', async () => {
        let resolve!: (value: { data: { shipmentId: string; outcome: string; operationId: string } }) => void
        api.post.mockImplementation(() => new Promise((done) => { resolve = done }))
        const first = requestShipmentCancellation(api, 'same-shipment', 'reason')
        const second = cancelAdminShipment('same-shipment', 'reason')
        resolve({ data: { shipmentId: 'same-shipment', outcome: 'ReconciliationRequired', operationId: 'original' } })
        const results = await Promise.all([first, second])
        expect(api.post).toHaveBeenCalledTimes(1)
        expect(api.post.mock.calls[0][1]).toEqual({ reason: 'reason' })
        expect(results.map(x => x.operationId)).toEqual(['original', 'original'])
    })

    it('a bare successful HTTP body or forged confirmed outcome is never admitted as confirmation', () => {
        expect(admitCancellationOutcome({ success: true }).outcome).toBe('ReconciliationRequired')
        expect(admitCancellationOutcome({ outcome: 'Confirmed' }).outcome).toBe('ReconciliationRequired')
        expect(admitCancellationOutcome({ ...confirmed, externalConfirmationExists: false }).outcome).toBe('ReconciliationRequired')
    })
})
