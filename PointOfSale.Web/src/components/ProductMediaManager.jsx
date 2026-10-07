import { useCallback, useEffect, useMemo, useState } from 'react'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import api from '../services/api'
import MediaGalleryGrid from './MediaGalleryGrid'
import MediaUploadButton from './MediaUploadButton'
import { useToast } from './Toast'

const ALLOWED_IMAGE_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp'])
const ALLOWED_VIDEO_TYPES = new Set(['video/mp4'])
const MAX_IMAGE_BYTES = 10 * 1024 * 1024
const MAX_VIDEO_BYTES = 100 * 1024 * 1024
const FILE_EXTENSIONS = {
    'image/jpeg': ['jpg', 'jpeg'],
    'image/png': ['png'],
    'image/webp': ['webp'],
    'video/mp4': ['mp4']
}

function sortMedia(items) {
    return [...items].sort((left, right) => {
        if (left.position !== right.position) {
            return left.position - right.position
        }

        return new Date(left.createdAtUtc || 0).getTime() - new Date(right.createdAtUtc || 0).getTime()
    })
}

function extractYouTubeId(url) {
    if (!url) {
        return null
    }

    try {
        const parsedUrl = new URL(url)

        if (parsedUrl.hostname.includes('youtu.be')) {
            return parsedUrl.pathname.replace('/', '') || null
        }

        if (parsedUrl.searchParams.has('v')) {
            return parsedUrl.searchParams.get('v')
        }

        const segments = parsedUrl.pathname.split('/').filter(Boolean)
        const embedIndex = segments.findIndex(segment => segment === 'embed' || segment === 'shorts')

        if (embedIndex >= 0 && segments[embedIndex + 1]) {
            return segments[embedIndex + 1]
        }
    } catch {
        return null
    }

    return null
}

function getYouTubeEmbedUrl(url) {
    const videoId = extractYouTubeId(url)
    return videoId ? `https://www.youtube.com/embed/${videoId}` : null
}

function getErrorMessage(error, fallback) {
    if (error?.response?.status === 401) {
        return 'Your session expired or this account is not allowed to manage product media. Please sign in again.'
    }

    const data = error?.response?.data
    const details = Object.values(data?.details || data?.data || {}).flat().filter(value => typeof value === 'string').join(' ')
    return (data?.message === 'The request payload is invalid.' ? details : null)
        || data?.error
        || data?.message
        || details
        || fallback
}

function YouTubeUrlPanel({
    youtubeUrl,
    setYoutubeUrl,
    submittingVideoUrl,
    savingOrder,
    onSubmit,
    helperText
}) {
    const handleKeyDown = (event) => {
        if (event.key !== 'Enter') {
            return
        }

        event.preventDefault()

        if (!youtubeUrl.trim() || submittingVideoUrl || savingOrder) {
            return
        }

        onSubmit()
    }

    return (
        <div className="rounded-2xl border border-gray-200 bg-white p-4 shadow-sm">
            <label className="label">YouTube URL</label>
            <input
                type="url"
                value={youtubeUrl}
                onChange={(event) => setYoutubeUrl(event.target.value)}
                onKeyDown={handleKeyDown}
                className="input"
                placeholder="https://youtube.com/watch?v=..."
            />
            <p className="mt-2 text-xs text-gray-500">
                {helperText}
            </p>
            <button
                type="button"
                onClick={onSubmit}
                disabled={submittingVideoUrl || savingOrder || !youtubeUrl.trim()}
                className="mt-4 inline-flex items-center rounded-lg bg-gray-900 px-4 py-2 text-sm font-semibold text-white transition hover:bg-black disabled:cursor-not-allowed disabled:opacity-60"
            >
                {submittingVideoUrl ? 'Adding video...' : 'Add Video URL'}
            </button>
        </div>
    )
}

function MediaPreviewModal({ item, onClose }) {
    if (!item) {
        return null
    }

    const isYouTube = item.mediaType === 'video' && item.provider === 'youtube'
    const embedUrl = isYouTube ? getYouTubeEmbedUrl(item.url) : null

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4">
            <div className="relative w-full max-w-5xl overflow-hidden rounded-3xl bg-white shadow-2xl">
                <button
                    type="button"
                    onClick={onClose}
                    className="absolute right-4 top-4 z-10 rounded-full bg-white/90 p-2 text-gray-600 shadow transition hover:bg-white hover:text-gray-900"
                >
                    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                    </svg>
                </button>

                <div className="bg-gray-950">
                    {item.mediaType === 'image' ? (
                        <img src={item.url} alt="Product media preview" className="max-h-[75vh] w-full object-contain" />
                    ) : isYouTube && embedUrl ? (
                        <div className="aspect-video w-full">
                            <iframe
                                src={embedUrl}
                                title="YouTube video preview"
                                allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                                allowFullScreen
                                className="h-full w-full border-0"
                            />
                        </div>
                    ) : (
                        <video src={item.url} controls className="max-h-[75vh] w-full bg-black" />
                    )}
                </div>

                <div className="border-t border-gray-200 px-6 py-4">
                    <p className="text-sm font-semibold text-gray-900">{item.mediaType === 'image' ? 'Image' : 'Video'} Preview</p>
                    <p className="mt-1 truncate text-xs text-gray-500">{item.url}</p>
                </div>
            </div>
        </div>
    )
}

export default function ProductMediaManager({ productId, onMediaChanged }) {
    const { confirm } = useConfirmDialog()
    const { showError, showSuccess } = useToast()
    const [media, setMedia] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState('')
    const [uploading, setUploading] = useState(false)
    const [savingOrder, setSavingOrder] = useState(false)
    const [submittingVideoUrl, setSubmittingVideoUrl] = useState(false)
    const [youtubeUrl, setYoutubeUrl] = useState('')
    const [previewItem, setPreviewItem] = useState(null)

    const loadMedia = useCallback(async (showSpinner = true) => {
        if (!productId) {
            setMedia([])
            setLoading(false)
            return
        }

        if (showSpinner) {
            setLoading(true)
        }

        try {
            const response = await api.get(`/admin/products/${productId}/media`)
            setMedia(sortMedia(response.data || []))
            setError('')
        } catch (err) {
            const message = getErrorMessage(err, 'Failed to load product media.')
            setError(message)
        } finally {
            if (showSpinner) {
                setLoading(false)
            }
        }
    }, [productId])

    useEffect(() => {
        loadMedia()
    }, [loadMedia])

    const hasMedia = media.length > 0
    const primaryMediaId = useMemo(
        () => sortMedia(media).find(item => item.mediaType === 'image')?.id ?? null,
        [media]
    )

    const handleFilesSelected = useCallback(async (selectedFiles) => {
        if (!productId || !selectedFiles.length) {
            return
        }

        for (const file of selectedFiles) {
            if (!ALLOWED_IMAGE_TYPES.has(file.type) && !ALLOWED_VIDEO_TYPES.has(file.type)) {
                const message = 'Only JPG, PNG, WEBP, and MP4 files are supported.'
                setError(message)
                showError(message)
                return
            }
            const extension = file.name.split('.').pop()?.toLowerCase()
            if (!FILE_EXTENSIONS[file.type]?.includes(extension)) {
                const message = 'File extension does not match its image or video type.'
                setError(message)
                showError(message)
                return
            }
            if (file.size > (ALLOWED_VIDEO_TYPES.has(file.type) ? MAX_VIDEO_BYTES : MAX_IMAGE_BYTES)) {
                const message = ALLOWED_VIDEO_TYPES.has(file.type)
                    ? 'Video size exceeds 100MB limit.' : 'Image size exceeds 10MB limit.'
                setError(message)
                showError(message)
                return
            }
        }

        setUploading(true)
        setError('')

        try {
            for (const file of selectedFiles) {
                const formData = new FormData()
                formData.append('file', file)
                formData.append('entityType', 'product')
                formData.append('entityId', productId)

                const uploadResponse = await api.post('/upload', formData, {
                    headers: {
                        'Content-Type': 'multipart/form-data'
                    }
                })

                const isVideo = (uploadResponse.data?.contentType || file.type || '').startsWith('video/')
                const registerResponse = await api.post(`/admin/products/${productId}/media`, {
                    url: uploadResponse.data?.url,
                    mediaType: isVideo ? 'video' : 'image',
                    provider: isVideo ? 'mp4' : null
                }, {
                    idempotencyKey: crypto.randomUUID()
                })

                setMedia(current => sortMedia([...current, registerResponse.data]))
                onMediaChanged?.(registerResponse.headers)
            }

            showSuccess(selectedFiles.length === 1 ? 'Media uploaded successfully.' : `${selectedFiles.length} media files uploaded successfully.`)
        } catch (err) {
            const message = getErrorMessage(err, 'Failed to upload media.')
            setError(message)
            showError(message)
            await loadMedia(false)
        } finally {
            setUploading(false)
        }
    }, [productId, showError, showSuccess, loadMedia, onMediaChanged])

    const handleDelete = useCallback(async (mediaId) => {
        const confirmed = await confirm({
            confirmLabel: 'Delete',
            description: 'Delete this media item?',
            title: 'Warning',
            variant: 'danger',
        })

        if (!confirmed) {
            return
        }

        const previousMedia = media

        setMedia(current => sortMedia(
            current
                .filter(item => item.id !== mediaId)
                .map((item, index) => ({ ...item, position: index + 1 }))
        ))

        try {
            const response = await api.delete(`/admin/products/${productId}/media/${mediaId}`, { idempotencyKey: crypto.randomUUID() })
            onMediaChanged?.(response.headers)
            showSuccess('Media deleted successfully.')
        } catch (err) {
            setMedia(previousMedia)
            const message = getErrorMessage(err, 'Failed to delete media.')
            setError(message)
            showError(message)
        }
    }, [confirm, media, productId, showError, showSuccess, onMediaChanged])

    const handleReorder = useCallback(async (orderedMedia) => {
        const previousMedia = media
        const normalizedMedia = orderedMedia.map((item, index) => ({
            ...item,
            position: index + 1
        }))

        setMedia(normalizedMedia)
        setSavingOrder(true)

        try {
            const response = await api.put(`/admin/products/${productId}/media/reorder`, {
                mediaIds: normalizedMedia.map(item => item.id)
            }, { idempotencyKey: crypto.randomUUID() })

            setMedia(sortMedia(response.data || normalizedMedia))
            onMediaChanged?.(response.headers)
            return true
        } catch (err) {
            setMedia(previousMedia)
            const message = getErrorMessage(err, 'Failed to reorder media.')
            setError(message)
            showError(message)
            return false
        } finally {
            setSavingOrder(false)
        }
    }, [media, productId, showError, onMediaChanged])

    const handleSetPrimary = useCallback(async (mediaId) => {
        const itemIndex = media.findIndex(item => item.id === mediaId)

        if (itemIndex <= 0) {
            return
        }

        const nextMedia = [...media]
        const [selectedItem] = nextMedia.splice(itemIndex, 1)
        nextMedia.unshift(selectedItem)

        const success = await handleReorder(nextMedia)

        if (success) {
            showSuccess('Primary media updated.')
        }
    }, [handleReorder, media, showSuccess])

    const handleAddYouTubeVideo = async () => {
        if (!youtubeUrl.trim()) {
            return
        }

        setSubmittingVideoUrl(true)
        setError('')

        try {
            const response = await api.post(`/admin/products/${productId}/media`, {
                url: youtubeUrl.trim(),
                mediaType: 'video',
                provider: 'youtube'
            }, {
                idempotencyKey: crypto.randomUUID()
            })

            setMedia(current => sortMedia([...current, response.data]))
            onMediaChanged?.(response.headers)
            setYoutubeUrl('')
            showSuccess('Video added successfully.')
        } catch (err) {
            const message = getErrorMessage(err, 'Failed to add video URL.')
            setError(message)
            showError(message)
        } finally {
            setSubmittingVideoUrl(false)
        }
    }

    if (loading) {
        return (
            <div className="flex items-center justify-center py-12">
                <div className="h-8 w-8 animate-spin rounded-full border-4 border-primary-600 border-t-transparent" />
            </div>
        )
    }

    return (
        <div className="space-y-6">
            {error && (
                <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                    {error}
                </div>
            )}

            {hasMedia ? (
                <>
                    <div className="rounded-2xl border border-blue-100 bg-blue-50 px-4 py-4 text-sm text-blue-800">
                        Drag media cards to change gallery order. The first image in the list is treated as the primary storefront image.
                    </div>

                    <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_22rem]">
                        <MediaUploadButton
                            label="Upload Images"
                            description="JPG, PNG, and WEBP files are uploaded to Cloudflare R2."
                            accept="image/jpeg,image/png,image/webp"
                            multiple
                            busy={uploading}
                            disabled={savingOrder || submittingVideoUrl}
                            onFilesSelected={handleFilesSelected}
                        />

                        <MediaUploadButton
                            label="Upload MP4 Video"
                            description="Use this for hosted product videos that should live in the gallery."
                            accept="video/mp4"
                            multiple
                            busy={uploading}
                            disabled={savingOrder || submittingVideoUrl}
                            onFilesSelected={handleFilesSelected}
                        />

                        <YouTubeUrlPanel
                            youtubeUrl={youtubeUrl}
                            setYoutubeUrl={setYoutubeUrl}
                            submittingVideoUrl={submittingVideoUrl}
                            savingOrder={savingOrder}
                            onSubmit={handleAddYouTubeVideo}
                            helperText="Add hosted YouTube videos without uploading a file."
                        />
                    </div>

                    <MediaGalleryGrid
                        items={sortMedia(media)}
                        primaryMediaId={primaryMediaId}
                        disabled={uploading || savingOrder || submittingVideoUrl}
                        onReorder={handleReorder}
                        onDelete={handleDelete}
                        onSetPrimary={handleSetPrimary}
                        onPreview={setPreviewItem}
                    />
                </>
            ) : (
                <>
                    <div className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-4 text-sm text-amber-800">
                        This product does not have media yet. Upload images or videos below to create the gallery.
                    </div>

                    <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_22rem]">
                        <MediaUploadButton
                            label="Upload Images"
                            description="Create the gallery with one or more JPG, PNG, or WEBP files."
                            accept="image/jpeg,image/png,image/webp"
                            multiple
                            busy={uploading}
                            disabled={savingOrder || submittingVideoUrl}
                            onFilesSelected={handleFilesSelected}
                        />

                        <MediaUploadButton
                            label="Upload MP4 Video"
                            description="MP4 uploads are stored in R2 and added directly to the gallery."
                            accept="video/mp4"
                            multiple
                            busy={uploading}
                            disabled={savingOrder || submittingVideoUrl}
                            onFilesSelected={handleFilesSelected}
                        />

                        <YouTubeUrlPanel
                            youtubeUrl={youtubeUrl}
                            setYoutubeUrl={setYoutubeUrl}
                            submittingVideoUrl={submittingVideoUrl}
                            savingOrder={savingOrder}
                            onSubmit={handleAddYouTubeVideo}
                            helperText="Paste a product video URL to create the first gallery item without uploading a file."
                        />
                    </div>
                </>
            )}

            <MediaPreviewModal item={previewItem} onClose={() => setPreviewItem(null)} />
        </div>
    )
}
