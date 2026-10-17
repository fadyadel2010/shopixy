import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const toast = vi.hoisted(() => ({
    showError: vi.fn(),
    showInfo: vi.fn(),
    showSuccess: vi.fn(),
}))

const service = vi.hoisted(() => ({
    generateHomepageStarterTemplate: vi.fn(),
    getHomepagePreviewStoreConfig: vi.fn(),
    getHomepageSchemas: vi.fn(),
    getHomepageSections: vi.fn(),
    getHomepageTenants: vi.fn(),
    isHomepageWorkspaceConflict: (error: { status?: number; code?: string } | null) => (
        error?.status === 409 && error?.code === 'HOMEPAGE_WORKSPACE_CONFLICT'
    ),
    publishHomepageSections: vi.fn(),
    saveHomepageDraft: vi.fn(),
    translateMarketplaceContent: vi.fn(),
}))

vi.mock('./homepageService', () => service)
vi.mock('../../components/Toast', () => ({ useToast: () => toast }))
vi.mock('./HomepageBuilderPreview', () => ({
    default: ({ sections, storeConfig }: { sections: Array<{ data: { title?: string } }>, storeConfig?: { storeName?: string } | null }) => (
        <div data-testid="homepage-preview">
            {sections.map((section) => section.data.title).join('|')}:{storeConfig?.storeName || 'no-config'}
        </div>
    ),
}))
vi.mock('@dnd-kit/core', () => ({
    closestCenter: vi.fn(),
    DndContext: ({ children }: { children: ReactNode }) => <>{children}</>,
    DragOverlay: ({ children }: { children: ReactNode }) => <>{children}</>,
    KeyboardSensor: class KeyboardSensor {},
    PointerSensor: class PointerSensor {},
    useSensor: () => ({}),
    useSensors: () => [],
}))
vi.mock('@dnd-kit/sortable', () => ({
    SortableContext: ({ children }: { children: ReactNode }) => <>{children}</>,
    arrayMove: (items: unknown[]) => items,
    sortableKeyboardCoordinates: vi.fn(),
    useSortable: () => ({
        attributes: {},
        listeners: {},
        setNodeRef: vi.fn(),
        transform: null,
        transition: undefined,
        isDragging: false,
    }),
    verticalListSortingStrategy: {},
}))
vi.mock('@dnd-kit/utilities', () => ({ CSS: { Transform: { toString: () => undefined } } }))

import HomepageBuilderPage from './HomepageBuilderPage'

const tenantId = '11111111-1111-4111-8111-111111111111'
const sectionId = '22222222-2222-4222-8222-222222222222'
const schemas = [{
    type: 'hero',
    displayName: 'Hero',
    description: 'Homepage hero',
    version: 1,
    fields: [
        { key: 'title', label: 'Title', kind: 'text', required: true, options: [] },
        { key: 'subtitle', label: 'Subtitle', kind: 'text', required: true, options: [] },
        { key: 'imageUrl', label: 'Image URL', kind: 'image', required: true, options: [] },
        { key: 'ctaText', label: 'CTA Text', kind: 'text', required: true, options: [] },
        { key: 'ctaLink', label: 'CTA Link', kind: 'link', required: true, options: [] },
    ],
}]

function snapshot(workspaceVersion: string, title: string) {
    return {
        workspaceVersion,
        sections: [{
            id: sectionId,
            type: 'hero',
            order: 0,
            data: {
                title,
                subtitle: 'Subtitle',
                imageUrl: 'https://cdn.test/hero.jpg',
                ctaText: 'Shop now',
                ctaLink: '/products',
            },
            version: 1,
            schemaVersion: 1,
            isActive: true,
            isDraft: true,
            isPublished: false,
            tenantId: null,
            createdAt: '2026-08-03T00:00:00Z',
            updatedAt: '2026-08-03T00:00:00Z',
        }],
    }
}

function deferred<T>() {
    let resolve!: (value: T) => void
    const promise = new Promise<T>((resolver) => {
        resolve = resolver
    })
    return { promise, resolve }
}

function renderBuilder() {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    })

    return render(
        <QueryClientProvider client={queryClient}>
            <HomepageBuilderPage />
        </QueryClientProvider>,
    )
}

describe('HomepageBuilderPage authoring contract', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        vi.spyOn(window, 'confirm').mockReturnValue(true)
        service.getHomepageSchemas.mockResolvedValue(schemas)
        service.getHomepageTenants.mockResolvedValue([{
            id: tenantId,
            name: 'Preview Tenant',
            isActive: true,
            createdAtUtc: '2026-08-03T00:00:00Z',
        }])
        service.getHomepagePreviewStoreConfig.mockResolvedValue({ storeName: 'Preview Store' })
    })

    afterEach(() => {
        vi.restoreAllMocks()
    })

    it('fails closed until the authoritative versioned draft is loaded', async () => {
        const load = deferred<ReturnType<typeof snapshot>>()
        service.getHomepageSections.mockImplementation((_tenantId: string | null, mode: string) => (
            mode === 'published' ? Promise.resolve(snapshot('published-1', 'Published')) : load.promise
        ))
        renderBuilder()

        expect(await screen.findByText('Loading authoritative Homepage draft')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Save Draft' })).toBeDisabled()
        fireEvent.keyDown(window, { key: 's', ctrlKey: true })
        expect(service.saveHomepageDraft).not.toHaveBeenCalled()

        load.resolve(snapshot('version-1', 'Loaded'))
        expect(await screen.findByDisplayValue('Loaded')).toBeInTheDocument()
        await waitFor(() => {
            expect(screen.getByTestId('homepage-preview')).toHaveTextContent('Loaded:Preview Store')
        })
    })

    it('skips no-op save and adopts each response version for repeated saves', async () => {
        service.getHomepageSections.mockImplementation((_tenantId: string | null, mode: string) => Promise.resolve(
            mode === 'published' ? snapshot('published-1', 'Published') : snapshot('version-1', 'Loaded'),
        ))
        service.saveHomepageDraft
            .mockResolvedValueOnce(snapshot('version-2', 'Edited once'))
            .mockResolvedValueOnce(snapshot('version-3', 'Edited twice'))
        renderBuilder()

        const titleInput = await screen.findByDisplayValue('Loaded')
        expect(screen.getByRole('button', { name: 'Save Draft' })).toBeDisabled()
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        expect(service.saveHomepageDraft).not.toHaveBeenCalled()

        fireEvent.change(titleInput, { target: { value: 'Edited once' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        await waitFor(() => expect(service.saveHomepageDraft).toHaveBeenCalledTimes(1))
        expect(service.saveHomepageDraft.mock.calls[0][1]).toBe('version-1')
        expect(service.saveHomepageDraft.mock.calls[0][0].tenantId).toBeNull()

        fireEvent.change(await screen.findByDisplayValue('Edited once'), { target: { value: 'Edited twice' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        await waitFor(() => expect(service.saveHomepageDraft).toHaveBeenCalledTimes(2))
        expect(service.saveHomepageDraft.mock.calls[1][1]).toBe('version-2')
        expect(await screen.findByDisplayValue('Edited twice')).toBeInTheDocument()
    })

    it('refreshes authoritative state and recovers a conflict before the next save', async () => {
        let previewLoad = 0
        service.getHomepageSections.mockImplementation((_tenantId: string | null, mode: string) => {
            if (mode === 'published') {
                return Promise.resolve(snapshot('published-1', 'Published'))
            }
            previewLoad += 1
            return Promise.resolve(previewLoad === 1
                ? snapshot('version-1', 'Loaded')
                : snapshot('version-4', 'Authoritative'))
        })
        const conflict = Object.assign(new Error('Homepage changed.'), {
            status: 409,
            code: 'HOMEPAGE_WORKSPACE_CONFLICT',
        })
        service.saveHomepageDraft
            .mockRejectedValueOnce(conflict)
            .mockResolvedValueOnce(snapshot('version-5', 'Recovered edit'))
        renderBuilder()

        fireEvent.change(await screen.findByDisplayValue('Loaded'), { target: { value: 'Stale edit' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        expect(await screen.findByDisplayValue('Authoritative')).toBeInTheDocument()
        expect(toast.showInfo).toHaveBeenCalledWith(expect.stringContaining('latest authoritative draft'))

        fireEvent.change(screen.getByDisplayValue('Authoritative'), { target: { value: 'Recovered edit' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }))
        await waitFor(() => expect(service.saveHomepageDraft).toHaveBeenCalledTimes(2))
        expect(service.saveHomepageDraft.mock.calls[1][1]).toBe('version-4')
    })

    it('explicit refresh replaces clean state without issuing a save', async () => {
        let previewLoad = 0
        service.getHomepageSections.mockImplementation((_tenantId: string | null, mode: string) => {
            if (mode === 'published') {
                return Promise.resolve(snapshot('published-1', 'Published'))
            }
            previewLoad += 1
            return Promise.resolve(previewLoad === 1
                ? snapshot('version-1', 'Loaded')
                : snapshot('version-6', 'Refreshed'))
        })
        renderBuilder()

        expect(await screen.findByDisplayValue('Loaded')).toBeInTheDocument()
        fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

        expect(await screen.findByDisplayValue('Refreshed')).toBeInTheDocument()
        expect(service.saveHomepageDraft).not.toHaveBeenCalled()
    })

    it('keeps preview-only StoreConfig changes outside Homepage dirty state and version ownership', async () => {
        const secondTenantId = '33333333-3333-4333-8333-333333333333'
        service.getHomepageTenants.mockResolvedValue([
            {
                id: tenantId,
                name: 'Preview Tenant',
                isActive: true,
                createdAtUtc: '2026-08-03T00:00:00Z',
            },
            {
                id: secondTenantId,
                name: 'Second Preview Tenant',
                isActive: true,
                createdAtUtc: '2026-08-03T00:00:00Z',
            },
        ])
        service.getHomepageSections.mockImplementation((_tenantId: string | null, mode: string) => Promise.resolve(
            mode === 'published' ? snapshot('published-1', 'Published') : snapshot('version-1', 'Loaded'),
        ))
        renderBuilder()

        expect(await screen.findByDisplayValue('Loaded')).toBeInTheDocument()
        const previewTenantSelect = screen.getByText('Preview tenant').parentElement?.querySelector('select')
        expect(previewTenantSelect).not.toBeNull()
        fireEvent.change(previewTenantSelect!, { target: { value: secondTenantId } })

        await waitFor(() => {
            expect(service.getHomepagePreviewStoreConfig).toHaveBeenCalledWith(secondTenantId)
        })
        expect(screen.getByRole('button', { name: 'Save Draft' })).toBeDisabled()
        expect(service.saveHomepageDraft).not.toHaveBeenCalled()
    })
})
