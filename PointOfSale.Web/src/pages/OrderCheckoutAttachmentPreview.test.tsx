import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import OrderCheckoutAttachmentPreview from './OrderCheckoutAttachmentPreview'
import { isPreviewableCheckoutImage } from './orderCheckoutImage'
import { useAuthStore } from '../store/authStore'
import {
    downloadAdminOrderCheckoutAttachment,
    getAdminOrderCheckoutAttachmentBlob,
} from '../services/adminOrdersApi'

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string, fallbackOrValues?: string | Record<string, unknown>, interpolationValues: Record<string, unknown> = {}) => {
            if (typeof fallbackOrValues !== 'string') return key
            return Object.entries(interpolationValues).reduce(
                (text, [name, value]) => text.split(`{{${name}}}`).join(String(value)),
                fallbackOrValues,
            )
        },
    }),
}))

vi.mock('../services/adminOrdersApi', () => ({
    downloadAdminOrderCheckoutAttachment: vi.fn(),
    getAdminOrderCheckoutAttachmentBlob: vi.fn(),
}))

const imageAttachment = {
    attachmentId: 'attachment-1',
    fileName: 'customer.png',
    contentType: 'image/png',
    mediaType: 'image',
    sizeBytes: 4,
}

describe('OrderCheckoutAttachmentPreview', () => {
    beforeEach(() => {
        useAuthStore.setState({ user: { effectivePermissions: ['storefront.orders.attachments.download'] } })
        vi.mocked(getAdminOrderCheckoutAttachmentBlob).mockReset()
        vi.mocked(downloadAdminOrderCheckoutAttachment).mockReset()
        vi.stubGlobal('URL', {
            ...URL,
            createObjectURL: vi.fn(() => 'blob:secure-preview'),
            revokeObjectURL: vi.fn(),
        })
    })

    afterEach(() => {
        cleanup()
        useAuthStore.setState({ user: null })
        vi.unstubAllGlobals()
    })

    it('previews only approved image media types', () => {
        expect(isPreviewableCheckoutImage(imageAttachment)).toBe(true)
        expect(isPreviewableCheckoutImage({ ...imageAttachment, contentType: 'image/svg+xml' })).toBe(false)
        expect(isPreviewableCheckoutImage({ ...imageAttachment, mediaType: 'file' })).toBe(false)
        expect(isPreviewableCheckoutImage({ ...imageAttachment, contentType: 'application/pdf' })).toBe(false)
    })

    it('loads an authenticated blob preview and revokes its object URL on unmount', async () => {
        vi.mocked(getAdminOrderCheckoutAttachmentBlob).mockResolvedValue(new Blob(['png'], { type: 'image/png' }))

        const view = render(
            <OrderCheckoutAttachmentPreview orderId="order-1" fieldLabel="Customer photo" attachment={imageAttachment} />,
        )

        const image = await screen.findByRole('img', { name: 'Customer photo attachment: customer.png' })
        expect(image).toHaveAttribute('src', 'blob:secure-preview')
        expect(getAdminOrderCheckoutAttachmentBlob).toHaveBeenCalledWith(
            'order-1',
            'attachment-1',
            expect.any(AbortSignal),
        )

        view.unmount()
        expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:secure-preview')
    })

    it('offers an explicit retry after a failed secure preview', async () => {
        vi.mocked(getAdminOrderCheckoutAttachmentBlob)
            .mockRejectedValueOnce(new Error('unavailable'))
            .mockResolvedValueOnce(new Blob(['png'], { type: 'image/png' }))

        render(<OrderCheckoutAttachmentPreview orderId="order-1" fieldLabel="Customer photo" attachment={imageAttachment} />)

        fireEvent.click(await screen.findByRole('button', { name: 'Retry preview' }))

        await waitFor(() => expect(getAdminOrderCheckoutAttachmentBlob).toHaveBeenCalledTimes(2))
        expect(await screen.findByRole('img')).toHaveAttribute('src', 'blob:secure-preview')
    })

    it('does not fetch a preview for non-image attachments and keeps download available', () => {
        render(
            <OrderCheckoutAttachmentPreview
                orderId="order-1"
                fieldLabel="Proof"
                attachment={{ ...imageAttachment, fileName: 'proof.pdf', contentType: 'application/pdf', mediaType: 'file' }}
            />,
        )

        expect(getAdminOrderCheckoutAttachmentBlob).not.toHaveBeenCalled()
        expect(screen.queryByRole('img')).not.toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'View / Download' })).toBeEnabled()
    })

    it.each(['Manager', 'Cashier', 'Warehouse'])('does not preview or offer downloads for %s with only order-view authority', (role) => {
        useAuthStore.setState({ user: { role, effectivePermissions: ['storefront.orders.view'] } })
        const view = render(<OrderCheckoutAttachmentPreview orderId="order-1" fieldLabel="Proof" attachment={imageAttachment} />)
        expect(view.container).toBeEmptyDOMElement()
        expect(getAdminOrderCheckoutAttachmentBlob).not.toHaveBeenCalled()
        expect(downloadAdminOrderCheckoutAttachment).not.toHaveBeenCalled()
    })

    it('downloads for an explicitly authorized user', async () => {
        vi.mocked(downloadAdminOrderCheckoutAttachment).mockResolvedValue(undefined)
        render(<OrderCheckoutAttachmentPreview orderId="order-1" fieldLabel="Proof" attachment={{ ...imageAttachment, mediaType: 'file' }} />)
        fireEvent.click(screen.getByRole('button', { name: 'View / Download' }))
        await waitFor(() => expect(downloadAdminOrderCheckoutAttachment).toHaveBeenCalledWith('order-1', 'attachment-1', 'customer.png'))
    })

    it('removes a loaded preview and revokes the blob when permission is lost', async () => {
        vi.mocked(getAdminOrderCheckoutAttachmentBlob).mockResolvedValue(new Blob(['png']))
        const view = render(<OrderCheckoutAttachmentPreview orderId="order-1" fieldLabel="Proof" attachment={imageAttachment} />)
        await screen.findByRole('img')
        act(() => useAuthStore.setState({ user: { effectivePermissions: ['storefront.orders.view'] } }))
        await waitFor(() => expect(view.container).toBeEmptyDOMElement())
        expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:secure-preview')
    })
})
