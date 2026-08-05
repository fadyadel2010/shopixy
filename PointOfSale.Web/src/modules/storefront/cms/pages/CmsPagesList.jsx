import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
    FilePlus2,
    Loader2,
    PencilLine,
    RefreshCcw,
    Trash2,
} from 'lucide-react'
import { cmsService } from '../services'
import { useAuthStore } from '../../../../store/authStore'
import { deriveCurrentCmsPageListPresentation } from '../cmsPageIdentityAuthority'
import {
    AuthoringActionBar,
    AuthoringConfirmDialog,
    AuthoringEmptyState,
    AuthoringFeedback,
    AuthoringLoadingState,
    AuthoringStatusBadge,
    getAuthoringErrorFeedback,
} from '../../authoringPlatform'

export function CmsPagesList() {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const navigate = useNavigate()

    const [pages, setPages] = useState([])
    const [isLoading, setIsLoading] = useState(true)
    const [isSeeding, setIsSeeding] = useState(false)
    const [busyPageId, setBusyPageId] = useState(null)
    const [error, setError] = useState('')
    const [notice, setNotice] = useState('')
    const [confirmAction, setConfirmAction] = useState(null)
    const currentUser = useAuthStore((state) => state.user)

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
            setError(isRTL ? 'تعذر تحميل الصفحات.' : 'Unable to load pages.')
        } finally {
            setIsLoading(false)
        }
    }

    async function handleSeedDefaults() {
        setIsSeeding(true)
        setError('')
        setNotice('')
        try {
            await cmsService.seedDefaultPages()
            await loadPages()
            setNotice(isRTL ? 'تم إنشاء محتوى CMS الافتراضي.' : 'Default CMS content created successfully.')
        } catch (err) {
            console.error('Failed to seed CMS pages', err)
            const feedback = getAuthoringErrorFeedback(err)
            setError(`${feedback.title}: ${feedback.message}`)
        } finally {
            setIsSeeding(false)
        }
    }

    async function handleDelete(page) {
        setBusyPageId(page.id)
        setError('')
        setNotice('')
        try {
            await cmsService.deletePage(page.id, page.draftVersion ?? page.publishedVersion)
            await loadPages()
            setNotice(isRTL ? 'تم حذف الصفحة.' : 'Page deleted successfully.')
        } catch (err) {
            console.error('Failed to delete CMS page', err)
            const feedback = getAuthoringErrorFeedback(err)
            setError(`${feedback.title}: ${feedback.message}`)
        } finally {
            setBusyPageId(null)
        }
    }

    function renderStatus(page) {
        return (
            <AuthoringStatusBadge state={page.status === 'Published' ? 'published' : page.status === 'UnpublishedChanges' ? 'dirty' : 'draft'}>
                {page.status === 'UnpublishedChanges'
                    ? (isRTL ? 'مسودة جاهزة للنشر' : 'Draft ready to publish')
                    : page.status === 'Published'
                        ? (isRTL ? 'منشور' : 'Published')
                        : (isRTL ? 'مسودة' : 'Draft')}
            </AuthoringStatusBadge>
        )
    }

    return (
        <div className="space-y-6">
            <AuthoringActionBar
                title="Pages"
                subtitle="Open a Page to preview its saved draft, view Page history, save, publish, or roll back. The list never publishes content."
                backAction={{ label: 'Back', onClick: () => navigate('/storefront') }}
                status={<AuthoringStatusBadge state="draft">{pages.length} Pages</AuthoringStatusBadge>}
                leading={<div className="flex flex-wrap gap-3">
                    <button
                        onClick={() => setConfirmAction({ type: 'seed', title: 'Create Default Pages?', description: 'This creates the default CMS Page drafts. Nothing is published.' })}
                        disabled={isSeeding}
                        className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-60"
                    >
                        {isSeeding ? <Loader2 size={16} className="animate-spin" /> : <FilePlus2 size={16} />}
                        {isRTL ? 'إضافة الصفحات الافتراضية' : 'Seed Default Pages'}
                    </button>
                    <button
                        onClick={() => navigate('/storefront/content/pages/new')}
                        className="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-slate-800"
                    >
                        <FilePlus2 size={16} />
                        {isRTL ? 'صفحة جديدة' : 'New Page'}
                    </button>
                </div>}
            />

            {error ? <AuthoringFeedback kind="error" title="Pages action failed" message={error} action={{ label: 'Retry', onClick: loadPages }} /> : null}
            {notice ? <AuthoringFeedback kind="success" title="Pages updated" message={notice} /> : null}

            {isLoading ? (
                <AuthoringLoadingState label="Loading Pages…" />
            ) : pages.length === 0 ? (
                <AuthoringEmptyState title="No Pages" description="Create a custom Page draft or seed the default Page drafts. Nothing is published from this list." action={{ label: 'Create Page', onClick: () => navigate('/storefront/content/pages/new') }} />
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
                                            onClick={() => navigate(`/storefront/content/pages/${page.id}`)}
                                            className="rounded-xl border border-slate-200 p-2 text-slate-600 transition hover:border-slate-300 hover:bg-slate-50"
                                            title={isRTL ? 'تعديل' : 'Edit'}
                                        >
                                            <PencilLine size={16} />
                                        </button>
                                        <button
                                            onClick={() => setConfirmAction({ type: 'delete', page, title: 'Delete Page?', description: `Delete "${page.title}" from the current Page workspace?` })}
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
            <AuthoringConfirmDialog
                open={confirmAction != null}
                title={confirmAction?.title}
                description={confirmAction?.description}
                confirmLabel={confirmAction?.type === 'delete' ? 'Delete Page' : 'Create Default Pages'}
                confirmTone={confirmAction?.type === 'delete' ? 'danger' : 'primary'}
                busy={isSeeding || Boolean(busyPageId)}
                onClose={() => setConfirmAction(null)}
                onConfirm={async () => {
                    const action = confirmAction
                    if (!action) return
                    if (action.type === 'delete') await handleDelete(action.page)
                    else await handleSeedDefaults()
                    setConfirmAction(null)
                }}
            />
        </div>
    )
}

export default CmsPagesList
