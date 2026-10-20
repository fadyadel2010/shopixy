import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import SmartOfferEditor from './SmartOfferEditor'

vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key: string) => key }) }))
vi.mock('../hooks/useRTL', () => ({ useRTL: () => ({ dir: 'rtl' }) }))
afterEach(cleanup)

const persisted = { id: 'safe-fixture', name: 'Persisted', status: 'Inactive', priority: 0, triggerType: 'TimeDelay', pagesScope: 'Product',
    delaySeconds: 10, scrollPercent: 45, maxDisplaysPerUser: 3, showOncePerSession: false,
    popupTitle: 'عنوان — Title', popupDescription: null, buttonText: 'Continue', couponCode: 'SAFE10',
    couponDiscountType: 'Percentage', couponDiscountValue: 10, couponMinOrderAmount: 0, couponMaxDiscountAmount: 5 }
const props = { open: true, offer: persisted, saving: false, error: '', onClose: vi.fn(), onSave: vi.fn() }
const field = (name: string) => screen.getByLabelText(`SmartOfferEditor.fields.${name}`, { exact: true })
const form = () => document.querySelector('form')!

describe('Smart Offer editing sessions', () => {
    it('retains the current draft and DOM controls when a refreshed offer object arrives', () => {
        const view = render(<SmartOfferEditor {...props} />)
        const priority = field('priority')
        fireEvent.change(priority, { target: { value: '17' } })
        fireEvent.change(field('name'), { target: { value: 'Unsaved draft' } })
        view.rerender(<SmartOfferEditor {...props} offer={{ ...persisted, priority: 99, name: 'Late read' }} error="Save failed" />)
        expect(field('priority')).toBe(priority)
        expect(priority).toHaveValue(17)
        expect(field('name')).toHaveValue('Unsaved draft')
        expect(screen.getByText('Save failed')).toBeVisible()
    })

    it('does not inject global input styles into the Admin document', () => {
        const { container } = render(<SmartOfferEditor {...props} />)
        expect(container.querySelector('style')).toBeNull()
        expect([...document.querySelectorAll('style')].some(style => /\.input\s*\{/.test(style.textContent || ''))).toBe(false)
    })

    it('shows the public content preview without disclosing the private coupon code or inventing a description', () => {
        render(<SmartOfferEditor {...props} />)
        expect(screen.getByRole('heading', { name: persisted.popupTitle })).toBeVisible()
        expect(screen.queryByText('SAFE10')).toBeNull()
        expect(screen.queryByText('Use this space for a short conversion-focused message.')).toBeNull()
        fireEvent.change(field('title'), { target: { value: 'New live title' } })
        expect(screen.getByRole('heading', { name: 'New live title' })).toBeVisible()
    })

    it('initializes a fresh session on reopen or when switching records, not on ordinary renders', () => {
        const view = render(<SmartOfferEditor {...props} />)
        fireEvent.change(field('name'), { target: { value: 'Discard me' } })
        view.rerender(<SmartOfferEditor {...props} open={false} />)
        expect(document.querySelector('form')).toBeNull()
        view.rerender(<SmartOfferEditor {...props} />)
        expect(field('name')).toHaveValue('Persisted')
        view.rerender(<SmartOfferEditor {...props} offer={{ ...persisted, id: 'other', name: 'Other' }} />)
        expect(field('name')).toHaveValue('Other')
    })

    it('retains cleared numbers and quick select edits, serializing zero, null and decimals correctly', () => {
        const save = vi.fn()
        render(<SmartOfferEditor {...props} onSave={save} />)
        fireEvent.change(field('priority'), { target: { value: '' } })
        expect(field('priority')).toHaveValue(null)
        fireEvent.change(field('delaySeconds'), { target: { value: '' } })
        for (const value of ['ScrollDepth', 'BeforeCheckout', 'ExitIntent']) fireEvent.change(field('triggerType'), { target: { value } })
        fireEvent.change(field('pagesScope'), { target: { value: 'Cart' } })
        fireEvent.change(field('discountValue'), { target: { value: '12.75' } })
        fireEvent.change(field('couponCode'), { target: { value: ' safe10 ' } })
        fireEvent.submit(form())
        expect(save).toHaveBeenCalledWith(expect.objectContaining({ priority: 0, delaySeconds: null, scrollPercent: 45,
            triggerType: 'ExitIntent', pagesScope: 'Cart', couponCode: 'SAFE10', couponDiscountValue: 12.75, couponMinOrderAmount: 0, showOncePerSession: false }))
    })

    it('derives timing and coupon constraints from the backend and preserves other fields after errors', () => {
        const view = render(<SmartOfferEditor {...props} />)
        expect(field('delaySeconds')).toBeRequired()
        expect(field('delaySeconds')).toHaveAttribute('min', '1')
        fireEvent.change(field('triggerType'), { target: { value: 'ScrollDepth' } })
        expect(field('scrollPercent')).toBeRequired()
        fireEvent.change(field('scrollPercent'), { target: { value: '101' } })
        expect(field('scrollPercent')).toBeInvalid()
        view.rerender(<SmartOfferEditor {...props} error="Validation failed" />)
        expect(field('scrollPercent')).toHaveValue(101)
        fireEvent.change(field('scrollPercent'), { target: { value: '50' } })
        expect(field('scrollPercent')).toBeValid()
        expect(field('title')).toHaveValue(persisted.popupTitle)
        fireEvent.change(field('couponCode'), { target: { value: '   ' } })
        expect(field('discountValue')).toBeDisabled()
    })

    it('has an accessible RTL dialog and locks dismissal and all form actions while saving', () => {
        render(<SmartOfferEditor {...props} saving />)
        const dialog = screen.getByRole('dialog', { name: 'SmartOfferEditor.titles.edit' })
        expect(dialog).toHaveAttribute('dir', 'rtl')
        expect(within(dialog).getByRole('button', { name: 'SmartOfferEditor.actions.closeEditor' })).toBeDisabled()
        expect(within(dialog).getByRole('button', { name: 'SmartOfferEditor.actions.cancel' })).toBeDisabled()
        expect(field('priority')).toBeDisabled()
    })

    it('keeps preview content synchronized when optional copy, artwork and color are rapidly replaced or cleared', () => {
        render(<SmartOfferEditor {...props} />)
        fireEvent.change(field('description'), { target: { value: 'Current description' } })
        expect(within(screen.getByRole('complementary')).getByText('Current description')).toBeVisible()
        for (const value of ['/first-artwork.png', '/latest-artwork.png']) fireEvent.change(field('imageUrl'), { target: { value } })
        expect(screen.getByRole('img', { name: persisted.popupTitle })).toHaveAttribute('src', '/latest-artwork.png')
        fireEvent.change(field('backgroundColor'), { target: { value: '#2563eb' } })
        expect(screen.getByLabelText('SmartOfferEditor.fields.colorPicker')).toHaveValue('#2563eb')
        fireEvent.change(field('description'), { target: { value: '' } })
        fireEvent.change(field('imageUrl'), { target: { value: '' } })
        expect(screen.queryByText('Current description')).toBeNull()
        expect(screen.queryByRole('img')).toBeNull()
        expect(field('priority')).toHaveValue(0)
    })

    it('shows admitted numeric enum values as their corresponding selected options', () => {
        render(<SmartOfferEditor {...props} offer={{ ...persisted, status: 0, triggerType: 2, pagesScope: 4, couponDiscountType: 1 }} />)
        expect(field('status')).toHaveValue('Inactive')
        expect(field('triggerType')).toHaveValue('ScrollDepth')
        expect(field('pagesScope')).toHaveValue('Cart')
        expect(field('couponDiscountType')).toHaveValue('FixedAmount')
    })
})
