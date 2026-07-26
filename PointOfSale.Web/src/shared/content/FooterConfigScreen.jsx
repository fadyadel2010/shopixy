import { useEffect, useMemo, useState } from 'react'
import { ChevronDown, ChevronUp, Loader2, Plus, RefreshCcw, Save, Sparkles, Trash2 } from 'lucide-react'

const LINK_TYPES = ['Internal', 'Cms', 'External']
const inputClassName = 'w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 outline-none focus:border-sky-500 focus:ring-2 focus:ring-sky-100'
const createLocalizedField = (fallback = '') => ({ en: fallback || '', ar: '' })

const createId = () =>
    typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
        ? crypto.randomUUID()
        : `footer-${Date.now()}-${Math.random().toString(16).slice(2)}`

const createLink = (localized) => ({
    _key: createId(),
    label: '',
    labelLocalized: localized ? createLocalizedField('') : undefined,
    type: 'Internal',
    url: '',
    cmsPageSlug: '',
    openInNewTab: false,
    isActive: true,
})

const createSection = (localized) => ({
    _key: createId(),
    title: '',
    titleLocalized: localized ? createLocalizedField('') : undefined,
    isActive: true,
    links: [createLink(localized)],
})

const createSocial = () => ({
    _key: createId(),
    platform: '',
    url: '',
    icon: '',
    isActive: true,
})

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

function normalizeConfig(configuration, localized) {
    return {
        sections: (configuration?.sections || [])
            .slice()
            .sort((left, right) => (left.order ?? 0) - (right.order ?? 0))
            .map((section) => ({
                _key: createId(),
                title: section.title || '',
                titleLocalized: localized
                    ? {
                        en: section.titleLocalized?.en || section.title || '',
                        ar: section.titleLocalized?.ar || '',
                    }
                    : undefined,
                isActive: section.isActive !== false,
                links: (section.links || [])
                    .slice()
                    .sort((left, right) => (left.order ?? 0) - (right.order ?? 0))
                    .map((link) => ({
                        _key: createId(),
                        label: link.label || '',
                        labelLocalized: localized
                            ? {
                                en: link.labelLocalized?.en || link.label || '',
                                ar: link.labelLocalized?.ar || '',
                            }
                            : undefined,
                        type: link.type || 'Internal',
                        url: link.url || '',
                        cmsPageSlug: link.cmsPageSlug || '',
                        openInNewTab: Boolean(link.openInNewTab),
                        isActive: link.isActive !== false,
                    })),
            })),
        socialLinks: (configuration?.socialLinks || [])
            .slice()
            .sort((left, right) => (left.order ?? 0) - (right.order ?? 0))
            .map((link) => ({
                _key: createId(),
                platform: link.platform || '',
                url: link.url || '',
                icon: link.icon || '',
                isActive: link.isActive !== false,
            })),
    }
}

function toRequest(draft, localized) {
    return {
        sections: draft.sections.map((section, sectionIndex) => ({
            title: localized ? (section.titleLocalized?.en || '').trim() : section.title.trim(),
            ...(localized ? { titleLocalized: trimLocalizedField(section.titleLocalized) } : {}),
            order: sectionIndex,
            isActive: section.isActive !== false,
            links: section.links.map((link, linkIndex) => ({
                label: localized ? (link.labelLocalized?.en || '').trim() : link.label.trim(),
                ...(localized ? { labelLocalized: trimLocalizedField(link.labelLocalized) } : {}),
                type: link.type,
                url: link.type === 'Cms' ? null : (link.url || '').trim(),
                cmsPageSlug: link.type === 'Cms' ? (link.cmsPageSlug || '').trim() : null,
                openInNewTab: Boolean(link.openInNewTab),
                order: linkIndex,
                isActive: link.isActive !== false,
            })),
        })),
        socialLinks: draft.socialLinks.map((link, index) => ({
            platform: link.platform.trim(),
            url: link.url.trim(),
            icon: (link.icon || '').trim() || null,
            order: index,
            isActive: link.isActive !== false,
        })),
    }
}

const getErrorMessage = (error) =>
    error?.message || error?.response?.data?.message || 'Unable to save the footer configuration.'

function trimLocalizedField(field) {
    const trimmed = {
        en: (field?.en || '').trim(),
        ar: (field?.ar || '').trim(),
    }

    return trimmed.en || trimmed.ar ? trimmed : null
}

export function FooterConfigScreen({
    title,
    description,
    note,
    configuration,
    cmsPages,
    localized = false,
    allowCmsSlugEntry = false,
    isLoading,
    isSaving,
    error,
    conflict,
    onRefresh,
    onSave,
    onLoadLatest,
    onDismissConflict,
    onTranslateContent,
}) {
    const [draft, setDraft] = useState(() => normalizeConfig(configuration, localized))
    const [saveError, setSaveError] = useState('')
    const [activeLanguage, setActiveLanguage] = useState('en')
    const [translatingKey, setTranslatingKey] = useState('')

    useEffect(() => {
        setDraft(normalizeConfig(configuration, localized))
    }, [configuration, localized])

    const baseline = useMemo(
        () => JSON.stringify(toRequest(normalizeConfig(configuration, localized), localized)),
        [configuration, localized],
    )
    const current = useMemo(() => JSON.stringify(toRequest(draft, localized)), [draft, localized])
    const isDirty = baseline !== current

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

    const updateLocalizedField = async (key, source, target, apply) => {
        if (!onTranslateContent || activeLanguage !== 'ar') {
            return
        }

        if (!source?.trim()) {
            window.alert('Add English content first, then translate it into Arabic.')
            return
        }

        if (target?.trim() && !window.confirm('Arabic content already exists. Overwrite it with a fresh AI translation?')) {
            return
        }

        setTranslatingKey(key)
        try {
            const translated = await onTranslateContent(source, 'en', 'ar')
            apply(translated)
        } catch (saveFailure) {
            window.alert(getErrorMessage(saveFailure))
        } finally {
            setTranslatingKey('')
        }
    }

    const handleSave = async () => {
        setSaveError('')

        try {
            await onSave(toRequest(draft, localized))
        } catch (saveFailure) {
            setSaveError(getErrorMessage(saveFailure))
        }
    }

    if (isLoading) {
        return (
            <div className="flex min-h-[280px] items-center justify-center rounded-[24px] border border-slate-200 bg-white">
                <Loader2 size={28} className="animate-spin text-slate-500" />
            </div>
        )
    }

    return (
        <div className="space-y-6">
            <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
                <div>
                    <h1 className="text-3xl font-semibold text-slate-900">{title}</h1>
                    <p className="mt-2 max-w-3xl text-sm text-slate-500">{description}</p>
                </div>
                <div className="flex flex-wrap gap-3">
                    <button onClick={onRefresh} className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50">
                        <RefreshCcw size={16} />
                        Refresh
                    </button>
                    <button onClick={handleSave} disabled={isSaving || !isDirty} className="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-slate-800 disabled:opacity-60">
                        {isSaving ? <Loader2 size={16} className="animate-spin" /> : <Save size={16} />}
                        Save Footer
                    </button>
                </div>
            </div>

            <div className="rounded-[24px] border border-sky-100 bg-sky-50/80 px-5 py-4 text-sm text-sky-900">{note}</div>
            {error ? <div className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">{error}</div> : null}
            {saveError ? <div className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">{saveError}</div> : null}
            {conflict ? (
                <div className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-4 text-sm text-amber-900">
                    <p className="font-semibold">A newer footer version is available</p>
                    <p className="mt-1 text-amber-800">{conflict.message}</p>
                    <p className="mt-1 text-amber-700">
                        Your current draft is still visible. Load the latest version to review it, or keep this draft and merge manually.
                    </p>
                    <div className="mt-3 flex flex-wrap gap-3">
                        <button
                            type="button"
                            onClick={onLoadLatest}
                            className="inline-flex items-center gap-2 rounded-xl bg-amber-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-amber-950"
                        >
                            <RefreshCcw size={16} />
                            Load latest version
                        </button>
                        <button
                            type="button"
                            onClick={onDismissConflict}
                            className="inline-flex items-center gap-2 rounded-xl border border-amber-300 bg-white px-4 py-2 text-sm font-medium text-amber-900 transition hover:bg-amber-100"
                        >
                            Keep my draft
                        </button>
                    </div>
                </div>
            ) : null}

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
                <div className="flex items-center justify-between">
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
                                        <input type="checkbox" checked={section.isActive !== false} onChange={(event) => updateSection(section._key, (currentSection) => ({ ...currentSection, isActive: event.target.checked }))} className="h-4 w-4 rounded border-slate-300 text-sky-600" />
                                        Section active
                                    </label>
                                </div>
                                <div className="flex items-start gap-2">
                                    <button onClick={() => setDraft((value) => ({ ...value, sections: moveItem(value.sections, sectionIndex, 'up') }))} disabled={sectionIndex === 0} className="rounded-lg border border-slate-200 bg-white p-2 text-slate-600 disabled:opacity-40"><ChevronUp size={16} /></button>
                                    <button onClick={() => setDraft((value) => ({ ...value, sections: moveItem(value.sections, sectionIndex, 'down') }))} disabled={sectionIndex === draft.sections.length - 1} className="rounded-lg border border-slate-200 bg-white p-2 text-slate-600 disabled:opacity-40"><ChevronDown size={16} /></button>
                                    <button onClick={() => setDraft((value) => ({ ...value, sections: value.sections.filter((item) => item._key !== section._key) }))} className="rounded-lg border border-rose-200 bg-white p-2 text-rose-700"><Trash2 size={16} /></button>
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
                                            <select value={link.type} onChange={(event) => updateLink(section._key, link._key, (currentLink) => ({ ...currentLink, type: event.target.value, url: event.target.value === 'Cms' ? '' : currentLink.url, cmsPageSlug: event.target.value === 'Cms' ? currentLink.cmsPageSlug : '' }))} className={inputClassName}>
                                                {LINK_TYPES.map((type) => <option key={type} value={type}>{type}</option>)}
                                            </select>
                                            {link.type === 'Cms' ? (
                                                allowCmsSlugEntry ? (
                                                    <div>
                                                        <input
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
                                                    <select value={link.cmsPageSlug} onChange={(event) => updateLink(section._key, link._key, (currentLink) => ({ ...currentLink, cmsPageSlug: event.target.value }))} className={inputClassName}>
                                                        <option value="">Select CMS page</option>
                                                        {cmsOptions.map((page) => <option key={page.slug} value={page.slug}>{page.title} ({page.slug})</option>)}
                                                    </select>
                                                )
                                            ) : (
                                                <input value={link.url} onChange={(event) => updateLink(section._key, link._key, (currentLink) => ({ ...currentLink, url: event.target.value }))} className={inputClassName} placeholder={link.type === 'External' ? 'https://example.com' : '/about'} />
                                            )}
                                            <label className="flex items-center gap-2 rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-700">
                                                <input type="checkbox" checked={link.isActive !== false} onChange={(event) => updateLink(section._key, link._key, (currentLink) => ({ ...currentLink, isActive: event.target.checked }))} className="h-4 w-4 rounded border-slate-300 text-sky-600" />
                                                Active
                                            </label>
                                            <div className="flex items-center gap-2">
                                                <label className="flex items-center gap-2 rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-700">
                                                    <input type="checkbox" checked={Boolean(link.openInNewTab)} onChange={(event) => updateLink(section._key, link._key, (currentLink) => ({ ...currentLink, openInNewTab: event.target.checked }))} className="h-4 w-4 rounded border-slate-300 text-sky-600" />
                                                    New tab
                                                </label>
                                                <button onClick={() => updateSection(section._key, (currentSection) => ({ ...currentSection, links: moveItem(currentSection.links, linkIndex, 'up') }))} disabled={linkIndex === 0} className="rounded-lg border border-slate-200 bg-white p-2 text-slate-600 disabled:opacity-40"><ChevronUp size={16} /></button>
                                                <button onClick={() => updateSection(section._key, (currentSection) => ({ ...currentSection, links: moveItem(currentSection.links, linkIndex, 'down') }))} disabled={linkIndex === section.links.length - 1} className="rounded-lg border border-slate-200 bg-white p-2 text-slate-600 disabled:opacity-40"><ChevronDown size={16} /></button>
                                                <button onClick={() => updateSection(section._key, (currentSection) => ({ ...currentSection, links: currentSection.links.filter((item) => item._key !== link._key) }))} className="rounded-lg border border-rose-200 bg-white p-2 text-rose-700"><Trash2 size={16} /></button>
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
                <div className="flex items-center justify-between">
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
                            <input value={link.platform} onChange={(event) => setDraft((value) => ({ ...value, socialLinks: value.socialLinks.map((item) => item._key === link._key ? { ...item, platform: event.target.value } : item) }))} className={inputClassName} placeholder="Platform" />
                            <input value={link.url} onChange={(event) => setDraft((value) => ({ ...value, socialLinks: value.socialLinks.map((item) => item._key === link._key ? { ...item, url: event.target.value } : item) }))} className={inputClassName} placeholder="https://instagram.com/brand" />
                            <input value={link.icon} onChange={(event) => setDraft((value) => ({ ...value, socialLinks: value.socialLinks.map((item) => item._key === link._key ? { ...item, icon: event.target.value } : item) }))} className={inputClassName} placeholder="Icon" />
                            <label className="flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700">
                                <input type="checkbox" checked={link.isActive !== false} onChange={(event) => setDraft((value) => ({ ...value, socialLinks: value.socialLinks.map((item) => item._key === link._key ? { ...item, isActive: event.target.checked } : item) }))} className="h-4 w-4 rounded border-slate-300 text-sky-600" />
                                Active
                            </label>
                            <button onClick={() => setDraft((value) => ({ ...value, socialLinks: moveItem(value.socialLinks, linkIndex, 'up') }))} disabled={linkIndex === 0} className="rounded-lg border border-slate-200 bg-white p-2 text-slate-600 disabled:opacity-40"><ChevronUp size={16} /></button>
                            <div className="flex items-center gap-2">
                                <button onClick={() => setDraft((value) => ({ ...value, socialLinks: moveItem(value.socialLinks, linkIndex, 'down') }))} disabled={linkIndex === draft.socialLinks.length - 1} className="rounded-lg border border-slate-200 bg-white p-2 text-slate-600 disabled:opacity-40"><ChevronDown size={16} /></button>
                                <button onClick={() => setDraft((value) => ({ ...value, socialLinks: value.socialLinks.filter((item) => item._key !== link._key) }))} className="rounded-lg border border-rose-200 bg-white p-2 text-rose-700"><Trash2 size={16} /></button>
                            </div>
                        </div>
                    ))}
                </div>
            </div>
        </div>
    )
}

export default FooterConfigScreen
