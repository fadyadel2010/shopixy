import { beforeAll, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import ProductExportMenu from './ProductExportMenu'

// Minimal i18n double: t() returns the key so items are easy to locate.
vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string) => key,
        i18n: { resolvedLanguage: 'en', language: 'en' },
    }),
}))

// jsdom lacks the pointer-capture APIs Radix relies on for menus.
beforeAll(() => {
    if (typeof window.PointerEvent === 'undefined') {
        class PointerEventPolyfill extends MouseEvent {
            pointerId: number
            pointerType: string
            constructor(type: string, props: PointerEventInit = {}) {
                super(type, props)
                this.pointerId = props.pointerId ?? 0
                this.pointerType = props.pointerType ?? 'mouse'
            }
        }
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        ;(window as any).PointerEvent = PointerEventPolyfill
    }
    if (!Element.prototype.hasPointerCapture) {
        Element.prototype.hasPointerCapture = () => false
        Element.prototype.setPointerCapture = () => { /* noop */ }
        Element.prototype.releasePointerCapture = () => { /* noop */ }
    }
    if (!Element.prototype.scrollIntoView) {
        Element.prototype.scrollIntoView = () => { /* noop */ }
    }
})

const renderMenu = (props: Partial<React.ComponentProps<typeof ProductExportMenu>> = {}) => {
    const onExport = vi.fn()
    render(
        <ProductExportMenu
            onExport={onExport}
            selectedCount={0}
            available
            exporting={null}
            {...props}
        />,
    )
    return { onExport }
}

const openMenu = async (user: ReturnType<typeof userEvent.setup>) => {
    await user.click(screen.getByRole('button', { name: 'products.exportMenuAria' }))
}

describe('ProductExportMenu', () => {
    it('disables both "Selected" items with a visible reason when nothing is selected', async () => {
        const user = userEvent.setup()
        renderMenu({ selectedCount: 0 })
        await openMenu(user)

        const selectedItems = screen.getAllByText('products.exportSelected')
        expect(selectedItems).toHaveLength(2)
        for (const item of selectedItems) {
            expect(item.closest('[data-disabled]')).not.toBeNull()
        }
        expect(screen.getAllByText('products.exportNoSelection').length).toBe(2)
    })

    it('exports the current view for Excel and PDF with the correct scope', async () => {
        const user = userEvent.setup()
        const { onExport } = renderMenu({ selectedCount: 0 })
        await openMenu(user)

        const currentViewItems = screen.getAllByText('products.exportCurrentView')
        expect(currentViewItems).toHaveLength(2)

        await user.click(currentViewItems[0])
        expect(onExport).toHaveBeenCalledWith('excel', 'current-view')

        await openMenu(user)
        await user.click(screen.getAllByText('products.exportCurrentView')[1])
        expect(onExport).toHaveBeenCalledWith('pdf', 'current-view')
    })

    it('enables "Selected" items with the selection count once products are selected', async () => {
        const user = userEvent.setup()
        const { onExport } = renderMenu({ selectedCount: 3 })
        await openMenu(user)

        expect(screen.queryByText('products.exportNoSelection')).toBeNull()
        expect(screen.getAllByText('products.productsSelected').length).toBe(2)

        const selectedItems = screen.getAllByText('products.exportSelected')
        for (const item of selectedItems) {
            expect(item.closest('[data-disabled]')).toBeNull()
        }

        await user.click(selectedItems[1])
        expect(onExport).toHaveBeenCalledWith('pdf', 'selected')
    })

    it('disables the trigger when there is nothing to export', () => {
        renderMenu({ available: false })
        expect(screen.getByRole('button', { name: 'products.exportMenuAria' })).toBeDisabled()
    })

    it('shows a busy state while an export is in progress', () => {
        renderMenu({ exporting: 'excel' })
        expect(screen.getByRole('button', { name: 'products.exportMenuAria' })).toBeDisabled()
        expect(screen.getByText('products.exporting')).toBeInTheDocument()
    })
})
