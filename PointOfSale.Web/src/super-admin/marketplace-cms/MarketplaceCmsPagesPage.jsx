import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
    AlertCircle,
    CheckCircle2,
    Clock3,
    Eye,
    FilePlus2,
    Globe,
    Loader2,
    PencilLine,
    RefreshCcw,
    Trash2,
} from 'lucide-react'
import { marketplaceCmsService } from './marketplaceCmsService'

const statusStyles = {
    Published: 'bg-emerald-50 text-emerald-700',
    Draft: 'bg-amber-50 text-amber-700',
    UnpublishedChanges: 'bg-sky-50 text-sky-700',
}

export function MarketplaceCmsPagesPage() {
    const navigate = useNavigate()

    const [pages, setPages] = useState([])
    const [isLoading, setIsLoading] = useState(true)
    const [busyPageId, setBusyPageId] = useState(null)
    const [error, setError] = useState('')

    const publicBaseUrl = useMemo(
        () => import.meta.env.VITE_MARKETPLACE_PUBLIC_URL || window.location.origin,
        [],
    )

    useEffect(() => {
        loadPages()
    }, [])

    async function loadPages() {
        setIsLoading(true)
        setError('')

        try {
            const data = await marketplaceCmsService.listPages()
            setPages(data)
        } catch (err) {
            console.error('Failed to load marketplace CMS pages', err)
            setError(err?.message || 'Unable to load marketplace CMS pages.')
        } finally {
            setIsLoading(false)
        }
    }

    async function handlePublish(page) {
        if (!window.confirm(`Publish "${page.title}" to the marketplace domain now?`)) {
            return
        }

        setBusyPageId(page.id)
        try {
            await marketplaceCmsService.publishPage(page.id)
            await loadPages()
        } catch (err) {
            console.error('Failed to publish marketplace CMS page', err)
            alert(err?.message || 'Failed to publish marketplace page.')
        } finally {
            setBusyPageId(null)
        }
    }

    async function handleDelete(page) {
        if (!window.confirm(`Delete "${page.title}" and all of its versions?`)) {
            return
        }

        setBusyPageId(page.id)
        try {
            await marketplaceCmsService.deletePage(page.id)
            await loadPages()
        } catch (err) {
            console.error('Failed to delete marketplace CMS page', err)
            alert(err?.message || 'Failed to delete marketplace page.')
        } finally {
            setBusyPageId(null)
        }
    }

    async function handlePreview(page) {
        setBusyPageId(page.id)

        try {
            const previewAccess = await marketplaceCmsService.previewPage(page.id)
            const previewUrl = `${publicBaseUrl}/${previewAccess.slug}?preview=true&previewToken=${encodeURIComponent(previewAccess.previewToken)}`
            window.open(previewUrl, '_blank', 'noopener,noreferrer')
        } catch (err) {
            console.error('Failed to open marketplace CMS preview', err)
            alert(err?.message || 'Unable to create a secure preview link.')
        } finally {
            setBusyPageId(null)
        }
    }

    function renderStatus(page) {
        const icon = page.status === 'Published'
            ? <CheckCircle2 size={14} />
            : page.status === 'UnpublishedChanges'
                ? <AlertCircle size={14} />
                : <Clock3 size={14} />

        const label = page.status === 'UnpublishedChanges'
            ? 'Draft ready to publish'
            : page.status === 'Published'
                ? 'Published'
                : 'Draft'

        return (
            <span className={`inline-flex items-center gap-1 rounded-full px-3 py-1 text-xs font-medium ${statusStyles[page.status] || statusStyles.Draft}`}>
                {icon}
                {label}
            </span>
        )
    }

    return (
        <div className="space-y-6">
            <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
                <div>
                    <p className="text-sm font-semibold uppercase tracking-[0.2em] text-sky-600">
                        Global Content
                    </p>
                    <h1 className="mt-2 text-3xl font-semibold text-slate-900">
                        Marketplace CMS Pages
                    </h1>
                    <p className="mt-2 max-w-3xl text-sm text-slate-500">
                        Create and publish global marketplace pages like About, Privacy, Terms, and campaign landing pages.
                        These pages are completely separate from tenant storefront CMS content.
                    </p>
                </div>

                <div className="flex flex-wrap gap-3">
                    <button
                        onClick={() => navigate('/super-admin/marketplace/footer')}
                        className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                    >
                        <Globe size={16} />
                        Footer & Social
                    </button>
                    <button
                        onClick={loadPages}
                        className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                    >
                        <RefreshCcw size={16} />
                        Refresh
                    </button>
                    <button
                        onClick={() => navigate('/super-admin/marketplace/pages/new')}
                        className="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-slate-800"
                    >
                        <FilePlus2 size={16} />
                        New Page
                    </button>
                </div>
            </div>

            <div className="rounded-[28px] border border-sky-100 bg-sky-50/70 px-5 py-4 text-sm text-sky-900">
                Public routes resolve at the marketplace root, for example `/about`, `/privacy`, and `/terms`.
                Storefront tenant pages remain isolated under the tenant storefront app and are not read from this system.
            </div>

            {error ? (
                <div className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
                    {error}
                </div>
            ) : null}

            {isLoading ? (
                <div className="flex min-h-[260px] items-center justify-center rounded-3xl border border-slate-200 bg-white">
                    <Loader2 size={32} className="animate-spin text-slate-500" />
                </div>
            ) : pages.length === 0 ? (
                <div className="rounded-[28px] border border-dashed border-slate-300 bg-white px-8 py-14 text-center">
                    <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-2xl bg-sky-50 text-sky-700">
                        <FilePlus2 size={28} />
                    </div>
                    <h2 className="mt-5 text-xl font-semibold text-slate-900">No marketplace pages yet</h2>
                    <p className="mx-auto mt-2 max-w-xl text-sm text-slate-500">
                        Start with an About page, policy page, or a campaign landing page for the marketplace domain.
                    </p>
                    <button
                        onClick={() => navigate('/super-admin/marketplace/pages/new')}
                        className="mt-6 rounded-xl bg-slate-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-slate-800"
                    >
                        Create First Page
                    </button>
                </div>
            ) : (
                <div className="overflow-hidden rounded-[28px] border border-slate-200 bg-white shadow-sm">
                    <div className="grid grid-cols-[1.3fr_1fr_0.9fr_0.9fr_0.9fr] gap-4 border-b border-slate-200 px-6 py-4 text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">
                        <span>Page</span>
                        <span>Mode</span>
                        <span>Status</span>
                        <span>Updated</span>
                        <span className="text-right">Actions</span>
                    </div>

                    <div className="divide-y divide-slate-100">
                        {pages.map((page) => {
                            const isBusy = busyPageId === page.id

                            return (
                                <div
                                    key={page.id}
                                    className="grid grid-cols-[1.3fr_1fr_0.9fr_0.9fr_0.9fr] gap-4 px-6 py-5"
                                >
                                    <div>
                                        <div className="font-medium text-slate-900">{page.title}</div>
                                        <div className="mt-1 text-sm text-slate-500">/{page.slug}</div>
                                    </div>

                                    <div className="text-sm text-slate-600">
                                        {page.contentType === 'Blocks' ? 'Blocks' : 'Rich text'}
                                    </div>

                                    <div>{renderStatus(page)}</div>

                                    <div className="text-sm text-slate-500">
                                        {page.updatedAtUtc ? new Date(page.updatedAtUtc).toLocaleString() : '-'}
                                    </div>

                                    <div className="flex justify-end gap-2">
                                        <button
                                            onClick={() => navigate(`/super-admin/marketplace/pages/${page.id}`)}
                                            className="rounded-xl border border-slate-200 p-2 text-slate-600 transition hover:border-slate-300 hover:bg-slate-50"
                                            title="Edit"
                                        >
                                            <PencilLine size={16} />
                                        </button>
                                        <button
                                            onClick={() => handlePreview(page)}
                                            disabled={isBusy}
                                            className="rounded-xl border border-slate-200 p-2 text-slate-600 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-60"
                                            title="Preview"
                                        >
                                            {isBusy ? <Loader2 size={16} className="animate-spin" /> : <Eye size={16} />}
                                        </button>
                                        <button
                                            onClick={() => handlePublish(page)}
                                            disabled={isBusy}
                                            className="rounded-xl border border-emerald-200 p-2 text-emerald-700 transition hover:bg-emerald-50 disabled:opacity-60"
                                            title="Publish"
                                        >
                                            {isBusy ? <Loader2 size={16} className="animate-spin" /> : <Globe size={16} />}
                                        </button>
                                        <button
                                            onClick={() => handleDelete(page)}
                                            disabled={isBusy}
                                            className="rounded-xl border border-rose-200 p-2 text-rose-700 transition hover:bg-rose-50 disabled:opacity-60"
                                            title="Delete"
                                        >
                                            {isBusy ? <Loader2 size={16} className="animate-spin" /> : <Trash2 size={16} />}
                                        </button>
                                    </div>
                                </div>
                            )
                        })}
                    </div>
                </div>
            )}
        </div>
    )
}

export default MarketplaceCmsPagesPage
