import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { hasPosPermission } from '../auth/posPermissions'
import { useAuthStore } from '../store/authStore'
import { isPreviewableCheckoutImage } from './orderCheckoutImage'

import {
    downloadAdminOrderCheckoutAttachment,
    getAdminOrderCheckoutAttachmentBlob,
} from '../services/adminOrdersApi'

export default function OrderCheckoutAttachmentPreview({ orderId, fieldLabel, attachment }) {
    const { t } = useTranslation()
    const canDownload = useAuthStore((state) => hasPosPermission(state.user, 'storefront.orders.attachments.download'))
    const previewable = isPreviewableCheckoutImage(attachment)
    const [previewAttempt, setPreviewAttempt] = useState(0)
    const [preview, setPreview] = useState({ status: previewable ? 'loading' : 'not-applicable', url: null })
    const [downloadState, setDownloadState] = useState({ status: 'idle', message: null })

    useEffect(() => {
        if (!canDownload || !previewable) {
            setPreview({ status: 'not-applicable', url: null })
            return undefined
        }

        const controller = new AbortController()
        let active = true
        let objectUrl = null
        setPreview({ status: 'loading', url: null })

        getAdminOrderCheckoutAttachmentBlob(orderId, attachment.attachmentId, controller.signal)
            .then((blob) => {
                if (!active) return
                objectUrl = window.URL.createObjectURL(blob)
                setPreview({ status: 'available', url: objectUrl })
            })
            .catch((error) => {
                if (!active || error?.code === 'ERR_CANCELED') return
                setPreview({ status: 'error', url: null })
            })

        return () => {
            active = false
            controller.abort()
            if (objectUrl) window.URL.revokeObjectURL(objectUrl)
        }
    }, [attachment.attachmentId, canDownload, orderId, previewAttempt, previewable])

    const download = async () => {
        if (!canDownload) return
        setDownloadState({ status: 'loading', message: null })
        try {
            await downloadAdminOrderCheckoutAttachment(orderId, attachment.attachmentId, attachment.fileName)
            setDownloadState({ status: 'idle', message: null })
        } catch (error) {
            setDownloadState({
                status: 'error',
                message: error?.message || t('checkoutFields.downloadFailed', 'Unable to download this attachment right now.'),
            })
        }
    }

    if (!canDownload) return null

    return (
        <div className="mt-2 rounded-2xl border border-slate-200 bg-slate-50 p-3">
            {preview.status === 'loading' ? (
                <div className="flex min-h-32 items-center justify-center rounded-xl bg-white text-sm text-slate-500" role="status">
                    {t('checkoutFields.previewLoading', 'Loading secure image preview...')}
                </div>
            ) : null}
            {preview.status === 'available' ? (
                <img
                    src={preview.url}
                    alt={t('checkoutFields.previewAlt', '{{label}} attachment: {{fileName}}', { label: fieldLabel, fileName: attachment.fileName })}
                    className="max-h-64 w-full rounded-xl bg-white object-contain"
                    onError={() => setPreview({ status: 'error', url: null })}
                />
            ) : null}
            {preview.status === 'error' ? (
                <div className="rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900" role="alert">
                    <p>{t('checkoutFields.previewUnavailable', 'The secure image preview is unavailable. You can retry or download the file.')}</p>
                    <button
                        type="button"
                        onClick={() => setPreviewAttempt((attempt) => attempt + 1)}
                        className="mt-2 rounded-full border border-amber-300 bg-white px-3 py-1.5 font-semibold text-amber-900"
                    >
                        {t('checkoutFields.retryPreview', 'Retry preview')}
                    </button>
                </div>
            ) : null}

            <div className="mt-3 flex flex-wrap items-center gap-2 text-xs text-slate-500">
                <span>
                    {attachment.contentType || t('orders.management.details.file')}
                    {attachment.sizeBytes > 0 ? t('orders.management.details.fileSize', { size: attachment.sizeBytes }) : ''}
                </span>
                <button
                    type="button"
                    onClick={download}
                    disabled={downloadState.status === 'loading'}
                    className="rounded-full border border-slate-200 bg-white px-3 py-1 font-semibold text-slate-700 transition hover:border-slate-300 hover:text-slate-950 disabled:cursor-not-allowed disabled:opacity-60"
                >
                    {downloadState.status === 'loading'
                        ? t('checkoutFields.downloading', 'Downloading...')
                        : t('checkoutFields.viewDownload', 'View / Download')}
                </button>
            </div>
            {downloadState.status === 'error' ? (
                <p className="mt-2 text-xs text-rose-600" role="alert">{downloadState.message}</p>
            ) : null}
        </div>
    )
}
