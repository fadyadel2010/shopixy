import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { CuratedProductSelector } from './CuratedProductSelector'

const get = vi.hoisted(() => vi.fn())
vi.mock('../../../../services/api', () => ({ default: { get } }))
vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        i18n: { language: 'en' },
        t: (key: string, options?: { name?: string }) => `${key.split('.').pop()}${options?.name ? ` ${options.name}` : ''}`,
    }),
}))

const one = '11111111-1111-4111-8111-111111111111'
const two = '22222222-2222-4222-8222-222222222222'
const missing = '33333333-3333-4333-8333-333333333333'

afterEach(() => { cleanup(); get.mockReset() })

describe('CuratedProductSelector', () => {
    it('searches tenant catalog names/SKUs and preserves ordered ProductIds through selection and reorder', async () => {
        get.mockImplementation((path: string) => Promise.resolve({ data: path.endsWith('/selected')
            ? [{ id: one, name: 'First', sku: 'A', hasVariants: false }, { id: two, name: 'Variant', sku: 'B', hasVariants: true }]
            : [{ id: one, name: 'First', sku: 'A' }, { id: two, name: 'Variant', sku: 'B', hasVariants: true }] }))
        const onChange = vi.fn()
        const { rerender } = render(<CuratedProductSelector ids={[one]} onChange={onChange} maxItems={1} />)
        fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'B' } })
        await waitFor(() => expect(get).toHaveBeenCalledWith('/products/lookup', expect.objectContaining({ params: expect.objectContaining({ search: 'B', isActive: true }) })))
        fireEvent.click(await screen.findByRole('button', { name: /Variant.*B/ }))
        expect(onChange).toHaveBeenLastCalledWith([one, two])
        rerender(<CuratedProductSelector ids={[one, two]} onChange={onChange} maxItems={1} />)
        fireEvent.click(await screen.findByRole('button', { name: 'moveUp Variant' }))
        expect(onChange).toHaveBeenLastCalledWith([two, one])
        expect(get).toHaveBeenCalledWith('/products/lookup/selected', expect.objectContaining({ params: { ids: [one, two] } }))
    })

    it('retains an unavailable persisted ID for explicit removal rather than silently changing the draft', async () => {
        get.mockResolvedValue({ data: [] })
        const onChange = vi.fn()
        render(<CuratedProductSelector ids={[missing]} onChange={onChange} />)
        await waitFor(() => expect(get).toHaveBeenCalledWith('/products/lookup/selected', expect.anything()))
        expect(screen.getByText('unavailable')).toBeTruthy()
        expect(onChange).not.toHaveBeenCalled()
        fireEvent.click(screen.getByRole('button', { name: 'removeProduct 1' }))
        expect(onChange).toHaveBeenCalledWith([])
    })
})
