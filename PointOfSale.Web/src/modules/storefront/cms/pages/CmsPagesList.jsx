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
    const { t, i18n } = useTranslation()
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
            setError(t('storefront.cms.pageList.loadFailed'))
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
            setNotice(t('storefront.cms.pageList.defaultsCreated'))
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
            setNotice(t('storefront.cms.pageList.deleted'))
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
                    ? t('storefront.cms.pageList.status.ready')
                    : page.status === 'Published'
                        ? t('storefront.cms.published')
                        : t('storefront.cms.draft')}
            </AuthoringStatusBadge>
        )
    }

    return (
        <div className="space-y-6">
            <AuthoringActionBar
                title={t('storefront.cms.pageList.title')}
                subtitle={t('storefront.cms.pageList.subtitle')}
                backAction={{ label: t('storefront.cms.pageList.back'), onClick: () => navigate('/storefront') }}
                status={<AuthoringStatusBadge state="draft">{t('storefront.cms.pageList.count', { count: pages.length })}</AuthoringStatusBadge>}
                leading={<div className="flex flex-wrap gap-3">
                    <button
                        onClick={() => setConfirmAction({ type: 'seed', title: t('storefront.cms.pageList.seedConfirmTitle'), description: t('storefront.cms.pageList.seedConfirmDescription') })}
                        disabled={isSeeding}
                        className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-60"
                    >
                        {isSeeding ? <Loader2 size={16} className="animate-spin" /> : <FilePlus2 size={16} />}
                        {t('storefront.cms.seedDefaults')}
                    </button>
                    <button
                        onClick={() => navigate('/storefront/content/pages/new')}
                        className="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-slate-800"
                    >
                        <FilePlus2 size={16} />
                        {t('storefront.cms.pageList.create')}
                    </button>
                </div>}
            />

            {error ? <AuthoringFeedback kind="error" title={t('storefront.cms.pageList.actionFailed')} message={error} action={{ label: t('storefront.cms.pageList.retry'), onClick: loadPages }} /> : null}
            {notice ? <AuthoringFeedback kind="success" title={t('storefront.cms.pageList.updated')} message={notice} /> : null}

            {isLoading ? (
                <AuthoringLoadingState label={t('storefront.cms.pageList.loading')} />
            ) : pages.length === 0 ? (
                <AuthoringEmptyState title={t('storefront.cms.pageList.emptyTitle')} description={t('storefront.cms.pageList.emptyDescription')} action={{ label: t('storefront.cms.pageList.create'), onClick: () => navigate('/storefront/content/pages/new') }} />
            ) : (
                <div className="overflow-hidden rounded-[28px] border border-slate-200 bg-white shadow-sm">
                    <div className="hidden grid-cols-[1.3fr_1fr_0.9fr_0.9fr_0.9fr] gap-4 border-b border-slate-200 px-6 py-4 text-xs font-semibold uppercase tracking-[0.18em] text-slate-400 md:grid">
                        <span>{t('storefront.cms.pageList.columns.page')}</span>
                        <span>{t('storefront.cms.pageList.columns.mode')}</span>
                        <span>{t('storefront.cms.pageList.columns.status')}</span>
                        <span>{t('storefront.cms.pageList.columns.updated')}</span>
                        <span className="text-end">{t('storefront.cms.pageList.columns.actions')}</span>
                    </div>

                    <div className="divide-y divide-slate-100">
                        {pages.map((page) => {
                            const isBusy = busyPageId === page.id

                            return (
                                <div
                                    key={page.id}
                                    className="grid grid-cols-1 gap-3 px-4 py-5 md:grid-cols-[1.3fr_1fr_0.9fr_0.9fr_0.9fr] md:gap-4 md:px-6"
                                >
                                    <div>
                                        <div className="font-medium text-slate-900">{page.title}</div>
                                        <div className="mt-1 text-sm text-slate-500">/{page.slug}</div>
                                    </div>

                                    <div className="text-sm text-slate-600">
                                        {page.contentType === 'Blocks'
                                            ? t('storefront.cms.pageList.blocks')
                                            : t('storefront.cms.pageList.richText')}
                                    </div>

                                    <div>{renderStatus(page)}</div>

                                    <div className="text-sm text-slate-500">
                                        {page.updatedAtUtc ? new Date(page.updatedAtUtc).toLocaleString(isRTL ? 'ar' : 'en') : '-'}
                                    </div>

                                    <div className="flex justify-start gap-2 md:justify-end">
                                        <button
                                            onClick={() => navigate(`/storefront/content/pages/${page.id}`)}
                                            className="rounded-xl border border-slate-200 p-2 text-slate-600 transition hover:border-slate-300 hover:bg-slate-50"
                                            title={t('storefront.cms.pageList.edit')}
                                        >
                                            <PencilLine size={16} />
                                        </button>
                                        <button
                                            onClick={() => setConfirmAction({ type: 'delete', page, title: t('storefront.cms.pageList.deleteConfirmTitle'), description: t('storefront.cms.pageList.deleteConfirmDescription', { title: page.title }) })}
                                            disabled={isBusy}
                                            className="rounded-xl border border-rose-200 p-2 text-rose-700 transition hover:bg-rose-50 disabled:opacity-60"
                                            title={t('storefront.cms.pageList.delete')}
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
                confirmLabel={confirmAction?.type === 'delete' ? t('storefront.cms.pageList.deletePage') : t('storefront.cms.seedDefaults')}
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
