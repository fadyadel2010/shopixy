import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import ValidationDeleteModal from './ValidationDeleteModal'

const apiGetMock = vi.hoisted(() => vi.fn())
const apiDeleteMock = vi.hoisted(() => vi.fn())
const apiPutMock = vi.hoisted(() => vi.fn())

vi.mock('../services/api', () => ({
    default: {
        get: apiGetMock,
        delete: apiDeleteMock,
        put: apiPutMock,
    },
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string) => key,
        i18n: {
            language: 'en',
            dir: () => 'ltr',
        },
    }),
}))

describe('ValidationDeleteModal product lifecycle contract', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        apiGetMock.mockResolvedValue({ data: { canDelete: true } })
        apiDeleteMock.mockReset()
        apiDeleteMock.mockResolvedValue({ data: null })
        apiPutMock.mockReset()
        apiPutMock.mockResolvedValue({ data: null })
    })

    function renderProductDelete(overrides: Record<string, unknown> = {}) {
        const props = {
            isOpen: true,
            onClose: vi.fn(),
            entityType: 'product',
            entityId: 'product-1',
            entityName: 'Coffee',
            expectedVersion: '17',
            onDeleteSuccess: vi.fn(),
            onDeactivateSuccess: vi.fn(),
            ...overrides,
        }

        render(<ValidationDeleteModal {...props} />)
        return props
    }

    it('requires confirmation and sends the exact authoritative version plus idempotency key once', async () => {
        const props = renderProductDelete()

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1/can-delete'))
        expect(apiDeleteMock).not.toHaveBeenCalled()

        const deleteButton = await screen.findByRole('button', { name: 'common.delete' })
        fireEvent.click(deleteButton)
        fireEvent.click(deleteButton)

        await waitFor(() => expect(apiDeleteMock).toHaveBeenCalledTimes(1))
        expect(apiDeleteMock).toHaveBeenCalledWith(
            '/products/product-1',
            expect.objectContaining({
                headers: expect.objectContaining({
                    'If-Match': '"17"',
                    'Idempotency-Key': expect.stringContaining('product-delete:product-1:'),
                }),
            }),
        )
        await waitFor(() => expect(props.onDeleteSuccess).toHaveBeenCalledTimes(1))
        expect(props.onClose).toHaveBeenCalledTimes(1)
    })

    it('refuses a destructive request when the authoritative row version is missing', async () => {
        renderProductDelete({ expectedVersion: '' })

        expect(await screen.findByRole('alert')).toHaveTextContent('products.deleteVersionMissing')
        expect(screen.queryByRole('button', { name: 'common.delete' })).not.toBeInTheDocument()
        expect(apiDeleteMock).not.toHaveBeenCalled()
        expect(apiPutMock).not.toHaveBeenCalled()
    })

    it('presents stale delete as a refresh-required conflict without retrying or closing', async () => {
        apiDeleteMock.mockRejectedValueOnce({
            response: { status: 409, data: { message: 'Product authoring concurrency conflict.' } },
        })
        const props = renderProductDelete()

        fireEvent.click(await screen.findByRole('button', { name: 'common.delete' }))

        expect(await screen.findByRole('alert')).toHaveTextContent('products.deleteConcurrencyConflict')
        expect(apiDeleteMock).toHaveBeenCalledTimes(1)
        expect(props.onDeleteSuccess).not.toHaveBeenCalled()
        expect(props.onClose).not.toHaveBeenCalled()
        expect(screen.queryByRole('button', { name: 'common.markInactive' })).not.toBeInTheDocument()
    })

    it('does not expose raw diagnostics or claim definite failure for an ambiguous network outcome', async () => {
        apiDeleteMock.mockRejectedValueOnce(new Error('Axios network error Authorization Bearer secret'))
        renderProductDelete()

        fireEvent.click(await screen.findByRole('button', { name: 'common.delete' }))

        expect(await screen.findByRole('alert')).toHaveTextContent('products.deleteOutcomeUncertain')
        expect(document.body.textContent).not.toMatch(/Axios|Authorization|Bearer|secret/)
        expect(apiDeleteMock).toHaveBeenCalledTimes(1)
    })

    it('shows authoritative dependency blocking and deactivates with the same version contract', async () => {
        apiGetMock.mockResolvedValueOnce({
            data: {
                canDelete: false,
                errorCode: 'PRODUCT_USED_IN_SALES',
                messageEn: 'Product is used in sales transactions and cannot be deleted.',
            },
        })
        const props = renderProductDelete()

        expect(await screen.findByRole('alert')).toHaveTextContent('Product is used in sales transactions')
        fireEvent.click(screen.getByRole('button', { name: 'common.markInactive' }))

        await waitFor(() => expect(apiPutMock).toHaveBeenCalledWith(
            '/products/product-1/deactivate',
            undefined,
            expect.objectContaining({
                headers: expect.objectContaining({
                    'If-Match': '"17"',
                    'Idempotency-Key': expect.stringContaining('product-deactivate:product-1:'),
                }),
            }),
        ))
        expect(props.onDeactivateSuccess).toHaveBeenCalledTimes(1)
    })

    it('closes on Escape only while no mutation is pending', async () => {
        const props = renderProductDelete()
        await screen.findByRole('button', { name: 'common.delete' })

        fireEvent.keyDown(window, { key: 'Escape' })
        expect(props.onClose).toHaveBeenCalledTimes(1)
        expect(apiDeleteMock).not.toHaveBeenCalled()
    })
})
