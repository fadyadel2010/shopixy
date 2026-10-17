import { useEffect, useMemo, useRef, useState } from 'react'
import { ChevronDown, ChevronUp, Loader2, Plus, RefreshCcw, Sparkles, Trash2 } from 'lucide-react'
import {
    admitFooterConfiguration,
    createFooterLink as createLink,
    createFooterSection as createSection,
    createSocialLink as createSocial,
    getFooterDraftFingerprint,
    mapFooterDraftToRequest,
    validateFooterDraft,
} from './footerAuthoringContract'
import { loadFooterConfiguration, saveFooterConfiguration } from './footerAuthoringService'
import {
    AuthoringActionBar,
    AuthoringConfirmDialog,
    AuthoringConflictDialog,
    AuthoringFeedback,
    AuthoringLoadingState,
    AuthoringStatusBadge,
    AuthoringValidationSummary,
    UnsavedChangesDialog,
    getAuthoringErrorFeedback,
    useAuthoringGuard,
} from '../../modules/storefront/authoringPlatform'

const LINK_TYPES = ['Internal', 'Cms', 'External']
const inputClassName = 'w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 outline-none focus:border-sky-500 focus:ring-2 focus:ring-sky-100'
const createLocalizedField = (fallback = '') => ({ en: fallback || '', ar: '' })

const moveItem = (items, index, direction) => {
    const nextIndex = direction === 'up' ? index - 1 : index + 1
    if (nextIndex < 0 || nextIndex >= items.length) {
        return items
    }

    const cloned = [...items]
    const [item] = cloned.splice(index, 1)
    cloned.splice(nextIndex, 0, item)
    return cloned
}

export function FooterConfigScreen({
    scope,
    title,
    description,
    note,
    localized = false,
    allowCmsSlugEntry = false,
    loadCmsPages,
    onTranslateContent,
}) {
    const [acceptedConfiguration, setAcceptedConfiguration] = useState(null)
    const [draft, setDraft] = useState(null)
    const [cmsPages, setCmsPages] = useState([])
    const [isLoading, setIsLoading] = useState(true)
    const [isSaving, setIsSaving] = useState(false)
    const [loadError, setLoadError] = useState(null)
    const [feedback, setFeedback] = useState(null)
    const [conflict, setConflict] = useState(null)
    const [isSaveConfirmOpen, setIsSaveConfirmOpen] = useState(false)
    const [isDiscardConfirmOpen, setIsDiscardConfirmOpen] = useState(false)
    const [pendingDelete, setPendingDelete] = useState(null)
    const [pendingTranslation, setPendingTranslation] = useState(null)
    const [activeLanguage, setActiveLanguage] = useState('en')
    const [translatingKey, setTranslatingKey] = useState('')
    const draftRef = useRef(draft)
    draftRef.current = draft
    const saveInFlightRef = useRef(false)

    const baseline = useMemo(
        () => acceptedConfiguration ? getFooterDraftFingerprint(acceptedConfiguration, localized) : '',
        [acceptedConfiguration, localized],
    )
    const current = useMemo(
        () => draft ? getFooterDraftFingerprint(draft, localized) : '',
        [draft, localized],
    )
    const isDirty = Boolean(draft && acceptedConfiguration && baseline !== current)
    const validationIssues = useMemo(
        () => draft ? validateFooterDraft(draft, { isLocalized: localized, cmsPages, allowCmsSlugEntry }) : [],
        [allowCmsSlugEntry, cmsPages, draft, localized],
    )

    const adoptFooterResponse = (response) => {
        const admitted = admitFooterConfiguration(response, scope)
        setAcceptedConfiguration(admitted)
        setDraft(admitted)
        setConflict(null)
        return admitted
    }

    const loadWorkspace = async () => {
        setIsLoading(true)
        setLoadError(null)
        setFeedback(null)
        try {
            const [configuration, pages] = await Promise.all([
                loadFooterConfiguration(scope),
                loadCmsPages ? loadCmsPages() : Promise.resolve([]),
            ])
            setCmsPages(Array.isArray(pages) ? pages : [])
            adoptFooterResponse(configuration)
            return true
        } catch (error) {
            setLoadError(getAuthoringErrorFeedback(error))
            return false
        } finally {
            setIsLoading(false)
        }
    }

    useEffect(() => {
        loadWorkspace()
        // The scope owns the workspace identity. Explicit Refresh owns later reloads.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [scope])

    const updateSection = (sectionKey, updater) =>
        setDraft((value) => ({
            ...value,
            sections: value.sections.map((section) => (section._key === sectionKey ? updater(section) : section)),
        }))

    const updateLink = (sectionKey, linkKey, updater) =>
        updateSection(sectionKey, (section) => ({
            ...section,
            links: section.links.map((link) => (link._key === linkKey ? updater(link) : link)),
        }))

    const cmsOptions = (cmsPages || []).slice().sort((left, right) => left.title.localeCompare(right.title))

    const performTranslation = async (key, source, apply) => {
        setTranslatingKey(key)
        try {
            const translated = await onTranslateContent(source, 'en', 'ar')
            apply(translated)
        } catch (saveFailure) {
            setFeedback(getAuthoringErrorFeedback(saveFailure))
        } finally {
            setTranslatingKey('')
        }
    }

    const updateLocalizedField = async (key, source, target, apply) => {
        if (!onTranslateContent || activeLanguage !== 'ar') {
            return
        }

        if (!source?.trim()) {
            setFeedback({ kind: 'warning', title: 'English content required', message: 'Add English content first, then translate it into Arabic.' })
            return
        }

        if (target?.trim()) {
            setPendingTranslation({ key, source, apply })
            return
        }

        await performTranslation(key, source, apply)
    }

    const handleSave = async (revision = acceptedConfiguration?.revision) => {
        if (saveInFlightRef.current || !draft || !revision || validationIssues.length > 0) return
        saveInFlightRef.current = true
        const submittedFingerprint = getFooterDraftFingerprint(draft, localized)
        setIsSaving(true)
        setFeedback(null)
        try {
            const request = mapFooterDraftToRequest(draft, revision, localized)
            const response = await saveFooterConfiguration(scope, request)
            const admitted = admitFooterConfiguration(response, scope)
            const hasNewerEdits = getFooterDraftFingerprint(draftRef.current, localized) !== submittedFingerprint
            setAcceptedConfiguration(admitted)
            if (!hasNewerEdits) setDraft(admitted)
            setConflict(null)
            setFeedback({
                kind: hasNewerEdits ? 'info' : 'success',
                title: 'Footer saved live',
                message: hasNewerEdits
                    ? 'The submitted Footer is live. Newer unsaved edits were retained.'
                    : 'The accepted backend response is now live and is the saved authoring baseline.',
            })
            setIsSaveConfirmOpen(false)
        } catch (saveFailure) {
            const failure = getAuthoringErrorFeedback(saveFailure)
            if (failure.kind === 'conflict') {
                setIsSaveConfirmOpen(false)
                try {
                    const latest = admitFooterConfiguration(await loadFooterConfiguration(scope), scope)
                    setConflict({ ...failure, latest })
                } catch {
                    setConflict({
                        ...failure,
                        message: `${failure.message} The latest revision could not be loaded, so retry is disabled.`,
                        latest: null,
                    })
                }
            } else {
                setFeedback(failure)
            }
        } finally {
            saveInFlightRef.current = false
            setIsSaving(false)
        }
    }

    const handleDiscard = () => {
        setDraft(acceptedConfiguration)
        setConflict(null)
        setFeedback({ kind: 'info', title: 'Changes discarded', message: 'The accepted live Footer has been restored.' })
        setIsDiscardConfirmOpen(false)
    }

    const handleLoadLatest = () => {
        if (!conflict?.latest) return
        adoptFooterResponse(conflict.latest)
        setFeedback({ kind: 'success', title: 'Latest revision loaded', message: 'The footer editor now matches the authoritative backend response.' })
    }

    const handleRetryAfterConflict = () => {
        if (!conflict?.latest) return
        handleSave(conflict.latest.revision)
    }

    const confirmDelete = (message, remove) => {
        setPendingDelete({ message, remove })
    }

    const { requestNavigation, unsavedDialogProps } = useAuthoringGuard({
        isDirty,
        isBusy: isSaving,
        onSave: () => setIsSaveConfirmOpen(true),
    })

    if (isLoading) {
        return <AuthoringLoadingState label="Loading Footer content…" />
    }

    if (!draft) {
        return (
            <div className="rounded-[24px] border border-rose-200 bg-white p-8 shadow-sm">
                <h1 className="text-2xl font-semibold text-slate-900">{title}</h1>
                <p className="mt-3 font-semibold text-rose-800">{loadError?.title || 'Footer authoring is unavailable'}</p>
                <p className="mt-1 text-sm text-rose-700">{loadError?.message || 'No editable fallback was created because the authoritative footer could not be loaded.'}</p>
                <button type="button" onClick={() => loadWorkspace()} className="mt-5 inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2 text-sm font-medium text-white">
                    <RefreshCcw size={16} /> Retry loading
                </button>
            </div>
        )
    }

    return (
        <div className="space-y-6">
            <AuthoringActionBar
                title={title}
                subtitle={`${description} Changes on this surface are committed live; there is no separate publish step.`}
                backAction={{ label: 'Back', onClick: () => requestNavigation('/storefront') }}
                status={<AuthoringStatusBadge state={isSaving ? 'saving' : isDirty ? 'dirty' : 'live'} />}
                discardAction={{ label: 'Discard', onClick: () => setIsDiscardConfirmOpen(true), disabled: !isDirty || isSaving }}
                publishAction={{ label: 'Save live Footer', busyLabel: 'Saving…', onClick: () => setIsSaveConfirmOpen(true), disabled: !isDirty || validationIssues.length > 0, busy: isSaving }}
            />

            <div className="rounded-[24px] border border-sky-100 bg-sky-50/80 px-5 py-4 text-sm text-sky-900">{note}</div>
            {feedback ? <AuthoringFeedback {...feedback} /> : null}
            {isDirty ? <AuthoringValidationSummary errors={validationIssues.map((item) => item.message)} title="Resolve Footer validation before saving live" /> : null}

            {localized ? (
                <div className="inline-flex rounded-2xl border border-slate-200 bg-white p-1 shadow-sm">
                    {['en', 'ar'].map((language) => (
                        <button
                            key={language}
                            type="button"
                            onClick={() => setActiveLanguage(language)}
                            className={[
                                'rounded-xl px-4 py-2 text-sm font-semibold transition',
                                activeLanguage === language
                                    ? 'bg-slate-900 text-white'
                                    : 'text-slate-600 hover:bg-slate-50',
                            ].join(' ')}
                        >
                            {language.toUpperCase()}
                        </button>
                    ))}
                </div>
            ) : null}

            <div className="rounded-[24px] border border-slate-200 bg-white p-6 shadow-sm">
                <div className="flex flex-col items-start justify-between gap-4 sm:flex-row sm:items-center">
                    <div>
                        <h2 className="text-lg font-semibold text-slate-900">Footer Sections</h2>
                        <p className="mt-1 text-sm text-slate-500">Create ordered sections and decide which links stay active.</p>
                    </div>
                    <button onClick={() => setDraft((value) => ({ ...value, sections: [...value.sections, createSection(localized)] }))} className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50">
                        <Plus size={16} />
                        Add Section
                    </button>
                </div>

                <div className="mt-6 space-y-4">
                    {draft.sections.length === 0 ? <div className="rounded-2xl border border-dashed border-slate-300 bg-slate-50 px-6 py-10 text-center text-sm text-slate-500">No footer sections configured yet.</div> : null}
                    {draft.sections.map((section, sectionIndex) => (
                        <div key={section._key} className="rounded-[20px] border border-slate-200 bg-slate-50/70 p-4">
                            <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_auto]">
                                <div className="grid gap-4 md:grid-cols-2">
                                    <div className="space-y-2">
                                        {localized ? (
                                            <div className="flex items-center justify-between gap-3">
                                                <span className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                    Section title ({activeLanguage.toUpperCase()})
                                                </span>
                                                <button
                                                    type="button"
                                                    onClick={() => updateLocalizedField(
                                                        `section-${section._key}-title`,
                                                        section.titleLocalized?.en || '',
                                                        section.titleLocalized?.ar || '',
                                                        (translated) => updateSection(section._key, (currentSection) => ({
                                                            ...currentSection,
                                                            titleLocalized: {
                                                                ...(currentSection.titleLocalized || createLocalizedField(currentSection.title)),
                                                                ar: translated,
                                                            },
                                                        })),
                                                    )}
                                                    disabled={!onTranslateContent || activeLanguage !== 'ar' || translatingKey === `section-${section._key}-title`}
                                                    className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 py-2 text-xs font-semibold uppercase tracking-[0.18em] text-slate-600 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-50"
                                                >
                                                    {translatingKey === `section-${section._key}-title` ? <Loader2 size={14} className="animate-spin" /> : <Sparkles size={14} />}
                                                    Auto Translate
                                                </button>
                                            </div>
                                        ) : null}
                                        <input
                                            aria-label={`Section title ${activeLanguage.toUpperCase()}`}
                                            dir={localized && activeLanguage === 'ar' ? 'rtl' : 'ltr'}
                                            value={localized ? (section.titleLocalized?.[activeLanguage] || '') : section.title}
                                            onChange={(event) => updateSection(section._key, (currentSection) => ({
                                                ...currentSection,
                                                ...(localized
                                                    ? {
                                                        titleLocalized: {
                                                            ...(currentSection.titleLocalized || createLocalizedField(currentSection.title)),
                                                            [activeLanguage]: event.target.value,
                                                        },
                                                    }
                                                    : { title: event.target.value }),
                                            }))}
                                            className={inputClassName}
                                            placeholder="Section title"
                                        />
                                    </div>
                                    <label className="flex items-center gap-3 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700">
                                        <input aria-label={`Section ${sectionIndex + 1} active`} type="checkbox" checked={section.isActive !== false} onChange={(event) => updateSection(section._key, (currentSection) => ({ ...currentSection, isActive: event.target.checked }))} className="h-4 w-4 rounded border-slate-300 text-sky-600" />
                                        Section active
                                    </label>
                                </div>
                                <div className="flex items-start gap-2">
                                    <button type="button" aria-label={`Move section ${sectionIndex + 1} up`} onClick={() => setDraft((value) => ({ ...value, sections: moveItem(value.sections, sectionIndex, 'up') }))} disabled={sectionIndex === 0} className="rounded-lg border border-slate-200 bg-white p-2 text-slate-600 disabled:opacity-40"><ChevronUp size={16} /></button>
                                    <button type="button" aria-label={`Move section ${sectionIndex + 1} down`} onClick={() => setDraft((value) => ({ ...value, sections: moveItem(value.sections, sectionIndex, 'down') }))} disabled={sectionIndex === draft.sections.length - 1} className="rounded-lg border border-slate-200 bg-white p-2 text-slate-600 disabled:opacity-40"><ChevronDown size={16} /></button>
                                    <button type="button" aria-label={`Delete section ${section.title || sectionIndex + 1}`} onClick={() => confirmDelete('Delete this footer section and all of its links?', () => setDraft((value) => ({ ...value, sections: value.sections.filter((item) => item._key !== section._key) })))} className="rounded-lg border border-rose-200 bg-white p-2 text-rose-700"><Trash2 size={16} /></button>
                                </div>
                            </div>

                            <div className="mt-4 space-y-3">
                                {section.links.map((link, linkIndex) => (
                                    <div key={link._key} className="rounded-xl border border-slate-200 bg-white p-4">
                                        <div className={`grid gap-3 ${localized ? 'xl:grid-cols-6' : 'xl:grid-cols-6'}`}>
                                            <div className="space-y-2 xl:col-span-2">
                                                {localized ? (
                                                    <div className="flex items-center justify-between gap-3">
                                                        <span className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                                            Label ({activeLanguage.toUpperCase()})
                                                        </span>
                                                        <button
                                                            type="button"
                                                            onClick={() => updateLocalizedField(
                                                                `link-${link._key}-label`,
                                                                link.labelLocalized?.en || '',
                                                                link.labelLocalized?.ar || '',
                                                                (translated) => updateLink(section._key, link._key, (currentLink) => ({
                                                                    ...currentLink,
                                                                    labelLocalized: {
                                                                        ...(currentLink.labelLocalized || createLocalizedField(currentLink.label)),
                                                                        ar: translated,
                                                                    },
                                                                })),
                                                            )}
                                                            disabled={!onTranslateContent || activeLanguage !== 'ar' || translatingKey === `link-${link._key}-label`}
                                                            className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 py-2 text-xs font-semibold uppercase tracking-[0.18em] text-slate-600 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-50"
                                                        >
                                                            {translatingKey === `link-${link._key}-label` ? <Loader2 size={14} className="animate-spin" /> : <Sparkles size={14} />}
                                                            Auto Translate
                                                        </button>
                                                    </div>
                                                ) : null}
                                                <input
                                                    aria-label={`Link ${linkIndex + 1} label ${activeLanguage.toUpperCase()}`}
                                                    dir={localized && activeLanguage === 'ar' ? 'rtl' : 'ltr'}
                                                    value={localized ? (link.labelLocalized?.[activeLanguage] || '') : link.label}
                                                    onChange={(event) => updateLink(section._key, link._key, (currentLink) => ({
                                                        ...currentLink,
                                                        ...(localized
                                                            ? {
                                                                labelLocalized: {
                                                                    ...(currentLink.labelLocalized || createLocalizedField(currentLink.label)),
                                                                    [activeLanguage]: event.target.value,
                                                                },
                                                            }
                                                            : { label: event.target.value }),
                                                    }))}
                                                    className={inputClassName}
                                                    placeholder="Label"
                                                />
                                            </div>
                                            <select aria-label={`Link ${linkIndex + 1} type`} value={link.type} onChange={(event) => updateLink(section._key, link._key, (currentLink) => ({ ...currentLink, type: event.target.value, url: event.target.value === 'Cms' ? '' : currentLink.url, cmsPageSlug: event.target.value === 'Cms' ? currentLink.cmsPageSlug : '' }))} className={inputClassName}>
                                                {LINK_TYPES.map((type) => <option key={type} value={type}>{type}</option>)}
                                            </select>
                                            {link.type === 'Cms' ? (
                                                allowCmsSlugEntry ? (
                                                    <div>
                                                        <input
                                                            aria-label={`Link ${linkIndex + 1} CMS page slug`}
                                                            value={link.cmsPageSlug}
                                                            onChange={(event) => updateLink(section._key, link._key, (currentLink) => ({ ...currentLink, cmsPageSlug: event.target.value }))}
                                                            className={inputClassName}
                                                            placeholder="about"
                                                            list={cmsOptions.length > 0 ? `cms-pages-${link._key}` : undefined}
                                                        />
                                                        {cmsOptions.length > 0 ? (
                                                            <datalist id={`cms-pages-${link._key}`}>
                                                                {cmsOptions.map((page) => (
                                                                    <option key={page.slug} value={page.slug}>
                                                                        {page.title}
                                                                    </option>
                                                                ))}
                                                            </datalist>
                                                        ) : null}
                                                    </div>
                                                ) : (
                                                    <select aria-label={`Link ${linkIndex + 1} CMS page`} value={link.cmsPageSlug} onChange={(event) => updateLink(section._key, link._key, (currentLink) => ({ ...currentLink, cmsPageSlug: event.target.value }))} className={inputClassName}>
                                                        <option value="">Select CMS page</option>
                                                        {cmsOptions.map((page) => <option key={page.slug} value={page.slug}>{page.title} ({page.slug})</option>)}
                                                    </select>
                                                )
                                            ) : (
                                                <input aria-label={`Link ${linkIndex + 1} URL`} value={link.url} onChange={(event) => updateLink(section._key, link._key, (currentLink) => ({ ...currentLink, url: event.target.value }))} className={inputClassName} placeholder={link.type === 'External' ? 'https://example.com' : '/about'} />
                                            )}
                                            <label className="flex items-center gap-2 rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-700">
                                                <input aria-label={`Link ${linkIndex + 1} active`} type="checkbox" checked={link.isActive !== false} onChange={(event) => updateLink(section._key, link._key, (currentLink) => ({ ...currentLink, isActive: event.target.checked }))} className="h-4 w-4 rounded border-slate-300 text-sky-600" />
                                                Active
                                            </label>
                                            <div className="flex items-center gap-2">
                                                <label className="flex items-center gap-2 rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-700">
                                                    <input aria-label={`Link ${linkIndex + 1} opens in new tab`} type="checkbox" checked={Boolean(link.openInNewTab)} onChange={(event) => updateLink(section._key, link._key, (currentLink) => ({ ...currentLink, openInNewTab: event.target.checked }))} className="h-4 w-4 rounded border-slate-300 text-sky-600" />
                                                    New tab
                                                </label>
                                                <button type="button" aria-label={`Move link ${linkIndex + 1} up`} onClick={() => updateSection(section._key, (currentSection) => ({ ...currentSection, links: moveItem(currentSection.links, linkIndex, 'up') }))} disabled={linkIndex === 0} className="rounded-lg border border-slate-200 bg-white p-2 text-slate-600 disabled:opacity-40"><ChevronUp size={16} /></button>
                                                <button type="button" aria-label={`Move link ${linkIndex + 1} down`} onClick={() => updateSection(section._key, (currentSection) => ({ ...currentSection, links: moveItem(currentSection.links, linkIndex, 'down') }))} disabled={linkIndex === section.links.length - 1} className="rounded-lg border border-slate-200 bg-white p-2 text-slate-600 disabled:opacity-40"><ChevronDown size={16} /></button>
                                                <button type="button" aria-label={`Delete link ${link.label || linkIndex + 1}`} onClick={() => confirmDelete('Delete this footer link?', () => updateSection(section._key, (currentSection) => ({ ...currentSection, links: currentSection.links.filter((item) => item._key !== link._key) })))} className="rounded-lg border border-rose-200 bg-white p-2 text-rose-700"><Trash2 size={16} /></button>
                                            </div>
                                        </div>
                                    </div>
                                ))}
                            </div>

                            <button onClick={() => updateSection(section._key, (currentSection) => ({ ...currentSection, links: [...currentSection.links, createLink(localized)] }))} className="mt-4 inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50">
                                <Plus size={16} />
                                Add Link
                            </button>
                        </div>
                    ))}
                </div>
            </div>

            <div className="rounded-[24px] border border-slate-200 bg-white p-6 shadow-sm">
                <div className="flex flex-col items-start justify-between gap-4 sm:flex-row sm:items-center">
                    <div>
                        <h2 className="text-lg font-semibold text-slate-900">Social Links</h2>
                        <p className="mt-1 text-sm text-slate-500">Only active links with valid URLs should be rendered.</p>
                    </div>
                    <button onClick={() => setDraft((value) => ({ ...value, socialLinks: [...value.socialLinks, createSocial()] }))} className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50">
                        <Plus size={16} />
                        Add Social Link
                    </button>
                </div>

                <div className="mt-6 space-y-3">
                    {draft.socialLinks.length === 0 ? <div className="rounded-2xl border border-dashed border-slate-300 bg-slate-50 px-6 py-10 text-center text-sm text-slate-500">No social links configured yet.</div> : null}
                    {draft.socialLinks.map((link, linkIndex) => (
                        <div key={link._key} className="grid gap-3 rounded-xl border border-slate-200 bg-slate-50/70 p-4 xl:grid-cols-6">
                            <input aria-label={`Social link ${linkIndex + 1} platform`} value={link.platform} onChange={(event) => setDraft((value) => ({ ...value, socialLinks: value.socialLinks.map((item) => item._key === link._key ? { ...item, platform: event.target.value } : item) }))} className={inputClassName} placeholder="Platform" />
                            <input aria-label={`Social link ${linkIndex + 1} URL`} value={link.url} onChange={(event) => setDraft((value) => ({ ...value, socialLinks: value.socialLinks.map((item) => item._key === link._key ? { ...item, url: event.target.value } : item) }))} className={inputClassName} placeholder="https://instagram.com/brand" />
                            <input aria-label={`Social link ${linkIndex + 1} icon`} value={link.icon} onChange={(event) => setDraft((value) => ({ ...value, socialLinks: value.socialLinks.map((item) => item._key === link._key ? { ...item, icon: event.target.value } : item) }))} className={inputClassName} placeholder="Icon" />
                            <label className="flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700">
                                <input aria-label={`Social link ${linkIndex + 1} active`} type="checkbox" checked={link.isActive !== false} onChange={(event) => setDraft((value) => ({ ...value, socialLinks: value.socialLinks.map((item) => item._key === link._key ? { ...item, isActive: event.target.checked } : item) }))} className="h-4 w-4 rounded border-slate-300 text-sky-600" />
                                Active
                            </label>
                            <button type="button" aria-label={`Move social link ${linkIndex + 1} up`} onClick={() => setDraft((value) => ({ ...value, socialLinks: moveItem(value.socialLinks, linkIndex, 'up') }))} disabled={linkIndex === 0} className="rounded-lg border border-slate-200 bg-white p-2 text-slate-600 disabled:opacity-40"><ChevronUp size={16} /></button>
                            <div className="flex items-center gap-2">
                                <button type="button" aria-label={`Move social link ${linkIndex + 1} down`} onClick={() => setDraft((value) => ({ ...value, socialLinks: moveItem(value.socialLinks, linkIndex, 'down') }))} disabled={linkIndex === draft.socialLinks.length - 1} className="rounded-lg border border-slate-200 bg-white p-2 text-slate-600 disabled:opacity-40"><ChevronDown size={16} /></button>
                                <button type="button" aria-label={`Delete social link ${link.platform || linkIndex + 1}`} onClick={() => confirmDelete('Delete this social link?', () => setDraft((value) => ({ ...value, socialLinks: value.socialLinks.filter((item) => item._key !== link._key) })))} className="rounded-lg border border-rose-200 bg-white p-2 text-rose-700"><Trash2 size={16} /></button>
                            </div>
                        </div>
                    ))}
                </div>
            </div>
            <AuthoringConfirmDialog
                open={isSaveConfirmOpen}
                title="Save Footer live?"
                description="This action immediately replaces the live Footer content. It does not publish Homepage, Navigation, Theme, or Pages."
                confirmLabel="Save live Footer"
                confirmTone="publish"
                busy={isSaving}
                onClose={() => setIsSaveConfirmOpen(false)}
                onConfirm={() => handleSave()}
            />
            <AuthoringConfirmDialog
                open={isDiscardConfirmOpen}
                title="Discard Footer changes?"
                description="The editor will return to the accepted live Footer configuration."
                confirmLabel="Discard Changes"
                confirmTone="danger"
                onClose={() => setIsDiscardConfirmOpen(false)}
                onConfirm={handleDiscard}
            />
            <AuthoringConfirmDialog
                open={pendingDelete != null}
                title="Delete Footer item?"
                description={pendingDelete?.message}
                confirmLabel="Delete Item"
                confirmTone="danger"
                onClose={() => setPendingDelete(null)}
                onConfirm={() => {
                    pendingDelete?.remove()
                    setPendingDelete(null)
                }}
            />
            <AuthoringConfirmDialog
                open={pendingTranslation != null}
                title="Overwrite Arabic translation?"
                description="Arabic content already exists. Replace it with a fresh AI translation from the current English content?"
                confirmLabel="Overwrite Translation"
                confirmTone="danger"
                onClose={() => setPendingTranslation(null)}
                onConfirm={() => {
                    const translation = pendingTranslation
                    setPendingTranslation(null)
                    void performTranslation(translation.key, translation.source, translation.apply)
                }}
            />
            <AuthoringConflictDialog
                open={conflict != null}
                message={conflict?.message}
                hasLatest={Boolean(conflict?.latest)}
                busy={isSaving}
                onClose={() => setConflict(null)}
                onLoadLatest={handleLoadLatest}
                onRetry={handleRetryAfterConflict}
            />
            <UnsavedChangesDialog {...unsavedDialogProps} />
        </div>
    )
}

export default FooterConfigScreen
