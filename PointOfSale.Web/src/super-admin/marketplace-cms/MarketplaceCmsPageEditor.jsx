import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Eye, Globe, Loader2, Plus, RefreshCcw, RotateCcw, Save, Sparkles, Trash2 } from 'lucide-react'
import { marketplaceCmsService } from './marketplaceCmsService'
import { sanitizeRichTextHtml } from '@/utils/htmlSanitizer'
import { getAuthoringErrorFeedback } from '../../modules/storefront/authoringPlatform'

const CONTENT_TYPES = { RichText: 'RichText', Blocks: 'Blocks' }
const SUPPORTED_LANGUAGES = ['en', 'ar']
const EMPTY_LOCALIZED_FIELD = { en: '', ar: '' }

function createEmptyForm() {
    return {
        slug: '',
        titleLocalized: { ...EMPTY_LOCALIZED_FIELD },
        contentType: CONTENT_TYPES.RichText,
        localizedContent: {
            en: '',
            ar: '',
        },
        seo: {
            metaTitleLocalized: { ...EMPTY_LOCALIZED_FIELD },
            metaDescriptionLocalized: { ...EMPTY_LOCALIZED_FIELD },
            openGraphImageUrl: '',
        },
    }
}

function createBlock(type) {
    if (type === 'image') {
        return { type, data: { imageUrl: '', alt: '', caption: '' } }
    }

    if (type === 'faq') {
        return { type, data: { items: [{ question: '', answer: '' }] } }
    }

    return { type: 'text', data: { text: '', alignment: 'left' } }
}

export function MarketplaceCmsPageEditor() {
    const { pageId } = useParams()
    const navigate = useNavigate()
    const isNewPage = pageId === 'new'

    const [page, setPage] = useState(null)
    const [form, setForm] = useState(createEmptyForm())
    const [acceptedFingerprint, setAcceptedFingerprint] = useState(() => getMarketplaceFormFingerprint(createEmptyForm()))
    const [isLoading, setIsLoading] = useState(!isNewPage)
    const [isSaving, setIsSaving] = useState(false)
    const [isPublishing, setIsPublishing] = useState(false)
    const [activeLanguage, setActiveLanguage] = useState('en')
    const [translatingField, setTranslatingField] = useState('')
    const [error, setError] = useState('')
    const [notice, setNotice] = useState('')

    const publicBaseUrl = useMemo(
        () => import.meta.env.VITE_MARKETPLACE_PUBLIC_URL || window.location.origin,
        [],
    )
    const currentFingerprint = useMemo(() => getMarketplaceFormFingerprint(form), [form])
    const isDirty = currentFingerprint !== acceptedFingerprint

    useEffect(() => {
        if (!isDirty) return undefined
        const handleBeforeUnload = (event) => {
            event.preventDefault()
            event.returnValue = ''
        }
        window.addEventListener('beforeunload', handleBeforeUnload)
        return () => window.removeEventListener('beforeunload', handleBeforeUnload)
    }, [isDirty])

    useEffect(() => {
        if (isNewPage) {
            setPage(null)
            const emptyForm = createEmptyForm()
            setForm(emptyForm)
            setAcceptedFingerprint(getMarketplaceFormFingerprint(emptyForm))
            setIsLoading(false)
            return
        }

        let cancelled = false

        async function loadPage() {
            setIsLoading(true)
            setError('')

            try {
                const data = await marketplaceCmsService.getPage(pageId)
                if (!cancelled) {
                    adoptMarketplaceResponse(data)
                }
            } catch (err) {
                if (!cancelled) {
                    console.error('Failed to load marketplace CMS page', err)
                    setError(err?.message || 'Unable to load marketplace page.')
                }
            } finally {
                if (!cancelled) {
                    setIsLoading(false)
                }
            }
        }

        loadPage()

        return () => {
            cancelled = true
        }
    }, [isNewPage, pageId])

    function adoptMarketplaceResponse(response) {
        const acceptedForm = fromPageResponse(response)
        setPage(response)
        setForm(acceptedForm)
        setAcceptedFingerprint(getMarketplaceFormFingerprint(acceptedForm))
        return response
    }

    function patchForm(patch) {
        setForm((current) => ({ ...current, ...patch }))
    }

    function patchLocalizedField(fieldKey, value) {
        setForm((current) => ({
            ...current,
            [fieldKey]: {
                ...(current[fieldKey] || EMPTY_LOCALIZED_FIELD),
                [activeLanguage]: value,
            },
        }))
    }

    function patchSeo(fieldKey, value) {
        setForm((current) => ({
            ...current,
            seo: {
                ...current.seo,
                [fieldKey]: {
                    ...(current.seo[fieldKey] || EMPTY_LOCALIZED_FIELD),
                    [activeLanguage]: value,
                },
            },
        }))
    }

    function patchLocalizedContent(value) {
        setForm((current) => ({
            ...current,
            localizedContent: {
                ...current.localizedContent,
                [activeLanguage]: value,
            },
        }))
    }

    async function handleSave() {
        if (!form.slug.trim() || !form.titleLocalized.en.trim()) {
            setError('English title and slug are required.')
            return null
        }
        if (!isNewPage && !isDirty) return page

        setIsSaving(true)
        setError('')
        setNotice('')

        try {
            const payload = toRequestPayload(form)
            const saved = isNewPage
                ? await marketplaceCmsService.createPage(payload)
                : await marketplaceCmsService.updatePage(
                    pageId,
                    payload,
                    marketplaceCmsService.getPageVersion(page),
                )

            adoptMarketplaceResponse(saved)
            setNotice('Draft saved and backend response adopted.')

            if (isNewPage) {
                navigate(`/super-admin/marketplace/pages/${saved.id}`, { replace: true })
            }

            return saved
        } catch (err) {
            console.error('Failed to save marketplace CMS page', err)
            if (marketplaceCmsService.isConcurrencyConflict(err)) {
                const refreshed = await refreshPageAfterConflict()
                setError(refreshed
                    ? `${err.message} The latest page version has been loaded.`
                    : `${err.message} The latest page version could not be loaded.`)
            } else {
                const feedback = getAuthoringErrorFeedback(err)
                setError(`${feedback.title}: ${feedback.message}`)
            }
            return null
        } finally {
            setIsSaving(false)
        }
    }

    async function handlePublish() {
        const target = (isDirty || !page) ? await handleSave() : page
        if (!target) {
            return
        }

        if (!window.confirm('Publish this page to the marketplace domain now?')) {
            return
        }

        setIsPublishing(true)
        try {
            const published = await marketplaceCmsService.publishPage(
                target.id,
                marketplaceCmsService.getPageVersion(target),
            )
            adoptMarketplaceResponse(published)
            setNotice('Marketplace page published successfully.')
        } catch (err) {
            console.error('Failed to publish marketplace CMS page', err)
            if (marketplaceCmsService.isConcurrencyConflict(err)) {
                await refreshPageAfterConflict()
            }
            const feedback = getAuthoringErrorFeedback(err)
            setError(`${feedback.title}: ${feedback.message}`)
        } finally {
            setIsPublishing(false)
        }
    }

    async function refreshPageAfterConflict() {
        try {
            const latest = await marketplaceCmsService.getPage(pageId)
            setPage(latest)
            return true
        } catch (refreshError) {
            console.error('Failed to refresh marketplace CMS page after a version conflict', refreshError)
            setPage(null)
            return false
        }
    }

    async function handlePreview() {
        if (isNewPage) {
            setError('Save the page first so a preview draft exists.')
            return
        }
        if (isDirty) {
            setError('Save your changes before previewing. Preview never saves or changes versions.')
            return
        }

        try {
            const previewAccess = await marketplaceCmsService.previewPage(pageId)
            const previewUrl = `${publicBaseUrl}/${activeLanguage}/${previewAccess.slug}?preview=true&previewToken=${encodeURIComponent(previewAccess.previewToken)}`
            window.open(previewUrl, '_blank', 'noopener,noreferrer')
        } catch (err) {
            console.error('Failed to create marketplace preview link', err)
            const feedback = getAuthoringErrorFeedback(err)
            setError(`${feedback.title}: ${feedback.message}`)
        }
    }

    async function handleAutoTranslate(fieldKey) {
        if (activeLanguage !== 'ar') {
            return
        }

        const translationTarget = getTranslationTarget(form, fieldKey, setForm)
        if (!translationTarget) {
            return
        }

        if (!hasTranslatableContent(translationTarget.source)) {
            alert('Add English content first, then translate it into Arabic.')
            return
        }

        if (hasTranslatableContent(translationTarget.target) && !window.confirm('Arabic content already exists. Overwrite it with a fresh AI translation?')) {
            return
        }

        setTranslatingField(fieldKey)
        try {
            const translated = await marketplaceCmsService.translateContent(translationTarget.source, 'en', 'ar')
            translationTarget.apply(translated)
        } catch (err) {
            console.error('Marketplace translation failed', err)
            alert(err?.message || 'Unable to translate this field right now.')
        } finally {
            setTranslatingField('')
        }
    }

    function addBlock(type) {
        setForm((current) => ({
            ...current,
            contentType: CONTENT_TYPES.Blocks,
            localizedContent: {
                ...current.localizedContent,
                [activeLanguage]: [...ensureBlocks(current.localizedContent?.[activeLanguage]), createBlock(type)],
            },
        }))
    }

    function updateBlock(index, nextBlock) {
        setForm((current) => ({
            ...current,
            localizedContent: {
                ...current.localizedContent,
                [activeLanguage]: ensureBlocks(current.localizedContent?.[activeLanguage]).map((block, blockIndex) =>
                    blockIndex === index ? nextBlock : block,
                ),
            },
        }))
    }

    function removeBlock(index) {
        if (!window.confirm('Delete this content block?')) return
        setForm((current) => ({
            ...current,
            localizedContent: {
                ...current.localizedContent,
                [activeLanguage]: ensureBlocks(current.localizedContent?.[activeLanguage]).filter((_, blockIndex) => blockIndex !== index),
            },
        }))
    }

    function handleCancel() {
        if (!isDirty || window.confirm('Discard all unsaved marketplace page changes?')) {
            const acceptedForm = page ? fromPageResponse(page) : createEmptyForm()
            setForm(acceptedForm)
            setAcceptedFingerprint(getMarketplaceFormFingerprint(acceptedForm))
            setError('')
            setNotice('')
        }
    }

    async function handleRefresh() {
        if (isDirty && !window.confirm('Discard unsaved changes and load the latest marketplace page?')) return
        if (isNewPage) {
            handleCancel()
            return
        }
        setIsLoading(true)
        setError('')
        try {
            adoptMarketplaceResponse(await marketplaceCmsService.getPage(pageId))
        } catch (err) {
            const feedback = getAuthoringErrorFeedback(err)
            setError(`${feedback.title}: ${feedback.message}`)
        } finally {
            setIsLoading(false)
        }
    }

    if (isLoading) {
        return (
            <div className="flex min-h-[320px] items-center justify-center rounded-[28px] border border-slate-200 bg-white">
                <Loader2 size={32} className="animate-spin text-slate-500" />
            </div>
        )
    }

    if (!isNewPage && !page && !isDirty) {
        return (
            <div className="rounded-[28px] border border-rose-200 bg-white p-8 shadow-sm">
                <h1 className="text-2xl font-semibold text-slate-900">Marketplace page editor unavailable</h1>
                <p className="mt-2 text-sm text-rose-700">{error || 'No authoritative page response was loaded.'}</p>
                <button type="button" onClick={handleRefresh} className="mt-5 inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2 text-sm font-medium text-white">
                    <RefreshCcw size={16} /> Retry loading
                </button>
            </div>
        )
    }

    return (
        <div className="space-y-6">
            <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
                <div className="flex items-start gap-4">
                    <button
                        onClick={() => {
                            if (!isDirty || window.confirm('Leave this page and discard unsaved changes?')) {
                                navigate('/super-admin/marketplace/pages')
                            }
                        }}
                        className="rounded-2xl border border-slate-200 bg-white p-3 text-slate-600 transition hover:border-slate-300 hover:bg-slate-50"
                    >
                        <ArrowLeft size={18} />
                    </button>
                    <div>
                        <p className="text-sm font-semibold uppercase tracking-[0.2em] text-sky-600">Marketplace Editor</p>
                        <h1 className="mt-2 text-3xl font-semibold text-slate-900">
                            {isNewPage ? 'Create Marketplace Page' : getLocalizedValue(form.titleLocalized, activeLanguage) || form.titleLocalized.en || 'Edit Marketplace Page'}
                        </h1>
                        <p className="mt-2 text-sm text-slate-500">
                            {page?.publishedAtUtc
                                ? `Last published: ${new Date(page.publishedAtUtc).toLocaleString()}`
                                : 'This page is still in draft mode.'}
                        </p>
                    </div>
                </div>

                <div className="flex flex-wrap gap-3">
                    <span className={`inline-flex items-center rounded-xl px-3 py-2 text-sm font-medium ${isDirty ? 'bg-amber-100 text-amber-900' : 'bg-emerald-50 text-emerald-800'}`}>
                        {isDirty ? 'Unsaved changes' : 'Draft in sync'}
                    </span>
                    <ActionButton onClick={handleCancel} disabled={!isDirty || isSaving || isPublishing} icon={RotateCcw}>
                        Cancel
                    </ActionButton>
                    <ActionButton onClick={handleRefresh} disabled={isSaving || isPublishing} icon={RefreshCcw}>
                        Refresh
                    </ActionButton>
                    <ActionButton onClick={handlePreview} disabled={isNewPage} icon={Eye}>
                        Preview
                    </ActionButton>
                    <ActionButton onClick={handleSave} disabled={isSaving || isPublishing || !isDirty} icon={isSaving ? Loader2 : Save} spinning={isSaving}>
                        Save Draft
                    </ActionButton>
                    <ActionButton onClick={handlePublish} disabled={isPublishing || isSaving} icon={isPublishing ? Loader2 : Globe} primary spinning={isPublishing}>
                        Publish
                    </ActionButton>
                </div>
            </div>

            <LanguageTabs value={activeLanguage} onChange={setActiveLanguage} />

            {error ? (
                <div className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
                    {error}
                </div>
            ) : null}

            {notice ? (
                <div className="rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
                    {notice}
                </div>
            ) : null}

            <div className="grid gap-6 xl:grid-cols-[340px_minmax(0,1fr)]">
                <div className="space-y-6">
                    <Panel title="Page Details">
                        <TranslatableFieldHeader
                            label="Title"
                            activeLanguage={activeLanguage}
                            isTranslating={translatingField === 'title'}
                            onTranslate={() => handleAutoTranslate('title')}
                        />
                        <LabeledInput
                            label={null}
                            value={getLocalizedValue(form.titleLocalized, activeLanguage)}
                            onChange={(value) => patchLocalizedField('titleLocalized', value)}
                        />
                        <LabeledInput
                            label="Slug"
                            value={form.slug}
                            onChange={(value) => patchForm({ slug: normalizeSlugInput(value) })}
                            helpText="This resolves at the marketplace root, for example /about"
                        />
                        <LabeledSelect
                            label="Content Mode"
                            value={form.contentType}
                            options={[CONTENT_TYPES.RichText, CONTENT_TYPES.Blocks]}
                            onChange={(value) => patchForm({
                                contentType: value,
                                localizedContent: {
                                    en: value === CONTENT_TYPES.Blocks
                                        ? ensureBlocks(form.localizedContent.en)
                                        : ensureRichTextRaw(form.localizedContent.en),
                                    ar: value === CONTENT_TYPES.Blocks
                                        ? ensureBlocks(form.localizedContent.ar)
                                        : ensureRichTextRaw(form.localizedContent.ar),
                                },
                            })}
                        />
                    </Panel>

                    <Panel title="SEO Settings">
                        <TranslatableFieldHeader
                            label="Meta title"
                            activeLanguage={activeLanguage}
                            isTranslating={translatingField === 'metaTitle'}
                            onTranslate={() => handleAutoTranslate('metaTitle')}
                        />
                        <LabeledInput
                            label={null}
                            value={getLocalizedValue(form.seo.metaTitleLocalized, activeLanguage)}
                            onChange={(value) => patchSeo('metaTitleLocalized', value)}
                        />
                        <TranslatableFieldHeader
                            label="Meta description"
                            activeLanguage={activeLanguage}
                            isTranslating={translatingField === 'metaDescription'}
                            onTranslate={() => handleAutoTranslate('metaDescription')}
                        />
                        <LabeledTextarea
                            label={null}
                            value={getLocalizedValue(form.seo.metaDescriptionLocalized, activeLanguage)}
                            onChange={(value) => patchSeo('metaDescriptionLocalized', value)}
                            rows={4}
                        />
                        <LabeledInput
                            label="Open Graph image URL"
                            value={form.seo.openGraphImageUrl}
                            onChange={(value) => setForm((current) => ({
                                ...current,
                                seo: {
                                    ...current.seo,
                                    openGraphImageUrl: value,
                                },
                            }))}
                        />
                    </Panel>
                </div>

                <Panel title="Content">
                    <p className="mb-5 text-sm text-slate-500">
                        {form.contentType === CONTENT_TYPES.RichText
                            ? 'Use the rich text editor and preview the result before publishing.'
                            : 'Build the page from blocks so it stays ready for a future marketplace page builder.'}
                    </p>

                    <div className="mb-5 flex items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                        <span className="text-sm font-medium text-slate-700">
                            Editing {activeLanguage === 'ar' ? 'Arabic' : 'English'} content
                        </span>
                        <button
                            type="button"
                            onClick={() => handleAutoTranslate('content')}
                            disabled={activeLanguage !== 'ar' || translatingField === 'content'}
                            className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-50"
                        >
                            {translatingField === 'content' ? <Loader2 size={14} className="animate-spin" /> : <Sparkles size={14} />}
                            Auto Translate
                        </button>
                    </div>

                    {form.contentType === CONTENT_TYPES.Blocks ? (
                        <div className="mb-5 flex flex-wrap gap-2">
                            {['text', 'image', 'faq'].map((type) => (
                                <button
                                    key={type}
                                    type="button"
                                    onClick={() => addBlock(type)}
                                    className="inline-flex items-center gap-2 rounded-xl border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                >
                                    <Plus size={14} />
                                    {type}
                                </button>
                            ))}
                        </div>
                    ) : null}

                    {form.contentType === CONTENT_TYPES.RichText ? (
                        <RichTextEditor value={ensureRichText(form.localizedContent[activeLanguage])} onChange={patchLocalizedContent} />
                    ) : (
                        <BlocksEditor
                            blocks={ensureBlocks(form.localizedContent[activeLanguage])}
                            onUpdate={updateBlock}
                            onRemove={removeBlock}
                        />
                    )}
                </Panel>
            </div>
        </div>
    )
}

function fromPageResponse(page) {
    const workingVersion = page?.draft || page?.published

    if (!workingVersion) {
        return createEmptyForm()
    }

    return {
        slug: workingVersion.slug || '',
        titleLocalized: normalizeLocalizedField(workingVersion.titleLocalized, workingVersion.title),
        contentType: workingVersion.contentType || CONTENT_TYPES.RichText,
        localizedContent: normalizeLocalizedContent(
            workingVersion.contentType || CONTENT_TYPES.RichText,
            workingVersion.localizedContent,
            workingVersion.content,
        ),
        seo: {
            metaTitleLocalized: normalizeLocalizedField(
                workingVersion.seo?.metaTitleLocalized,
                workingVersion.seo?.metaTitle,
            ),
            metaDescriptionLocalized: normalizeLocalizedField(
                workingVersion.seo?.metaDescriptionLocalized,
                workingVersion.seo?.metaDescription,
            ),
            openGraphImageUrl: workingVersion.seo?.openGraphImageUrl || '',
        },
    }
}

function toRequestPayload(form) {
    const englishContent = normalizeContentPayload(form.contentType, form.localizedContent.en)
    const arabicContent = normalizeContentPayload(form.contentType, form.localizedContent.ar)

    return {
        slug: form.slug.trim(),
        title: form.titleLocalized.en.trim(),
        titleLocalized: trimLocalizedField(form.titleLocalized),
        contentType: form.contentType,
        content: englishContent,
        localizedContent: {
            en: englishContent,
            ...(hasTranslatableContent(arabicContent) ? { ar: arabicContent } : {}),
        },
        seo: {
            metaTitle: emptyToNull(form.seo.metaTitleLocalized.en),
            metaTitleLocalized: trimLocalizedField(form.seo.metaTitleLocalized),
            metaDescription: emptyToNull(form.seo.metaDescriptionLocalized.en),
            metaDescriptionLocalized: trimLocalizedField(form.seo.metaDescriptionLocalized),
            openGraphImageUrl: emptyToNull(form.seo.openGraphImageUrl),
        },
    }
}

export function getMarketplaceFormFingerprint(form) {
    return JSON.stringify(toRequestPayload(form))
}

function emptyToNull(value) {
    return value && value.trim() ? value.trim() : null
}

function normalizeSlugInput(value) {
    return value.toLowerCase().replace(/[^a-z0-9-]+/g, '-').replace(/--+/g, '-').replace(/^-|-$/g, '')
}

function normalizeLocalizedField(localizedField, fallbackValue = '') {
    return {
        en: localizedField?.en || fallbackValue || '',
        ar: localizedField?.ar || '',
    }
}

function normalizeLocalizedContent(contentType, localizedContent, fallbackContent) {
    return {
        en: normalizeContentValue(contentType, localizedContent?.en ?? fallbackContent),
        ar: normalizeContentValue(contentType, localizedContent?.ar),
    }
}

function normalizeContentValue(contentType, value) {
    if (contentType === CONTENT_TYPES.Blocks) {
        return ensureBlocks(value)
    }

    return ensureRichTextRaw(value)
}

function normalizeContentPayload(contentType, value) {
    return contentType === CONTENT_TYPES.Blocks
        ? ensureBlocks(value)
        : ensureRichText(value)
}

function ensureBlocks(value) {
    return Array.isArray(value) ? value : []
}

function ensureRichTextRaw(value) {
    return typeof value === 'string' ? value : ''
}

function ensureRichText(value) {
    return sanitizeRichTextHtml(ensureRichTextRaw(value))
}

function trimLocalizedField(field) {
    const trimmed = {
        en: (field?.en || '').trim(),
        ar: (field?.ar || '').trim(),
    }

    return trimmed.en || trimmed.ar ? trimmed : null
}

function getLocalizedValue(field, language) {
    return field?.[language] || ''
}

function hasTranslatableContent(value) {
    if (typeof value === 'string') {
        return value.trim().length > 0
    }

    if (Array.isArray(value)) {
        return value.length > 0
    }

    if (value && typeof value === 'object') {
        return Object.keys(value).length > 0
    }

    return false
}

function getTranslationTarget(form, fieldKey, setForm) {
    switch (fieldKey) {
        case 'title':
            return {
                source: form.titleLocalized.en,
                target: form.titleLocalized.ar,
                apply: (translated) => setForm((current) => ({
                    ...current,
                    titleLocalized: {
                        ...current.titleLocalized,
                        ar: translated,
                    },
                })),
            }
        case 'metaTitle':
            return {
                source: form.seo.metaTitleLocalized.en,
                target: form.seo.metaTitleLocalized.ar,
                apply: (translated) => setForm((current) => ({
                    ...current,
                    seo: {
                        ...current.seo,
                        metaTitleLocalized: {
                            ...current.seo.metaTitleLocalized,
                            ar: translated,
                        },
                    },
                })),
            }
        case 'metaDescription':
            return {
                source: form.seo.metaDescriptionLocalized.en,
                target: form.seo.metaDescriptionLocalized.ar,
                apply: (translated) => setForm((current) => ({
                    ...current,
                    seo: {
                        ...current.seo,
                        metaDescriptionLocalized: {
                            ...current.seo.metaDescriptionLocalized,
                            ar: translated,
                        },
                    },
                })),
            }
        case 'content':
            return {
                source: form.localizedContent.en,
                target: form.localizedContent.ar,
                apply: (translated) => setForm((current) => ({
                    ...current,
                    localizedContent: {
                        ...current.localizedContent,
                        ar: translated,
                    },
                })),
            }
        default:
            return null
    }
}

function ActionButton({ children, disabled, icon: Icon, onClick, primary = false, spinning = false }) {
    return (
        <button
            type="button"
            onClick={onClick}
            disabled={disabled}
            className={[
                'inline-flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-medium transition disabled:opacity-50',
                primary
                    ? 'bg-emerald-600 text-white hover:bg-emerald-500'
                    : 'border border-slate-200 bg-white text-slate-700 hover:border-slate-300 hover:bg-slate-50',
            ].join(' ')}
        >
            <Icon size={16} className={spinning ? 'animate-spin' : ''} />
            {children}
        </button>
    )
}

function LanguageTabs({ value, onChange }) {
    return (
        <div className="inline-flex rounded-2xl border border-slate-200 bg-white p-1 shadow-sm">
            {SUPPORTED_LANGUAGES.map((language) => (
                <button
                    key={language}
                    type="button"
                    onClick={() => onChange(language)}
                    className={[
                        'rounded-xl px-4 py-2 text-sm font-semibold transition',
                        value === language
                            ? 'bg-slate-900 text-white'
                            : 'text-slate-600 hover:bg-slate-50',
                    ].join(' ')}
                >
                    {language.toUpperCase()}
                </button>
            ))}
        </div>
    )
}

function TranslatableFieldHeader({ label, activeLanguage, isTranslating, onTranslate }) {
    return (
        <div className="flex items-center justify-between gap-3">
            <span className="text-sm font-semibold text-slate-700">
                {label} ({activeLanguage.toUpperCase()})
            </span>
            <button
                type="button"
                onClick={onTranslate}
                disabled={activeLanguage !== 'ar' || isTranslating}
                className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 py-2 text-xs font-semibold uppercase tracking-[0.18em] text-slate-600 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-50"
            >
                {isTranslating ? <Loader2 size={14} className="animate-spin" /> : <Sparkles size={14} />}
                Auto Translate
            </button>
        </div>
    )
}

function Panel({ children, title }) {
    return (
        <section className="rounded-[28px] border border-slate-200 bg-white p-6 shadow-sm">
            <h2 className="text-lg font-semibold text-slate-900">{title}</h2>
            <div className="mt-5 space-y-4">{children}</div>
        </section>
    )
}

function LabeledInput({ label, value, onChange, helpText }) {
    return (
        <label className="block">
            {label ? <span className="mb-2 block text-sm font-medium text-slate-700">{label}</span> : null}
            <input
                value={value}
                onChange={(event) => onChange(event.target.value)}
                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-700 outline-none transition focus:border-sky-400 focus:ring-4 focus:ring-sky-100"
            />
            {helpText ? <span className="mt-2 block text-xs text-slate-400">{helpText}</span> : null}
        </label>
    )
}

function LabeledTextarea({ label, value, onChange, rows = 4 }) {
    return (
        <label className="block">
            {label ? <span className="mb-2 block text-sm font-medium text-slate-700">{label}</span> : null}
            <textarea
                value={value}
                onChange={(event) => onChange(event.target.value)}
                rows={rows}
                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-700 outline-none transition focus:border-sky-400 focus:ring-4 focus:ring-sky-100"
            />
        </label>
    )
}

function LabeledSelect({ label, value, options, onChange }) {
    return (
        <label className="block">
            <span className="mb-2 block text-sm font-medium text-slate-700">{label}</span>
            <select
                value={value}
                onChange={(event) => onChange(event.target.value)}
                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-700 outline-none transition focus:border-sky-400 focus:ring-4 focus:ring-sky-100"
            >
                {options.map((option) => (
                    <option key={option} value={option}>{option}</option>
                ))}
            </select>
        </label>
    )
}

function RichTextEditor({ value, onChange }) {
    const editorRef = useRef(null)
    const isEditingRef = useRef(false)
    const sanitizedValue = useMemo(() => ensureRichText(value), [value])

    useEffect(() => {
        // Only sync from props when not actively editing to avoid overwriting mid-edit
        if (editorRef.current && editorRef.current.innerHTML !== sanitizedValue && !isEditingRef.current) {
            editorRef.current.innerHTML = sanitizedValue
        }
    }, [sanitizedValue])

    function runCommand(command, commandValue) {
        if (!editorRef.current) {
            return
        }

        editorRef.current.focus()
        document.execCommand(command, false, commandValue)
        onChange(ensureRichText(editorRef.current.innerHTML))
    }

    function handleOnBlur() {
        if (!editorRef.current) {
            return
        }

        isEditingRef.current = false

        const raw = editorRef.current.innerHTML
        const sanitizedNextValue = ensureRichText(raw)
        
        // Normalize the editor DOM if sanitization changed the content
        if (editorRef.current.innerHTML !== sanitizedNextValue) {
            editorRef.current.innerHTML = sanitizedNextValue
        }
        
        // Trigger onChange whenever the raw DOM content differs from stored value
        // OR when sanitization changed the content (nextValue !== value)
        // This ensures form state is updated even if sanitization normalizes the content
        if (raw !== sanitizedValue || sanitizedNextValue !== sanitizedValue) {
            onChange(sanitizedNextValue)
        }
    }

    return (
        <div className="overflow-hidden rounded-[24px] border border-slate-200">
            <div className="flex flex-wrap gap-2 border-b border-slate-200 bg-slate-50 p-3">
                {['bold', 'italic', 'insertUnorderedList'].map((command) => (
                    <button
                        key={command}
                        type="button"
                        onClick={() => runCommand(command)}
                        className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                    >
                        {command === 'bold' ? 'B' : command === 'italic' ? 'I' : 'UL'}
                    </button>
                ))}
                <button
                    type="button"
                    onClick={() => runCommand('formatBlock', '<h2>')}
                    className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                >
                    H2
                </button>
            </div>
            <div
                ref={editorRef}
                contentEditable
                suppressContentEditableWarning
                onFocus={() => {
                    isEditingRef.current = true
                }}
                onInput={() => {
                    // Input is handled, but sanitization is deferred to onBlur to avoid lag
                    // This keeps the editor responsive while typing
                }}
                onBlur={handleOnBlur}
                className="min-h-[360px] px-5 py-4 text-sm leading-7 text-slate-700 outline-none"
            />
        </div>
    )
}

function BlocksEditor({ blocks, onUpdate, onRemove }) {
    if (!blocks.length) {
        return (
            <div className="rounded-[24px] border border-dashed border-slate-300 bg-slate-50 px-6 py-16 text-center text-sm text-slate-500">
                Add your first block to start building the page.
            </div>
        )
    }

    return (
        <div className="space-y-4">
            {blocks.map((block, index) => (
                <div key={`${block.type}-${index}`} className="rounded-[24px] border border-slate-200 p-4">
                    <div className="mb-4 flex items-center justify-between">
                        <span className="text-sm font-semibold capitalize text-slate-800">{block.type}</span>
                        <button
                            type="button"
                            onClick={() => onRemove(index)}
                            className="rounded-xl border border-rose-200 bg-white px-3 py-2 text-xs font-medium text-rose-700 transition hover:bg-rose-50"
                        >
                            <Trash2 size={14} />
                        </button>
                    </div>

                    {block.type === 'text' ? (
                        <div className="space-y-3">
                            <LabeledTextarea
                                label="Text"
                                value={block.data?.text || ''}
                                onChange={(value) => onUpdate(index, { ...block, data: { ...block.data, text: value } })}
                                rows={5}
                            />
                            <LabeledSelect
                                label="Alignment"
                                value={block.data?.alignment || 'left'}
                                options={['left', 'center', 'right']}
                                onChange={(value) => onUpdate(index, { ...block, data: { ...block.data, alignment: value } })}
                            />
                        </div>
                    ) : null}

                    {block.type === 'image' ? (
                        <div className="space-y-3">
                            <LabeledInput
                                label="Image URL"
                                value={block.data?.imageUrl || ''}
                                onChange={(value) => onUpdate(index, { ...block, data: { ...block.data, imageUrl: value } })}
                            />
                            <LabeledInput
                                label="Alt text"
                                value={block.data?.alt || ''}
                                onChange={(value) => onUpdate(index, { ...block, data: { ...block.data, alt: value } })}
                            />
                            <LabeledInput
                                label="Caption"
                                value={block.data?.caption || ''}
                                onChange={(value) => onUpdate(index, { ...block, data: { ...block.data, caption: value } })}
                            />
                        </div>
                    ) : null}

                    {block.type === 'faq' ? (
                        <LabeledTextarea
                            label="FAQ items JSON"
                            value={JSON.stringify(block.data?.items || [], null, 2)}
                            onChange={(value) => {
                                try {
                                    const items = JSON.parse(value)
                                    onUpdate(index, { ...block, data: { ...block.data, items } })
                                } catch {
                                }
                            }}
                            rows={8}
                        />
                    ) : null}
                </div>
            ))}
        </div>
    )
}

export default MarketplaceCmsPageEditor
