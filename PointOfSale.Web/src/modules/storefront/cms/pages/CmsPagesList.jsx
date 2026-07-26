import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
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
import { cmsService } from '../services'
import { useAuthStore } from '../../../../store/authStore'
import { deriveCurrentCmsPageListPresentation } from '../cmsPageIdentityAuthority'

const statusStyles = {
    Published: 'bg-emerald-50 text-emerald-700',
    Draft: 'bg-amber-50 text-amber-700',
    UnpublishedChanges: 'bg-sky-50 text-sky-700',
}

export function CmsPagesList() {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const navigate = useNavigate()

    const [pages, setPages] = useState([])
    const [isLoading, setIsLoading] = useState(true)
    const [isSeeding, setIsSeeding] = useState(false)
    const [busyPageId, setBusyPageId] = useState(null)
    const [error, setError] = useState('')

    const storefrontBaseUrl = import.meta.env.VITE_STOREFRONT_URL || 'http://localhost:5174'
    const currentUser = useAuthStore((state) => state.user)
    const currentTenantId = currentUser?.tenantId || ''

    useEffect(() => {
        loadPages()
    }, [])

    async function loadPages() {
        setIsLoading(true)
        setError('')

        try {
            const data = await cmsService.listPages()
            const admittedPages = deriveCurrentCmsPageListPresentation(data, currentUser)
            if (!admittedPages) {
                throw new Error('CMS page list was rejected by backend page identity admission.')
            }

            setPages(admittedPages)
        } catch (err) {
            console.error('Failed to load CMS pages', err)
            setError(isRTL ? 'تعذر تحميل الصفحات.' : 'Unable to load CMS pages.')
        } finally {
            setIsLoading(false)
        }
    }

    async function handleSeedDefaults() {
        if (!window.confirm(isRTL ? 'إنشاء الصفحات الافتراضية الآن؟' : 'Create the default CMS pages now?')) {
            return
        }

        setIsSeeding(true)
        try {
            await cmsService.seedDefaultPages()
            await loadPages()
        } catch (err) {
            console.error('Failed to seed CMS pages', err)
            alert(isRTL ? 'فشل إنشاء الصفحات الافتراضية.' : 'Failed to create default CMS pages.')
        } finally {
            setIsSeeding(false)
        }
    }

    async function handlePublish(page) {
        if (!window.confirm(
            isRTL
                ? `سيتم نشر الصفحة "${page.title}". هل تريد المتابعة؟`
                : `Publish "${page.title}" now?`
        )) {
            return
        }

        setBusyPageId(page.id)
        try {
            await cmsService.publishPage(page.id, page.draftVersion)
            await loadPages()
        } catch (err) {
            console.error('Failed to publish CMS page', err)
            alert(isRTL ? 'فشل نشر الصفحة.' : 'Failed to publish page.')
        } finally {
            setBusyPageId(null)
        }
    }

    async function handleDelete(page) {
        if (!window.confirm(
            isRTL
                ? `سيتم حذف الصفحة "${page.title}" من مساحة العمل الحالية. هل تريد المتابعة؟`
                : `Delete "${page.title}" from the current workspace?`
        )) {
            return
        }

        setBusyPageId(page.id)
        try {
            await cmsService.deletePage(page.id, page.draftVersion ?? page.publishedVersion)
            await loadPages()
        } catch (err) {
            console.error('Failed to delete CMS page', err)
            alert(isRTL ? 'فشل حذف الصفحة.' : 'Failed to delete page.')
        } finally {
            setBusyPageId(null)
        }
    }

    async function handlePreview(page) {
        setBusyPageId(page.id)

        try {
            if (!currentTenantId) {
                throw new Error('Current backend vendor identity is unavailable.')
            }
            const previewAccess = await cmsService.createPreviewLink(page.id)
            const previewUrl = `${storefrontBaseUrl}/${previewAccess.slug}?tenant=${encodeURIComponent(currentTenantId)}&preview=true&previewToken=${encodeURIComponent(previewAccess.previewToken)}`
            window.open(previewUrl, '_blank', 'noopener,noreferrer')
        } catch (err) {
            console.error('Failed to open CMS preview', err)
            alert(isRTL ? 'تعذر إنشاء رابط المعاينة الآمن.' : 'Unable to create a secure preview link.')
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

        return (
            <span className={`inline-flex items-center gap-1 rounded-full px-3 py-1 text-xs font-medium ${statusStyles[page.status] || statusStyles.Draft}`}>
                {icon}
                {page.status === 'UnpublishedChanges'
                    ? (isRTL ? 'مسودة جاهزة للنشر' : 'Draft ready to publish')
                    : page.status === 'Published'
                        ? (isRTL ? 'منشور' : 'Published')
                        : (isRTL ? 'مسودة' : 'Draft')}
            </span>
        )
    }

    return (
        <div className="space-y-6">
            <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
                <div>
                    <p className="text-sm font-semibold uppercase tracking-[0.2em] text-sky-600">
                        {isRTL ? 'إدارة المحتوى' : 'Content Management'}
                    </p>
                    <h1 className="mt-2 text-3xl font-semibold text-slate-900">
                        {isRTL ? 'صفحات المتجر' : 'CMS Pages'}
                    </h1>
                    <p className="mt-2 max-w-2xl text-sm text-slate-500">
                        {isRTL
                            ? 'أنشئ صفحات معلومات وسياسات قابلة للنشر، مع معاينة المسودات قبل ظهورها للزوار.'
                            : 'Create and publish branded pages like About, Contact, and policy content without exposing drafts to storefront traffic.'}
                    </p>
                </div>

                <div className="flex flex-wrap gap-3">
                    <button
                        onClick={loadPages}
                        className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                    >
                        <RefreshCcw size={16} />
                        {isRTL ? 'تحديث' : 'Refresh'}
                    </button>
                    <button
                        onClick={handleSeedDefaults}
                        disabled={isSeeding}
                        className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-60"
                    >
                        {isSeeding ? <Loader2 size={16} className="animate-spin" /> : <Globe size={16} />}
                        {isRTL ? 'إضافة الصفحات الافتراضية' : 'Seed Default Pages'}
                    </button>
                    <button
                        onClick={() => navigate('/storefront/cms/pages/new')}
                        className="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-slate-800"
                    >
                        <FilePlus2 size={16} />
                        {isRTL ? 'صفحة جديدة' : 'New Page'}
                    </button>
                </div>
            </div>

            {error && (
                <div className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
                    {error}
                </div>
            )}

            {isLoading ? (
                <div className="flex min-h-[260px] items-center justify-center rounded-3xl border border-slate-200 bg-white">
                    <Loader2 size={32} className="animate-spin text-slate-500" />
                </div>
            ) : pages.length === 0 ? (
                <div className="rounded-[28px] border border-dashed border-slate-300 bg-white px-8 py-14 text-center">
                    <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-2xl bg-sky-50 text-sky-700">
                        <FilePlus2 size={28} />
                    </div>
                    <h2 className="mt-5 text-xl font-semibold text-slate-900">
                        {isRTL ? 'لا توجد صفحات بعد' : 'No CMS pages yet'}
                    </h2>
                    <p className="mx-auto mt-2 max-w-xl text-sm text-slate-500">
                        {isRTL
                            ? 'ابدأ بصفحات جاهزة مثل من نحن والاتصال والسياسات، أو أنشئ صفحة مخصصة من الصفر.'
                            : 'Start with ready-made marketplace pages or create a custom content page from scratch.'}
                    </p>
                    <div className="mt-6 flex flex-wrap justify-center gap-3">
                        <button
                            onClick={handleSeedDefaults}
                            disabled={isSeeding}
                            className="rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-60"
                        >
                            {isRTL ? 'إنشاء الصفحات الافتراضية' : 'Create Default Pages'}
                        </button>
                        <button
                            onClick={() => navigate('/storefront/cms/pages/new')}
                            className="rounded-xl bg-slate-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-slate-800"
                        >
                            {isRTL ? 'إنشاء صفحة مخصصة' : 'Create Custom Page'}
                        </button>
                    </div>
                </div>
            ) : (
                <div className="overflow-hidden rounded-[28px] border border-slate-200 bg-white shadow-sm">
                    <div className="grid grid-cols-[1.3fr_1fr_0.9fr_0.9fr_0.9fr] gap-4 border-b border-slate-200 px-6 py-4 text-xs font-semibold uppercase tracking-[0.18em] text-slate-400">
                        <span>{isRTL ? 'الصفحة' : 'Page'}</span>
                        <span>{isRTL ? 'النوع' : 'Mode'}</span>
                        <span>{isRTL ? 'الحالة' : 'Status'}</span>
                        <span>{isRTL ? 'آخر تحديث' : 'Updated'}</span>
                        <span className="text-right">{isRTL ? 'الإجراءات' : 'Actions'}</span>
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
                                        {page.contentType === 'Blocks'
                                            ? (isRTL ? 'كتل مرنة' : 'Blocks')
                                            : (isRTL ? 'نص غني' : 'Rich text')}
                                    </div>

                                    <div>{renderStatus(page)}</div>

                                    <div className="text-sm text-slate-500">
                                        {page.updatedAtUtc ? new Date(page.updatedAtUtc).toLocaleString() : '-'}
                                    </div>

                                    <div className="flex justify-end gap-2">
                                        <button
                                            onClick={() => navigate(`/storefront/cms/pages/${page.id}`)}
                                            className="rounded-xl border border-slate-200 p-2 text-slate-600 transition hover:border-slate-300 hover:bg-slate-50"
                                            title={isRTL ? 'تعديل' : 'Edit'}
                                        >
                                            <PencilLine size={16} />
                                        </button>
                                        <button
                                            onClick={() => handlePreview(page)}
                                            disabled={isBusy}
                                            className="rounded-xl border border-slate-200 p-2 text-slate-600 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-60"
                                            title={isRTL ? 'معاينة' : 'Preview'}
                                        >
                                            {isBusy ? <Loader2 size={16} className="animate-spin" /> : <Eye size={16} />}
                                        </button>
                                        <button
                                            onClick={() => handlePublish(page)}
                                            disabled={isBusy}
                                            className="rounded-xl border border-emerald-200 p-2 text-emerald-700 transition hover:bg-emerald-50 disabled:opacity-60"
                                            title={isRTL ? 'نشر' : 'Publish'}
                                        >
                                            {isBusy ? <Loader2 size={16} className="animate-spin" /> : <Globe size={16} />}
                                        </button>
                                        <button
                                            onClick={() => handleDelete(page)}
                                            disabled={isBusy}
                                            className="rounded-xl border border-rose-200 p-2 text-rose-700 transition hover:bg-rose-50 disabled:opacity-60"
                                            title={isRTL ? 'حذف' : 'Delete'}
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

export default CmsPagesList
