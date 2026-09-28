import { describe, expect, it } from 'vitest'
import {
    deriveCurrentCmsPageIdentityPresentation,
    deriveCurrentCmsPageListPresentation,
    deriveCurrentCmsPageVersionHistoryPresentation,
} from './cmsPageIdentityAuthority'

const tenantId = '11111111-1111-4111-8111-111111111111'
const pageId = '22222222-2222-4222-8222-222222222222'
const recordId = '33333333-3333-4333-8333-333333333333'
const user = { tenantId }

function page(overrides = {}) {
    return {
        id: pageId,
        tenantId,
        slug: 'about-us',
        title: 'About Us',
        contentType: 'RichText',
        hasPendingChanges: true,
        draft: {
            recordId,
            slug: 'about-us',
            title: 'About Us',
            contentType: 'RichText',
            content: '<p>About</p>',
            version: 2,
            schemaVersion: 1,
            seo: {
                metaTitle: 'About Us',
                metaDescription: 'About our store.',
                metaKeywords: 'about,store',
                openGraphImageUrl: '/media/cms/about.png',
                canonicalUrl: 'https://evil.test/forged',
                robots: 'noindex',
                structuredData: '{"@context":"https://schema.org"}',
            },
        },
        published: null,
        ...overrides,
    }
}

describe('CMS page identity authority admission', () => {
    it('admits a backend page DTO only when tenant and expected page id match', () => {
        const admitted = deriveCurrentCmsPageIdentityPresentation(page(), user, { expectedPageId: pageId })

        expect(admitted?.id).toBe(pageId)
        expect(admitted?.tenantId).toBe(tenantId)
        expect(admitted?.draft?.version).toBe(2)
    })

    it('rejects route replay and foreign tenant page DTOs', () => {
        expect(deriveCurrentCmsPageIdentityPresentation(page(), user, {
            expectedPageId: '44444444-4444-4444-8444-444444444444',
        })).toBeNull()

        expect(deriveCurrentCmsPageIdentityPresentation(page({
            tenantId: '55555555-5555-4555-8555-555555555555',
        }), user, { expectedPageId: pageId })).toBeNull()
    })

    it('rejects DTOs where canonical slug no longer matches the admitted current version', () => {
        expect(deriveCurrentCmsPageIdentityPresentation(page({
            slug: 'old-slug',
        }), user, { expectedPageId: pageId })).toBeNull()
    })

    it('admits only backend-supported SEO fields and strips injected SEO authority', () => {
        const admitted = deriveCurrentCmsPageIdentityPresentation(page(), user, { expectedPageId: pageId })

        expect(admitted?.draft?.seo).toEqual({
            metaTitle: 'About Us',
            metaDescription: 'About our store.',
            metaKeywords: 'about,store',
            openGraphImageUrl: '/media/cms/about.png',
        })
        expect(admitted?.draft?.seo?.canonicalUrl).toBeUndefined()
        expect(admitted?.draft?.seo?.robots).toBeUndefined()
        expect(admitted?.draft?.seo?.structuredData).toBeUndefined()
    })

    it('rejects malformed SEO evidence before editor presentation', () => {
        expect(deriveCurrentCmsPageIdentityPresentation(page({
            draft: {
                ...page().draft,
                seo: {
                    metaTitle: 'About Us',
                    metaDescription: 'About our store.',
                    metaKeywords: 'about,store',
                    openGraphImageUrl: 'javascript:alert(1)',
                },
            },
        }), user, { expectedPageId: pageId })).toBeNull()

        expect(deriveCurrentCmsPageIdentityPresentation(page({
            draft: {
                ...page().draft,
                seo: {
                    metaTitle: { forged: true },
                },
            },
        }), user, { expectedPageId: pageId })).toBeNull()
    })

    it('admits complete bilingual snapshots and rejects partial Arabic evidence', () => {
        const english = {
            title: 'About Us',
            contentType: 'RichText',
            content: '<p>About</p>',
            schemaVersion: 1,
            seo: page().draft.seo,
        }
        const arabic = {
            title: 'من نحن',
            contentType: 'RichText',
            content: '<p>محتوى عربي</p>',
            schemaVersion: 1,
            seo: {
                metaTitle: 'من نحن',
                metaDescription: null,
                metaKeywords: null,
                openGraphImageUrl: null,
            },
        }
        const bilingualPage = page({
            draft: {
                ...page().draft,
                translations: { en: english, ar: arabic },
            },
        })

        const admitted = deriveCurrentCmsPageIdentityPresentation(bilingualPage, user, { expectedPageId: pageId })
        expect(admitted?.draft?.translations?.ar?.title).toBe('من نحن')

        expect(deriveCurrentCmsPageIdentityPresentation(page({
            draft: {
                ...page().draft,
                translations: { en: english, ar: { ...arabic, content: '' } },
            },
        }), user, { expectedPageId: pageId })).toBeNull()
    })

    it('admits list rows as one backend-owned collection and rejects duplicate or foreign evidence', () => {
        const list = [{
            id: pageId,
            tenantId,
            slug: 'about-us',
            title: 'About Us',
            contentType: 'RichText',
            hasDraft: true,
            hasPublished: false,
            status: 'Draft',
            draftVersion: 1,
            publishedVersion: null,
        }]

        expect(deriveCurrentCmsPageListPresentation(list, user)).toHaveLength(1)
        expect(deriveCurrentCmsPageListPresentation([...list, { ...list[0] }], user)).toBeNull()
        expect(deriveCurrentCmsPageListPresentation([{ ...list[0], tenantId: '55555555-5555-4555-8555-555555555555' }], user)).toBeNull()
    })

    it('admits version history only with current tenant, page, unique versions, and backend snapshots', () => {
        const history = {
            pageId,
            tenantId,
            currentDraftVersion: null,
            publishedVersion: 3,
            versions: [
                {
                    version: 3,
                    isPublished: true,
                    isCurrentDraft: false,
                    updatedAtUtc: new Date().toISOString(),
                    snapshot: {
                        ...page().draft,
                        version: 3,
                    },
                },
                {
                    version: 2,
                    isPublished: false,
                    isCurrentDraft: false,
                    updatedAtUtc: new Date().toISOString(),
                    snapshot: {
                        ...page().draft,
                        version: 2,
                    },
                },
            ],
        }

        expect(deriveCurrentCmsPageVersionHistoryPresentation(history, user, { expectedPageId: pageId })?.versions).toHaveLength(2)
        expect(deriveCurrentCmsPageVersionHistoryPresentation({ ...history, tenantId: '55555555-5555-4555-8555-555555555555' }, user, { expectedPageId: pageId })).toBeNull()
        expect(deriveCurrentCmsPageVersionHistoryPresentation({ ...history, versions: [history.versions[0], history.versions[0]] }, user, { expectedPageId: pageId })).toBeNull()
    })
})
