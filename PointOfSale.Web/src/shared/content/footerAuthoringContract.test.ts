import { describe, expect, it } from 'vitest'
import {
    admitFooterConfiguration,
    getAuthoringErrorFeedback,
    getFooterDraftFingerprint,
    mapFooterDraftToRequest,
    validateFooterDraft,
} from './footerAuthoringContract'

const revision = 'a'.repeat(64)

function response() {
    return {
        scope: 'Tenant',
        revision,
        updatedAtUtc: '2026-08-03T00:00:00Z',
        preview: { token: 'must-not-enter-authoring-state' },
        sections: [{
            id: 'section-id',
            title: 'Company',
            titleLocalized: { en: 'Company', ar: 'الشركة' },
            order: 0,
            isActive: true,
            links: [{
                id: 'link-id',
                label: 'About',
                labelLocalized: { en: 'About', ar: 'عن المتجر' },
                type: 'Cms',
                url: null,
                cmsPageSlug: 'about',
                openInNewTab: false,
                order: 0,
                isActive: true,
            }],
        }],
        socialLinks: [{
            id: 'social-id',
            platform: 'Instagram',
            url: 'https://instagram.com/store',
            icon: null,
            order: 0,
            isActive: true,
        }],
    }
}

describe('footerAuthoringContract', () => {
    it('admits one canonical response and maps one ordered DTO with backend revision evidence', () => {
        const draft = admitFooterConfiguration(response(), 'Tenant')
        const request = mapFooterDraftToRequest(draft, draft.revision, true)

        expect(request.expectedRevision).toBe(revision)
        expect(request.sections[0]).toMatchObject({
            title: 'Company',
            titleLocalized: { en: 'Company', ar: 'الشركة' },
            order: 0,
        })
        expect(request.sections[0].links[0]).toMatchObject({ cmsPageSlug: 'about', url: null, order: 0 })
        expect(request.socialLinks[0].order).toBe(0)
    })

    it('rejects malformed response ordering and keeps preview metadata out of dirty state', () => {
        const first = admitFooterConfiguration(response(), 'Tenant')
        const previewChanged = response()
        previewChanged.preview = { token: 'different-preview-only-token' }
        previewChanged.updatedAtUtc = '2026-08-03T01:00:00Z'
        const second = admitFooterConfiguration(previewChanged, 'Tenant')

        expect(getFooterDraftFingerprint(first, true)).toBe(getFooterDraftFingerprint(second, true))
        const invalid = response()
        invalid.sections[0].order = 2
        expect(() => admitFooterConfiguration(invalid, 'Tenant')).toThrow(/invalid ordering/i)
    })

    it('blocks invalid URLs, missing protocols, duplicate links, missing fields, and invalid references', () => {
        const draft = admitFooterConfiguration(response(), 'Tenant')
        draft.sections[0].titleLocalized.en = ''
        draft.sections[0].links = [
            { ...draft.sections[0].links[0], _key: 'one', type: 'External', cmsPageSlug: '', url: 'example.com' },
            { ...draft.sections[0].links[0], _key: 'two', type: 'External', cmsPageSlug: '', url: 'example.com' },
            { ...draft.sections[0].links[0], _key: 'three', cmsPageSlug: 'missing' },
        ]
        draft.socialLinks.push({
            ...draft.socialLinks[0],
            _key: 'duplicate-social',
            url: 'https://instagram.com/other',
        })

        const issues = validateFooterDraft(draft, {
            isLocalized: true,
            cmsPages: [{ slug: 'about', title: 'About' }],
        })
        const codes = issues.map((item) => item.code)

        expect(codes).toContain('FOOTER_REQUIRED_FIELD')
        expect(codes).toContain('FOOTER_EXTERNAL_URL_INVALID')
        expect(codes).toContain('FOOTER_DUPLICATE_LINK')
        expect(codes).toContain('FOOTER_CMS_TARGET_NOT_PUBLISHED')
        expect(codes).toContain('FOOTER_DUPLICATE_SOCIAL_LINK')
    })

    it('enforces localized label and social icon length limits before save', () => {
        const draft = admitFooterConfiguration(response(), 'Tenant')
        draft.sections[0].links[0].labelLocalized.ar = 'س'.repeat(161)
        draft.socialLinks[0].icon = 'i'.repeat(81)

        const issues = validateFooterDraft(draft, {
            isLocalized: true,
            cmsPages: [{ slug: 'about', title: 'About' }],
        })

        expect(issues).toEqual(expect.arrayContaining([
            expect.objectContaining({ field: 'sections[0].links[0].labelLocalized.ar', code: 'FOOTER_FIELD_TOO_LONG' }),
            expect.objectContaining({ field: 'socialLinks[0].icon', code: 'FOOTER_FIELD_TOO_LONG' }),
        ]))
    })

    it('maps every structured authoring failure class to meaningful feedback', () => {
        const cases = [
            [{ status: 422, code: 'FOOTER_VALIDATION_ERROR' }, 'validation'],
            [{ status: 409, code: 'FOOTER_CONCURRENCY_CONFLICT' }, 'conflict'],
            [{ status: 409, code: 'RESOURCE_ALREADY_EXISTS' }, 'already-exists'],
            [{ status: 409, code: 'RESOURCE_ALREADY_PUBLISHED' }, 'already-published'],
            [{ status: 409, code: 'FOOTER_DUPLICATE_LINK' }, 'duplicate'],
            [{ status: 404, code: 'RESOURCE_NOT_FOUND' }, 'missing'],
            [{ status: 403, code: 'FORBIDDEN' }, 'forbidden'],
            [{ status: 500, code: 'INTERNAL_ERROR' }, 'unexpected'],
        ] as const

        for (const [error, kind] of cases) {
            const feedback = getAuthoringErrorFeedback(error)
            expect(feedback.kind).toBe(kind)
            expect(feedback.title).not.toMatch(/^save failed$/i)
            expect(feedback.message.length).toBeGreaterThan(0)
        }
    })

    it('prefers structured backend authoring errors over Axios transport codes', () => {
        const conflict = getAuthoringErrorFeedback({
            name: 'AxiosError',
            code: 'ERR_BAD_REQUEST',
            message: 'AxiosError: Request failed with status code 409',
            response: {
                status: 409,
                data: {
                    code: 'CONCURRENCY_CONFLICT',
                    message: 'This page changed before it could be published.',
                },
            },
        })

        expect(conflict.kind).toBe('conflict')
        expect(conflict.code).toBe('CONCURRENCY_CONFLICT')
        expect(conflict.message).toBe('This page changed before it could be published.')
        expect(`${conflict.title} ${conflict.message}`).not.toMatch(/AxiosError|ERR_BAD_REQUEST/)

        const validation = getAuthoringErrorFeedback({
            name: 'AxiosError',
            code: 'ERR_BAD_REQUEST',
            message: 'AxiosError: Request failed with status code 400',
            response: {
                status: 400,
                data: {
                    code: 'PUBLISH_FAILED',
                    message: 'Draft content is invalid.',
                },
            },
        })

        expect(validation.kind).toBe('validation')
        expect(validation.code).toBe('PUBLISH_FAILED')
        expect(validation.message).toBe('Draft content is invalid.')
        expect(`${validation.title} ${validation.message}`).not.toMatch(/AxiosError|ERR_BAD_REQUEST/)
    })
})
