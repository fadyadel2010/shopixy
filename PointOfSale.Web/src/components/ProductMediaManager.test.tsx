import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ProductMediaManager from './ProductMediaManager'

const { get, post, showError, showSuccess } = vi.hoisted(() => ({
    get: vi.fn(), post: vi.fn(), showError: vi.fn(), showSuccess: vi.fn(),
}))
vi.mock('../services/api', () => ({ default: { get, post } }))
vi.mock('../common/confirm/ConfirmDialogProvider', () => ({
    useConfirmDialog: () => ({ confirm: vi.fn() }),
}))
vi.mock('./Toast', () => ({ useToast: () => ({ showError, showSuccess }) }))
vi.mock('./MediaGalleryGrid', () => ({
    default: ({ items }: { items: { url: string }[] }) => (
        <div data-testid="gallery">{items.map(item => <img key={item.url} src={item.url} alt="Uploaded product" />)}</div>
    ),
}))

const productId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'

describe('product media upload contract', () => {
    let stored: { id: string; url: string; mediaType: string; position: number }[]

    beforeEach(() => {
        stored = []
        get.mockReset().mockImplementation(() => Promise.resolve({ data: [...stored] }))
        post.mockReset().mockImplementation((url, data, options) => {
            if (url === '/upload') {
                expect(data).toBeInstanceOf(FormData)
                expect(data.get('entityType')).toBe('product')
                expect(data.get('entityId')).toBe(productId)
                return Promise.resolve({ data: { url: `/api/upload/product-media/${productId}/${crypto.randomUUID().replace(/-/g, '')}.jpg`, contentType: (data.get('file') as File).type } })
            }
            expect(url).toBe(`/admin/products/${productId}/media`)
            expect(data.url).toMatch(/^\/api\/upload\/product-media\//)
            expect(options.idempotencyKey).toBeTruthy()
            const item = { ...data, id: crypto.randomUUID(), position: stored.length + 1 }
            stored.push(item)
            return Promise.resolve({ data: item })
        })
        showError.mockReset()
        showSuccess.mockReset()
    })

    it.each([
        ['photo.jpg', 'image/jpeg'], ['photo.png', 'image/png'], ['photo.webp', 'image/webp'],
    ])('uploads %s, refreshes its gallery, and retains it after remount', async (name, mime) => {
        const view = render(<ProductMediaManager productId={productId} />)
        await screen.findByText('Upload Images')
        const file = new File(['image bytes'], name, { type: mime })
        fireEvent.change(view.container.querySelector('input[accept="image/jpeg,image/png,image/webp"]')!, { target: { files: [file] } })
        await waitFor(() => expect(screen.getByAltText('Uploaded product')).toHaveAttribute('src', stored[0]?.url))
        expect(post).toHaveBeenCalledTimes(2)
        expect((post.mock.calls[0][1] as FormData).get('file')).toBe(file)
        expect(post.mock.calls[1][1].mediaType).toBe('image')
        view.unmount()
        render(<ProductMediaManager productId={productId} />)
        await waitFor(() => expect(screen.getByAltText('Uploaded product')).toHaveAttribute('src', stored[0].url))
    })

    it('registers multiple images and permits another upload without losing earlier media', async () => {
        const { container } = render(<ProductMediaManager productId={productId} />)
        await screen.findByText('Upload Images')
        const input = container.querySelector('input[accept="image/jpeg,image/png,image/webp"]')!
        fireEvent.change(input, { target: { files: [new File(['a'], 'a.jpg', { type: 'image/jpeg' }), new File(['b'], 'b.jpg', { type: 'image/jpeg' })] } })
        await waitFor(() => expect(screen.getAllByAltText('Uploaded product')).toHaveLength(2))
        fireEvent.change(input, { target: { files: [new File(['c'], 'c.jpg', { type: 'image/jpeg' })] } })
        await waitFor(() => expect(screen.getAllByAltText('Uploaded product')).toHaveLength(3))
    })

    it('rejects invalid MIME and leaves empty selections untouched', async () => {
        const { container } = render(<ProductMediaManager productId={productId} />)
        await screen.findByText('Upload Images')
        const input = container.querySelector('input[accept="image/jpeg,image/png,image/webp"]')!
        fireEvent.change(input, { target: { files: [] } })
        fireEvent.change(input, { target: { files: [new File(['bad'], 'bad.gif', { type: 'image/gif' })] } })
        expect(post).not.toHaveBeenCalled()
        expect(showError).toHaveBeenCalledWith('Only JPG, PNG, WEBP, and MP4 files are supported.')
    })

    it('rejects extension and size mismatches before sending any bytes', async () => {
        const { container } = render(<ProductMediaManager productId={productId} />)
        await screen.findByText('Upload Images')
        const input = container.querySelector('input[accept="image/jpeg,image/png,image/webp"]')!
        fireEvent.change(input, { target: { files: [new File(['bad'], 'photo.txt', { type: 'image/jpeg' })] } })
        expect(showError).toHaveBeenCalledWith('File extension does not match its image or video type.')
        const oversized = { name: 'photo.jpg', type: 'image/jpeg', size: 10 * 1024 * 1024 + 1 }
        fireEvent.change(input, { target: { files: [oversized] } })
        expect(showError).toHaveBeenCalledWith('Image size exceeds 10MB limit.')
        expect(post).not.toHaveBeenCalled()
    })

    it('shows server field validation instead of a generic invalid-payload error', async () => {
        post.mockRejectedValueOnce({ response: {
            status: 400, data: { message: 'The request payload is invalid.', data: { 'request.File': ['File is required.'] } }
        } })
        const { container } = render(<ProductMediaManager productId={productId} />)
        await screen.findByText('Upload Images')
        fireEvent.change(container.querySelector('input[accept="image/jpeg,image/png,image/webp"]')!, {
            target: { files: [new File(['a'], 'a.jpg', { type: 'image/jpeg' })] }
        })
        await waitFor(() => expect(showError).toHaveBeenCalledWith('File is required.'))
    })
})
