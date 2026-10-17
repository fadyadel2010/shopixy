import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate, useLocation, useParams, useSearchParams } from 'react-router-dom'
import { fetchMarketplaceCmsPage } from './marketplaceCmsPublicService'
import { fetchMarketplaceFooter } from './marketplaceFooterPublicService'
import MarketplaceHeader from './MarketplaceHeader'
import MarketplacePublicFooter from './MarketplacePublicFooter'
import useLanguage from './useLanguage'
import { buildMarketplacePagePath } from './marketplaceLanguage'
import { sanitizeRichTextHtml } from '@/utils/htmlSanitizer'

const RESERVED_PUBLIC_SLUGS = new Set([
    'products',
    'categories',
    'customers',
    'suppliers',
    'inventory',
    'payments',
    'settings',
    'reports',
    'storefront',
    'analytics',
    'marketplace',
    'commercehub',
    'treasury',
    'super-admin',
])

export function MarketplaceCmsPublicPage() {
    const { lang, slug } = useParams()
    const location = useLocation()
    const [searchParams] = useSearchParams()
    const preview = searchParams.get('preview') === 'true'
    const previewToken = searchParams.get('previewToken')
    const { currentLang, isRTL } = useLanguage(lang)
    const { t } = useTranslation()
    const copy = useMemo(
        () => ({
            badge: t('marketplacePublic.cms.badge'),
            preview: t('marketplacePublic.cms.preview'),
            notFoundTitle: t('marketplacePublic.cms.notFoundTitle'),
            notFoundMessage: t('marketplacePublic.cms.notFoundMessage'),
            loadFailedTitle: t('marketplacePublic.cms.loadFailedTitle'),
            loadFailedMessage: t('marketplacePublic.cms.loadFailedMessage'),
            invalidContent: t('marketplacePublic.cms.invalidContent'),
            unsupportedBlock: (type) => t('marketplacePublic.cms.unsupportedBlock', { type }),
            footerFollow: t('marketplacePublic.common.followLabel'),
            goToAdmin: t('marketplacePublic.common.goToAdmin'),
            loading: t('marketplacePublic.cms.loading'),
            pageImageAlt: t('marketplacePublic.cms.pageImageAlt'),
        }),
        [t],
    )
    const [page, setPage] = useState(null)
    const [footer, setFooter] = useState(null)
    const [loading, setLoading] = useState(true)
    const [errorTitle, setErrorTitle] = useState('')
    const [error, setError] = useState('')

    useEffect(() => {
        if (!slug) {
            return
        }

        let cancelled = false

        async function loadPage() {
            setLoading(true)
            setErrorTitle('')
            setError('')

            try {
                const [pageResult, footerResult] = await Promise.allSettled([
                    fetchMarketplaceCmsPage(currentLang, slug, preview, previewToken),
                    fetchMarketplaceFooter(currentLang),
                ])

                if (pageResult.status !== 'fulfilled') {
                    throw pageResult.reason
                }

                if (!cancelled) {
                    setPage(pageResult.value)
                    setFooter(footerResult.status === 'fulfilled' ? footerResult.value : null)
                }
            } catch (loadError) {
                if (!cancelled) {
                    console.error('Failed to load marketplace CMS page', loadError)
                    const status = loadError?.response?.status ?? loadError?.status
                    const nextErrorTitle = status === 404 ? copy.notFoundTitle : copy.loadFailedTitle
                    const nextErrorMessage = status === 404 ? copy.notFoundMessage : copy.loadFailedMessage

                    setPage(null)
                    setFooter(null)
                    setErrorTitle(nextErrorTitle)
                    setError(nextErrorMessage)
                    applyFallbackSeo(nextErrorTitle, nextErrorMessage, currentLang, slug)
                }
            } finally {
                if (!cancelled) {
                    setLoading(false)
                }
            }
        }

        loadPage()

        return () => {
            cancelled = true
        }
    }, [copy.loadFailedMessage, copy.loadFailedTitle, copy.notFoundMessage, copy.notFoundTitle, currentLang, preview, previewToken, slug])

    useEffect(() => {
        if (!page || loading || !slug) {
            return
        }

        applySeo(page, preview, currentLang, slug)
    }, [currentLang, loading, page, preview, slug])

    if (!slug) {
        return <MarketplaceNotFound language={currentLang} copy={copy} />
    }

    if (!lang && !preview) {
        return <Navigate to={`${buildMarketplacePagePath(currentLang, slug)}${location.search}`} replace />
    }

    if (!preview && RESERVED_PUBLIC_SLUGS.has(slug.toLowerCase())) {
        return <MarketplaceNotFound language={currentLang} copy={copy} />
    }

    if (loading) {
        return <MarketplaceCmsLoadingState language={currentLang} copy={copy} />
    }

    if (error || !page) {
        return (
            <MarketplaceNotFound
                language={currentLang}
                copy={copy}
                title={errorTitle || copy.notFoundTitle}
                message={error || copy.notFoundMessage}
            />
        )
    }

    return (
        <div
            dir={isRTL ? 'rtl' : 'ltr'}
            className="min-h-screen bg-[radial-gradient(circle_at_top,_rgba(251,191,36,0.18),_transparent_38%),linear-gradient(180deg,#fff8eb_0%,#ffffff_55%)] px-6 py-16"
        >
            <div className="mx-auto max-w-5xl space-y-8">
                <MarketplaceHeader
                    language={currentLang}
                    badge={copy.badge}
                    title={page.title}
                    description={page.seo?.metaDescription || null}
                    statusLabel={preview ? copy.preview : null}
                />

                <MarketplaceCmsRenderer page={page} language={currentLang} copy={copy} />
                <MarketplacePublicFooter footer={footer} language={currentLang} followLabel={copy.footerFollow} />
            </div>
        </div>
    )
}

function MarketplaceCmsLoadingState({ language, copy }) {
    return (
        <div
            dir={language === 'ar' ? 'rtl' : 'ltr'}
            className="min-h-screen bg-[radial-gradient(circle_at_top,_rgba(251,191,36,0.18),_transparent_38%),linear-gradient(180deg,#fff8eb_0%,#ffffff_55%)] px-6 py-16"
        >
            <div className="mx-auto max-w-5xl space-y-8">
                <MarketplaceHeader language={language} badge={copy.badge} title={copy.loading} description={null} />
                <div className="space-y-6">
                    <div className="animate-pulse rounded-[36px] border border-amber-100 bg-white px-8 py-10 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
                        <div className="h-4 w-32 rounded-full bg-amber-100" />
                        <div className="mt-6 h-4 w-full rounded-full bg-slate-100" />
                        <div className="mt-3 h-4 w-11/12 rounded-full bg-slate-100" />
                        <div className="mt-3 h-4 w-10/12 rounded-full bg-slate-100" />
                    </div>
                    <div className="animate-pulse rounded-[36px] border border-amber-100 bg-white px-8 py-10 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
                        <div className="h-52 rounded-[24px] bg-amber-50" />
                    </div>
                </div>
            </div>
        </div>
    )
}

function MarketplaceCmsRenderer({ page, language, copy }) {
    if (page.contentType === 'RichText') {
        return <MarketplaceRichText html={typeof page.content === 'string' ? page.content : ''} language={language} copy={copy} />
    }

    return <MarketplaceBlocks blocks={Array.isArray(page.content) ? page.content : []} language={language} copy={copy} />
}

function MarketplaceRichText({ html, language, copy }) {
    const sanitizedHtml = useMemo(() => sanitizeRichTextHtml(html), [html])

    if (!sanitizedHtml.trim()) {
        return <MarketplaceInvalidContent copy={copy} />
    }

    return (
        <article
            dir={language === 'ar' ? 'rtl' : 'ltr'}
            className={`prose prose-slate mx-auto max-w-4xl rounded-[36px] border border-amber-100 bg-white px-8 py-10 shadow-[0_30px_90px_-50px_rgba(120,53,15,0.35)] ${language === 'ar' ? 'text-right' : ''}`}
        >
            <div dangerouslySetInnerHTML={{ __html: sanitizedHtml }} />
        </article>
    )
}

function MarketplaceBlocks({ blocks, language, copy }) {
    if (!blocks.length) {
        return <MarketplaceInvalidContent copy={copy} />
    }

    return (
        <div className="space-y-6">
            {blocks.map((block, index) => (
                <MarketplaceBlock key={`${block.type}-${index}`} block={block} language={language} copy={copy} />
            ))}
        </div>
    )
}

function MarketplaceBlock({ block, language, copy }) {
    if (!block || typeof block !== 'object' || typeof block.type !== 'string') {
        return <MarketplaceInvalidContent block={block} copy={copy} />
    }

    switch (block.type) {
        case 'text':
            return <MarketplaceTextBlock block={block} language={language} />
        case 'image':
            return <MarketplaceImageBlock block={block} language={language} copy={copy} />
        case 'faq':
            return <MarketplaceFaqBlock block={block} language={language} copy={copy} />
        default:
            return (
                <section className="rounded-[30px] border border-dashed border-amber-200 bg-white px-6 py-8 text-sm text-slate-500 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
                    {copy.unsupportedBlock(block.type)}
                </section>
            )
    }
}

function MarketplaceTextBlock({ block, language }) {
    const alignment = resolveAlignment(block.data?.alignment, language)

    return (
        <section className="rounded-[30px] border border-amber-100 bg-white px-8 py-8 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
            <div className={`mx-auto max-w-3xl whitespace-pre-line text-base leading-8 text-slate-700 ${alignment}`}>
                {block.data?.text || ''}
            </div>
        </section>
    )
}

function MarketplaceImageBlock({ block, language, copy }) {
    const imageUrl = block.data?.imageUrl || ''
    if (!imageUrl) {
        return <MarketplaceInvalidContent copy={copy} />
    }

    return (
        <section className="overflow-hidden rounded-[34px] border border-amber-100 bg-white shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
            <img src={imageUrl} alt={block.data?.alt ?? copy.pageImageAlt} className="h-auto w-full object-cover" loading="lazy" />
            {block.data?.caption ? (
                <div className={`border-t border-amber-100 px-6 py-4 text-sm text-slate-500 ${language === 'ar' ? 'text-right' : ''}`}>
                    {block.data.caption}
                </div>
            ) : null}
        </section>
    )
}

function MarketplaceFaqBlock({ block, language, copy }) {
    const items = Array.isArray(block.data?.items) ? block.data.items : []
    if (!items.length) {
        return <MarketplaceInvalidContent copy={copy} />
    }

    return (
        <section className="rounded-[30px] border border-amber-100 bg-white px-8 py-8 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
            <div className="space-y-4">
                {items.map((item, index) => (
                    <details key={index} className="group rounded-2xl border border-amber-100 bg-amber-50/70 px-5 py-4">
                        <summary className={`cursor-pointer list-none text-base font-medium text-slate-900 ${language === 'ar' ? 'text-right' : ''}`}>
                            {item.question}
                        </summary>
                        <p className={`mt-3 whitespace-pre-line text-sm leading-7 text-slate-600 ${language === 'ar' ? 'text-right' : ''}`}>
                            {item.answer}
                        </p>
                    </details>
                ))}
            </div>
        </section>
    )
}

function MarketplaceInvalidContent({ copy }) {
    return (
        <section className="rounded-[30px] border border-dashed border-amber-200 bg-white px-8 py-12 text-center text-sm text-slate-500 shadow-[0_24px_70px_-52px_rgba(120,53,15,0.35)]">
            {copy.invalidContent}
        </section>
    )
}

function MarketplaceNotFound({ language, copy, title = null, message = null }) {
    return (
        <div
            dir={language === 'ar' ? 'rtl' : 'ltr'}
            className="min-h-screen bg-[radial-gradient(circle_at_top,_rgba(251,191,36,0.18),_transparent_38%),linear-gradient(180deg,#fff8eb_0%,#ffffff_55%)] px-6 py-16"
        >
            <div className="mx-auto max-w-3xl rounded-[40px] border border-amber-100 bg-white/95 px-8 py-14 text-center shadow-[0_30px_90px_-50px_rgba(120,53,15,0.35)]">
                <p className="text-xs font-semibold uppercase tracking-[0.24em] text-amber-700">{copy.badge}</p>
                <h1 className="mt-4 text-3xl font-semibold text-slate-950">{title || copy.notFoundTitle}</h1>
                <p className="mt-3 text-sm leading-6 text-slate-500">{message || copy.notFoundMessage}</p>
                <Link
                    to="/super-admin/login"
                    className="mt-8 inline-flex rounded-full bg-slate-950 px-5 py-3 text-sm font-medium text-white transition hover:bg-slate-800"
                >
                    {copy.goToAdmin}
                </Link>
            </div>
        </div>
    )
}

function resolveAlignment(alignment, language) {
    if (alignment === 'center') {
        return 'text-center'
    }

    if (language === 'ar') {
        return alignment === 'right' ? 'text-left' : 'text-right'
    }

    return alignment === 'right' ? 'text-right' : 'text-left'
}

function applySeo(page, preview, language, slug) {
    const origin = window.location.origin
    document.title = page.seo?.metaTitle || page.title
    setMeta('description', page.seo?.metaDescription || '')
    setMeta('robots', preview ? 'noindex, nofollow' : 'index, follow')
    setMetaProperty('og:title', page.seo?.metaTitle || page.title)
    setMetaProperty('og:description', page.seo?.metaDescription || '')
    setMetaProperty('og:image', page.seo?.openGraphImageUrl || '')
    setLink('canonical', `${origin}/${language}/${slug}`)
    setAlternateLink('en', `${origin}/en/${slug}`)
    setAlternateLink('ar', `${origin}/ar/${slug}`)
    setAlternateLink('x-default', `${origin}/en/${slug}`)
}

function applyFallbackSeo(title, description, language, slug) {
    const origin = window.location.origin
    document.title = title
    setMeta('description', description)
    setMeta('robots', 'noindex, nofollow')
    setMetaProperty('og:title', title)
    setMetaProperty('og:description', description)
    setMetaProperty('og:image', '')
    setLink('canonical', `${origin}/${language}/${slug}`)
    setAlternateLink('en', `${origin}/en/${slug}`)
    setAlternateLink('ar', `${origin}/ar/${slug}`)
    setAlternateLink('x-default', `${origin}/en/${slug}`)
}

function setMeta(name, content) {
    let element = document.querySelector(`meta[name="${name}"]`)
    if (!element) {
        element = document.createElement('meta')
        element.setAttribute('name', name)
        document.head.appendChild(element)
    }

    element.setAttribute('content', content)
}

function setMetaProperty(property, content) {
    let element = document.querySelector(`meta[property="${property}"]`)
    if (!element) {
        element = document.createElement('meta')
        element.setAttribute('property', property)
        document.head.appendChild(element)
    }

    element.setAttribute('content', content)
}

function setLink(rel, href) {
    let element = document.querySelector(`link[rel="${rel}"]`)
    if (!element) {
        element = document.createElement('link')
        element.setAttribute('rel', rel)
        document.head.appendChild(element)
    }

    element.setAttribute('href', href)
}

function setAlternateLink(hreflang, href) {
    let element = document.querySelector(`link[rel="alternate"][hreflang="${hreflang}"]`)
    if (!element) {
        element = document.createElement('link')
        element.setAttribute('rel', 'alternate')
        element.setAttribute('hreflang', hreflang)
        document.head.appendChild(element)
    }

    element.setAttribute('href', href)
}

export default MarketplaceCmsPublicPage
