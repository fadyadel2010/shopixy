import { act } from 'react'
import { fireEvent, render, screen, waitFor, within, renderHook } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, expect, it, vi } from 'vitest'
import CampaignCostsPage from './attribution/pages/CampaignCostsPage'
import PixelTrackingConfigPage from '../storefront/config/PixelTrackingConfigPage'
import RoiSummaryCards from './attribution/components/RoiSummaryCards'
import { useRoiByCampaign } from './attribution/hooks/useCampaignCost'

const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }))
const t = (key: string, fallback?: string) => fallback || key
vi.mock('../../services/api', () => ({ default: api }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t, i18n: { language: 'en' } }) }))
vi.mock('../../contexts/SettingsContext', () => ({ useSettings: () => ({ formatCurrency: (n: number) => `${n} EGP` }) }))
vi.mock('../../common/confirm/ConfirmDialogProvider', () => ({ useConfirmDialog: () => ({ confirm: async () => true }) }))
const cost = { campaignCostId: 'cost-1', source: 'qa', medium: 'cpc', campaign: 'p6qa', costAmount: 0, currency: 'EGP', periodStartUtc: '2026-10-01T12:34:56.123Z', periodEndUtc: '2026-11-01T01:02:03.456Z', updatedAtUtc: '2026-10-17T21:00:00Z' }
const config = { isEnabled: true, strategy: 'PERCENTAGE', value: 100, appliesTo: ['Meta'] }
beforeEach(() => {
    vi.resetAllMocks()
    api.get.mockImplementation((url: string) => Promise.resolve({ data: url.includes('preview') ? { actualOrderTotal: 600, pixelReportedValue: 0, overrideDifference: -600 } : url.includes('pixel-tracking') ? config : { costs: [cost], totalCost: 0, totalCount: 1 } }))
    api.put.mockResolvedValue({ data: {} })
    api.post.mockResolvedValue({ data: {} })
})

it('edits via versioned PUT, preserves zero and exact unchanged UTC timestamps, excludes duplicate submissions', async () => {
    let complete: ((value: unknown) => void) | undefined
    api.put.mockImplementation(() => new Promise(resolve => { complete = resolve }))
    render(<MemoryRouter><CampaignCostsPage /></MemoryRouter>)
    fireEvent.click(await screen.findByTitle('Edit'))
    const modal = screen.getByRole('dialog')
    expect(within(modal).getByLabelText('Cost Amount')).toHaveValue(0)
    fireEvent.change(within(modal).getByLabelText('Campaign'), { target: { value: 'edited' } })
    act(() => { fireEvent.submit(modal.querySelector('form')!); fireEvent.submit(modal.querySelector('form')!) })
    expect(api.put).toHaveBeenCalledTimes(1)
    expect(api.post).not.toHaveBeenCalled()
    expect(api.put).toHaveBeenCalledWith('/admin/analytics/campaign-costs/cost-1', expect.objectContaining({ campaign: 'edited', costAmount: 0, periodStartUtc: cost.periodStartUtc, periodEndUtc: cost.periodEndUtc }), { headers: { 'If-Match': cost.updatedAtUtc } })
    await act(async () => { complete?.({ data: {} }) })
    expect(await screen.findByText('Campaign cost saved and verified')).toBeInTheDocument()
})

it('keeps a failed cost editor and displays the actual failure; accepted save/readback failure is qualified', async () => {
    render(<MemoryRouter><CampaignCostsPage /></MemoryRouter>)
    fireEvent.click(await screen.findByTitle('Edit'))
    api.put.mockRejectedValueOnce({ response: { data: { message: 'stale cost' } } })
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Save' }))
    expect(await screen.findByText('stale cost')).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    api.get.mockRejectedValue(new Error('read unavailable'))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Save' }))
    expect(await screen.findByText(/Saved, but the list could not be reloaded/)).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
})

it('pixel load failure cannot expose a saveable fabricated default; retry restores persisted values', async () => {
    api.get.mockRejectedValueOnce(new Error('offline'))
    render(<PixelTrackingConfigPage />)
    expect(await screen.findByText('Configuration unavailable')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save Configuration' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByRole('switch')).toHaveAttribute('aria-checked', 'true')
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save Configuration' })).toBeEnabled())
})

it('pixel zero preview is fetched and late old preview cannot replace the current value', async () => {
    let old: ((value: unknown) => void) | undefined
    api.get.mockImplementation((url: string, options?: { params?: { value: number } }) => url.includes('preview') && options?.params?.value === 100
        ? new Promise(resolve => { old = resolve })
        : Promise.resolve({ data: url.includes('preview') ? { actualOrderTotal: 600, pixelReportedValue: 0 } : config }))
    render(<PixelTrackingConfigPage />)
    fireEvent.change(await screen.findByLabelText('Override value'), { target: { value: '0' } })
    expect(await screen.findByText('0.00')).toBeInTheDocument()
    await act(async () => { old?.({ data: { actualOrderTotal: 600, pixelReportedValue: 999 } }) })
    expect(screen.queryByText('999.00')).not.toBeInTheDocument()
})

it('pixel excludes repeated saves and does not claim verification when readback fails', async () => {
    let complete: ((value: unknown) => void) | undefined
    api.put.mockImplementation(() => new Promise(resolve => { complete = resolve }))
    render(<PixelTrackingConfigPage />)
    const save = await screen.findByRole('button', { name: 'Save Configuration' })
    act(() => { fireEvent.click(save); fireEvent.click(save) })
    expect(api.put).toHaveBeenCalledTimes(1)
    api.get.mockRejectedValue(new Error('read failed'))
    await act(async () => { complete?.({ data: {} }) })
    expect(await screen.findByText(/Saved, but configuration could not be reloaded/)).toBeInTheDocument()
})

it('ROI cards render server-supplied ROI and CPA, including zero, without a new frontend formula', () => {
    render(<RoiSummaryCards loading={false} data={{ totalCost: 40, totalRevenue: 100, totalProfit: 60, totalOrders: 2, overallRoi: 150, costPerAcquisition: 0 }} />)
    expect(screen.getByText('+150.0%')).toBeInTheDocument()
    expect(screen.getByText('CPA: 0 EGP')).toBeInTheDocument()
})

it('ROI filters reject an older request and a later failure clears previously displayed numbers', async () => {
    let old: ((value: unknown) => void) | undefined
    api.get.mockImplementationOnce(() => new Promise(resolve => { old = resolve })).mockResolvedValue({ data: { totalCost: 20 } })
    const hook = renderHook(({ period }) => useRoiByCampaign('LastTouch', period), { initialProps: { period: 'today' } })
    hook.rerender({ period: 'last_month' })
    await waitFor(() => expect(hook.result.current.data).toEqual({ totalCost: 20 }))
    await act(async () => { old?.({ data: { totalCost: 999 } }) })
    expect(hook.result.current.data).toEqual({ totalCost: 20 })
    api.get.mockRejectedValue(new Error('ROI unavailable'))
    await act(async () => { await hook.result.current.refetch() })
    expect(hook.result.current.data).toBeNull()
    expect(hook.result.current.error).toBe('ROI unavailable')
})

it('an omitted nullable mixed-currency total is unavailable, not a fabricated zero amount', async () => {
    api.get.mockResolvedValue({ data: { costs: [cost, { ...cost, campaignCostId: 'usd', currency: 'USD' }], totalCount: 2 } })
    render(<MemoryRouter><CampaignCostsPage /></MemoryRouter>)
    expect(await screen.findByText('Unavailable: incompatible currency')).toBeInTheDocument()
})
