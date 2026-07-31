import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import type React from 'react'
import ProductWorkspacePage from './ProductWorkspacePage'

const productFormMock = vi.hoisted(() => vi.fn((props: {
    productId?: string | null
    focusSection?: string | null
    onSave: (savedProduct: { id: string, hasVariants?: boolean }) => void
}) => (
    <div>
        <div>productId:{props.productId || 'new'}</div>
        <div>focus:{props.focusSection || 'none'}</div>
        <button type="button" onClick={() => props.onSave({ id: 'product-created', hasVariants: true })}>
            save variant parent
        </button>
    </div>
)))

vi.mock('../components/ProductForm', () => ({
    default: productFormMock,
}))

vi.mock('../components/ProductWorkspaceLayout', () => ({
    default: ({ topBar, children }: { topBar: React.ReactNode, children: React.ReactNode }) => (
        <main>
            {topBar}
            {children}
        </main>
    ),
}))

vi.mock('../components/AiProductGeneratorModal', () => ({
    default: () => null,
}))

vi.mock('../components/Toast', () => ({
    useToast: () => ({
        showError: vi.fn(),
        showSuccess: vi.fn(),
    }),
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string, fallback?: string) => fallback || key,
    }),
}))

function LocationProbe() {
    const location = useLocation()
    return <div>location:{location.pathname}{location.search}</div>
}

describe('ProductWorkspacePage product creation navigation', () => {
    it('redirects created variant parents to edit mode focused on variants', async () => {
        render(
            <MemoryRouter initialEntries={['/products/new/edit']}>
                <Routes>
                    <Route
                        path="/products/:productId/edit"
                        element={(
                            <>
                                <LocationProbe />
                                <ProductWorkspacePage />
                            </>
                        )}
                    />
                </Routes>
            </MemoryRouter>
        )

        fireEvent.click(screen.getByRole('button', { name: 'save variant parent' }))

        await waitFor(() => {
            expect(screen.getByText('location:/products/product-created/edit?focus=variants')).toBeInTheDocument()
            expect(screen.getByText('productId:product-created')).toBeInTheDocument()
            expect(screen.getByText('focus:variants')).toBeInTheDocument()
        })
    })
})
