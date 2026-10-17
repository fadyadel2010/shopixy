import { beforeEach, describe, expect, it, vi } from 'vitest'

const { apiGet, apiPost, apiPut } = vi.hoisted(() => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPut: vi.fn(),
}))

vi.mock('../../../../services/api', () => ({
    default: {
        delete: vi.fn(),
        get: apiGet,
        post: apiPost,
        put: apiPut,
    },
}))

import { cmsService } from './cmsService'
import {
    admitNavigationSnapshot,
    admitNavigationTargets,
    extractNavigationAdmissionError,
    extractNavigationConflict,
    mapNavigationDraftToRequest,
} from './navigationAuthoringContract'

const categoryId = '11111111-1111-4111-8111-111111111111'
const productId = '22222222-2222-4222-8222-222222222222'
const pageId = '33333333-3333-4333-8333-333333333333'

const buildSnapshot = (rowVersion = '41'): import('./navigationAuthoringContract').NavigationSnapshot => ({
    rowVersion,
    draft: {
        header: [
            { label: 'Category', type: 'category', targetId: categoryId, slug: 'shoes', enabled: true },
            { label: 'Product', type: 'product', targetId: productId, slug: 'running-shoe', path: '/products/running-shoe', enabled: true },
            { label: 'About', type: 'cms', targetId: pageId, slug: 'about', enabled: true },
        ],
        footer: [
            { label: 'Catalog', type: 'collection', collectionKey: 'all-products', path: '/products', enabled: true },
            { label: 'Help', type: 'custom', path: '/help', enabled: true },
            { label: 'Partner', type: 'external', path: 'https://example.com/shop', openInNewTab: true, enabled: true },
        ],
    },
    live: null,
    hasPendingChanges: true,
})

describe('canonical Navigation authoring contract', () => {
    beforeEach(() => {
        apiGet.mockReset()
        apiPost.mockReset()
        apiPut.mockReset()
    })

    it('maps every link type through the one DTO mapper and never submits backend-owned slugs', () => {
        const request = mapNavigationDraftToRequest(buildSnapshot())

        expect(request.rowVersion).toBe('41')
        expect(request.header).toEqual([
            expect.objectContaining({ type: 'category', targetId: categoryId, path: null }),
            expect.objectContaining({ type: 'product', targetId: productId, path: null }),
            expect.objectContaining({ type: 'cms', targetId: pageId, path: null }),
        ])
        expect(request.footer).toEqual([
            expect.objectContaining({ type: 'collection', collectionKey: 'all-products', path: null }),
            expect.objectContaining({ type: 'custom', path: '/help' }),
            expect.objectContaining({ type: 'external', path: 'https://example.com/shop', openInNewTab: true }),
        ])
        expect(request.header.every((link) => !('slug' in link))).toBe(true)
    })

    it('prevents obviously invalid custom and external authoring without duplicating entity visibility rules', () => {
        const custom = buildSnapshot()
        custom.draft.footer[1].path = 'admin'
        expect(() => mapNavigationDraftToRequest(custom)).toThrow('requires a storefront-relative path')

        const external = buildSnapshot()
        external.draft.footer[2].path = 'javascript:alert(1)'
        expect(() => mapNavigationDraftToRequest(external)).toThrow('requires a valid http(s) URL')

        external.draft.footer[2].path = `https://example.com/${'x'.repeat(2048)}`
        expect(() => mapNavigationDraftToRequest(external)).toThrow('requires a valid http(s) URL')
    })

    it('admits Navigation load responses with a positive backend RowVersion', async () => {
        const response = buildSnapshot('41')
        apiGet.mockResolvedValue({ data: response })

        await expect(cmsService.getNavigation()).resolves.toEqual(admitNavigationSnapshot(response))
        expect(apiGet).toHaveBeenCalledWith('/admin/cms/navigation', {})
    })

    it('admits load-time validation warnings and keeps broken CMS links repairable', () => {
        const brokenPageId = '44444444-4444-4444-8444-444444444444'
        const response = buildSnapshot('41')
        response.draft.footer.push({
            label: 'Broken CMS',
            type: 'cms',
            targetId: brokenPageId,
            enabled: true,
            validationStatus: 'invalid',
            validationCode: 'NAVIGATION_CMS_PAGE_NOT_FOUND',
            validationMessage: 'The selected CMS page has no active draft or publication.',
        })
        response.validationWarnings = [{
            code: 'NAVIGATION_CMS_PAGE_NOT_FOUND',
            message: 'The selected CMS page has no active draft or publication.',
            snapshot: 'draft',
            menu: 'footer',
            index: 3,
            field: 'targetId',
            linkType: 'cms',
            targetId: brokenPageId,
            status: 'invalid',
        }]
        response.brokenLinks = response.validationWarnings

        const admitted = admitNavigationSnapshot(response)

        expect(admitted.validationWarnings).toEqual([expect.objectContaining({
            code: 'NAVIGATION_CMS_PAGE_NOT_FOUND',
            menu: 'footer',
            index: 3,
            targetId: brokenPageId,
        })])
        expect(admitted.brokenLinks).toHaveLength(1)
        expect(admitted.draft.footer[3]).toMatchObject({
            label: 'Broken CMS',
            type: 'cms',
            targetId: brokenPageId,
            validationStatus: 'invalid',
            validationCode: 'NAVIGATION_CMS_PAGE_NOT_FOUND',
        })
        expect(() => mapNavigationDraftToRequest(admitted)).not.toThrow()
        expect(mapNavigationDraftToRequest(admitted).footer[3]).not.toHaveProperty('validationStatus')
    })

    it('admits an unresolved entity link only when an exact backend repair warning accompanies it', () => {
        const response = buildSnapshot('41')
        response.draft.footer.push({
            label: 'Missing CMS page',
            type: 'cms',
            slug: 'missing-page',
            enabled: true,
            validationStatus: 'invalid',
            validationCode: 'NAVIGATION_CMS_PAGE_NOT_FOUND',
            validationMessage: 'The selected CMS page no longer exists.',
        })
        response.validationWarnings = [{
            code: 'NAVIGATION_CMS_PAGE_NOT_FOUND',
            message: 'The selected CMS page no longer exists.',
            snapshot: 'draft',
            menu: 'footer',
            index: 3,
            field: 'targetId',
            linkType: 'cms',
            status: 'invalid',
        }]

        const admitted = admitNavigationSnapshot(response)

        expect(admitted.draft.footer[3]).toMatchObject({
            label: 'Missing CMS page',
            type: 'cms',
            targetId: null,
            validationStatus: 'invalid',
            validationCode: 'NAVIGATION_CMS_PAGE_NOT_FOUND',
        })
        try {
            mapNavigationDraftToRequest(admitted)
            expect.unreachable('Expected unresolved CMS target validation to fail')
        } catch (error) {
            expect(error).toMatchObject({
                code: 'NAVIGATION_CLIENT_INVALID',
                details: { menu: 'footer', index: 3, field: 'targetId', linkType: 'cms' },
            })
            expect((error as Error).message).toContain('requires a selected cms target')
        }

        response.validationWarnings[0].index = 2
        expect(() => admitNavigationSnapshot(response)).toThrow('backend-issued target identity')
    })

    it.each([
        ['snapshot', 'live'],
        ['menu', 'header'],
        ['linkType', 'product'],
        ['code', 'NAVIGATION_CMS_PAGE_DELETED'],
        ['status', 'warning'],
    ])('rejects unresolved entity links when the backend repair warning mismatches %s', (field, value) => {
        const response = buildSnapshot('41')
        response.draft.footer.push({
            label: 'Missing CMS page',
            type: 'cms',
            slug: 'missing-page',
            enabled: true,
            validationStatus: 'invalid',
            validationCode: 'NAVIGATION_CMS_PAGE_NOT_FOUND',
        })
        response.validationWarnings = [{
            code: 'NAVIGATION_CMS_PAGE_NOT_FOUND',
            message: 'The selected CMS page no longer exists.',
            snapshot: 'draft',
            menu: 'footer',
            index: 3,
            field: 'targetId',
            linkType: 'cms',
            status: 'invalid',
            [field]: value,
        }]

        expect(() => admitNavigationSnapshot(response)).toThrow('backend-issued target identity')
    })

    it('fails closed when load or mutation responses include an invalid backend RowVersion', () => {
        expect(() => admitNavigationSnapshot(buildSnapshot('0'))).toThrow('usable backend RowVersion')

        const malformed = buildSnapshot('rv-41')
        expect(() => admitNavigationSnapshot(malformed)).toThrow('usable backend RowVersion')
    })

    it('fails closed when load or mutation responses omit backend RowVersion or target identity', () => {
        const missingVersion = Object.fromEntries(Object.entries(buildSnapshot()).filter(([key]) => key !== 'rowVersion'))
        expect(() => admitNavigationSnapshot(missingVersion)).toThrow('usable backend RowVersion')

        const missingIdentity = buildSnapshot()
        missingIdentity.draft.header[0].targetId = null
        expect(() => admitNavigationSnapshot(missingIdentity)).toThrow('backend-issued target identity')
    })

    it('admits only the backend target-selection response contract', () => {
        const targets = admitNavigationTargets({
            categories: [{ type: 'category', targetId: categoryId, label: 'Shoes', state: 'visible' }],
            products: [{ type: 'product', targetId: productId, label: 'Shoe', state: 'visible' }],
            cmsPages: [{ type: 'cms', targetId: pageId, label: 'About', state: 'draft' }],
            collections: [{ type: 'collection', collectionKey: 'all-products', label: 'All products', state: 'available' }],
        })

        expect(targets.cmsPages[0]).toMatchObject({ targetId: pageId, state: 'draft' })
        expect(targets.collections[0].collectionKey).toBe('all-products')
    })

    it('maps backend admission failures to meaningful authoring feedback', () => {
        expect(extractNavigationAdmissionError({
            response: {
                status: 422,
                data: {
                    code: 'NAVIGATION_CMS_PAGE_UNPUBLISHED',
                    message: 'Publish the selected CMS page first.',
                    navigation: { menu: 'footer', index: 2, field: 'targetId', targetId: pageId },
                },
            },
        })).toEqual({
            code: 'NAVIGATION_CMS_PAGE_UNPUBLISHED',
            message: 'Publish the selected CMS page first.',
            details: { menu: 'footer', index: 2, field: 'targetId', targetId: pageId },
        })
    })

    it('classifies only the exact Navigation concurrency contract and admits its latest snapshot', () => {
        expect(extractNavigationConflict({
            response: {
                status: 409,
                data: {
                    code: 'CONCURRENCY_CONFLICT',
                    message: 'Reload Navigation.',
                    latestVersion: buildSnapshot('44'),
                },
            },
        })).toMatchObject({
            code: 'CONCURRENCY_CONFLICT',
            message: 'Reload Navigation.',
            latestVersion: { rowVersion: '44' },
        })
        expect(extractNavigationConflict({
            response: { status: 409, data: { code: 'SOME_OTHER_CONFLICT' } },
        })).toBeNull()
    })

    it('uses one save owner and adopts the complete accepted save response', async () => {
        const candidate = buildSnapshot('41')
        const accepted = buildSnapshot('42')
        apiPut.mockResolvedValue({ data: accepted })

        await expect(cmsService.updateNavigation(candidate)).resolves.toEqual(admitNavigationSnapshot(accepted))
        expect(apiPut).toHaveBeenCalledTimes(1)
        expect(apiPut).toHaveBeenCalledWith(
            '/admin/cms/navigation',
            mapNavigationDraftToRequest(candidate),
            { headers: { 'If-Match': '"41"' } },
        )
    })

    it('adopts the publish response so the next command receives the rotated RowVersion', async () => {
        const published = { ...buildSnapshot('43'), hasPendingChanges: false }
        apiPost.mockResolvedValue({ data: published })

        const adopted = await cmsService.publishNavigation('42')

        expect(adopted.rowVersion).toBe('43')
        expect(apiPost).toHaveBeenCalledWith('/admin/cms/navigation/publish', undefined, {
            headers: { 'If-Match': '"42"' },
        })
    })
})
