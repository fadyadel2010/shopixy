import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ProductReadDto } from '../../../PointOfSale.Storefront/src/types/product'
import { ProductDetailsPage, useVariantSelector } from './fixtures/storefrontProductAuthority.fixture'

const probes = vi.hoisted(() => ({ identifier: 'first', read: vi.fn(), resolve: vi.fn(), section: vi.fn(), view: vi.fn() }))
vi.mock('../../../PointOfSale.Storefront/node_modules/react', async () => vi.importActual('react'))
vi.mock('../../../PointOfSale.Storefront/node_modules/react-router-dom', () => ({ useParams: () => ({ slug: probes.identifier }) }))
vi.mock('@/components/layout', () => ({ Layout: ({ children }: { children: React.ReactNode }) => <main>{children}</main> }))
vi.mock('@/components/common', () => ({ ProductDetailSkeleton: () => <p>Loading backend product</p> }))
vi.mock('@/components/sections/SectionRenderer', () => ({ SectionRenderer: ({ sectionProps }: { sectionProps: { product: ProductReadDto } }) => {
    probes.section(sectionProps.product)
    return <h1>{sectionProps.product.name}</h1>
} }))
vi.mock('@/services/api', () => ({ fetchProductByIdentifier: probes.read, fetchCategories: () => Promise.resolve([]) }))
vi.mock('@/services/api/storefrontApi', () => ({ resolveProductVariant: probes.resolve }))
vi.mock('@/analytics', () => ({ useAnalytics: () => ({ productView: probes.view }) }))
vi.mock('@/i18n', () => ({ useLocalizedContent: () => ({ t: (key: string) => key }) }))
vi.mock('@/store', () => ({ useStoreConfigStore: () => ({}), selectConfig: vi.fn() }))
vi.mock('@/utils', () => ({ resolveProductImage: () => null }))
vi.mock('@/storefront/products/ProductTemplateResolver', () => ({ resolveProductTemplate: () => ({ templateId: 'default', sections: [{ id: 'hero', type: 'product-hero', order: 0, enabled: true }] }) }))

function deferred<T>() {
    let resolve!: (value: T) => void
    const promise = new Promise<T>(accept => { resolve = accept })
    return { resolve, promise }
}
const product = (id: string, name = id) => ({ id, name, hasVariants: true, variants: [], media: [], pricing: { currentPrice: 123 } } as unknown as ProductReadDto)

beforeEach(() => { vi.clearAllMocks(); probes.identifier = 'first' })
afterEach(cleanup)

describe('product detail presentation request ownership', () => {
    it('forwards backend product fields without replacement to every runtime section', async () => {
        const backend = { ...product('first', 'Backend name'), description: undefined, categoryName: 'Backend category' }
        probes.read.mockResolvedValueOnce(backend)
        render(<ProductDetailsPage />)
        await screen.findByRole('heading', { name: 'Backend name' })
        expect(probes.section).toHaveBeenLastCalledWith(backend)
        expect(probes.section.mock.calls[probes.section.mock.calls.length - 1]?.[0]).toBe(backend)
    })
    it('never replaces a newer route with an older late product response', async () => {
        const first = deferred<ProductReadDto>()
        probes.read.mockReturnValueOnce(first.promise).mockResolvedValueOnce(product('second'))
        const { rerender } = render(<ProductDetailsPage />)
        probes.identifier = 'second'
        rerender(<ProductDetailsPage />)
        await screen.findByRole('heading', { name: 'second' })
        await act(async () => first.resolve(product('first')))
        expect(screen.getByRole('heading')).toHaveTextContent('second')
        expect(probes.view).not.toHaveBeenCalledWith('first', 'first')
    })
    it.each([null, new Error('PRODUCT_RESPONSE_AUTHORITY_FAILED')])('withholds missing/rejected backend product instead of restoring a previous route', async failure => {
        probes.read.mockResolvedValueOnce(product('first'))
        const { rerender } = render(<ProductDetailsPage />)
        await screen.findByRole('heading', { name: 'first' })
        probes.identifier = 'second'
        if (failure) probes.read.mockRejectedValueOnce(failure)
        else probes.read.mockResolvedValueOnce(null)
        rerender(<ProductDetailsPage />)
        await screen.findByRole('heading', { name: 'product.notFound' })
        expect(screen.queryByRole('heading', { name: 'first' })).toBeNull()
    })
})

describe('variant selector request ownership', () => {
    let state: ReturnType<typeof useVariantSelector>
    function Harness({ value }: { value: ProductReadDto }) { state = useVariantSelector(value); return <p>{state.selectedVariant?.id ?? 'No resolved variant'}</p> }
    it('does not accept an aborted option response arriving after its replacement', async () => {
        const initial = deferred<unknown>()
        const replacement = deferred<unknown>()
        probes.resolve.mockImplementation((_id, request) => Object.keys(request.selectedOptions).length ? replacement.promise : initial.promise)
        render(<Harness value={product('first')} />)
        await waitFor(() => expect(probes.resolve).toHaveBeenCalled())
        const initialCalls = probes.resolve.mock.calls.length
        act(() => state.selectAttribute('Color', 'Blue'))
        await waitFor(() => expect(probes.resolve).toHaveBeenCalledTimes(initialCalls + 1))
        expect(probes.resolve.mock.calls[0][2].aborted).toBe(true)
        await act(async () => replacement.resolve({ variant: { id: 'blue' } }))
        await screen.findByText('blue')
        await act(async () => initial.resolve({ variant: { id: 'old-red' } }))
        expect(screen.getByText('blue')).toBeInTheDocument()
        expect(state!.isResolvingVariant).toBe(false)
    })
    it('withholds variant authority after backend rejection even if candidates locally match', async () => {
        probes.resolve.mockRejectedValue(new Error('Rejected options'))
        render(<Harness value={product('first')} />)
        await waitFor(() => expect(state!.hasVariantResolutionError).toBe(true))
        expect(state!.selectedVariant).toBeNull()
    })
})
