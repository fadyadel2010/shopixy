import { useEffect, useState } from 'react'

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

function getYouTubeThumbnail(url) {
    const videoId = extractYouTubeId(url)
    return videoId ? `https://img.youtube.com/vi/${videoId}/hqdefault.jpg` : null
}

function getMediaLabel(item) {
    if (item.mediaType === 'video') {
        if (item.provider === 'youtube') {
            return 'YouTube Video'
        }

        if (item.provider === 'vimeo') {
            return 'Vimeo Video'
        }

        return 'Video'
    }

    return 'Image'
}

export default function MediaCard({
    item,
    isPrimary,
    isDragging = false,
    dragHandleProps,
    onDelete,
    onSetPrimary,
    onPreview,
    disabled = false
}) {
    const youtubeThumbnail = item.provider === 'youtube' ? getYouTubeThumbnail(item.url) : null
    const [imageFailed, setImageFailed] = useState(false)
    const [thumbnailFailed, setThumbnailFailed] = useState(false)

    useEffect(() => {
        setImageFailed(false)
        setThumbnailFailed(false)
    }, [item.id, item.url, item.provider, item.mediaType])

    return (
        <article className={`group relative overflow-hidden rounded-2xl border border-gray-200 bg-white shadow-sm transition ${isDragging ? 'scale-[1.02] shadow-xl ring-2 ring-primary-300' : 'hover:-translate-y-0.5 hover:shadow-md'}`}>
            <div className="relative aspect-[4/3] overflow-hidden bg-gray-100">
                {item.mediaType === 'image' ? (
                    imageFailed ? (
                        <div className="flex h-full w-full flex-col items-center justify-center bg-gradient-to-br from-slate-100 to-slate-200 px-4 text-center text-slate-500">
                            <svg className="h-10 w-10" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2 1.586-1.586a2 2 0 012.828 0L20 14m-6-8h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
                            </svg>
                            <span className="mt-3 text-sm font-medium">Image preview unavailable</span>
                        </div>
                    ) : (
                    <img
                        src={item.url}
                        alt={getMediaLabel(item)}
                        className="h-full w-full object-cover"
                        onError={() => setImageFailed(true)}
                    />
                    )
                ) : item.provider === 'youtube' ? (
                    <>
                        {youtubeThumbnail && !thumbnailFailed ? (
                            <img
                                src={youtubeThumbnail}
                                alt="YouTube thumbnail"
                                className="h-full w-full object-cover"
                                onError={() => setThumbnailFailed(true)}
                            />
                        ) : (
                            <div className="flex h-full w-full items-center justify-center bg-gradient-to-br from-red-500 to-rose-700 text-white">
                                <span className="text-sm font-semibold">YouTube</span>
                            </div>
                        )}

                        <div className="absolute inset-0 flex items-center justify-center bg-black/25">
                            <div className="rounded-full bg-white/90 p-3 text-red-600 shadow-sm">
                                <svg className="h-8 w-8" viewBox="0 0 24 24" fill="currentColor">
                                    <path d="M19.615 3.184C16.86 3 12 3 12 3s-4.86 0-7.615.184C1.403 3.38 1 5.293 1 8.5v7c0 3.207.403 5.12 3.385 5.316C7.14 21 12 21 12 21s4.86 0 7.615-.184C22.597 20.62 23 18.707 23 15.5v-7c0-3.207-.403-5.12-3.385-5.316zM10 15.5v-7l6 3.5-6 3.5z" />
                                </svg>
                            </div>
                        </div>
                    </>
                ) : (
                    <>
                        <video
                            src={item.url}
                            className="h-full w-full object-cover"
                            muted
                            playsInline
                            preload="metadata"
                        />
                        <div className="absolute inset-0 flex items-center justify-center bg-black/20">
                            <div className="rounded-full bg-white/90 p-3 text-gray-900 shadow-sm">
                                <svg className="h-8 w-8" viewBox="0 0 24 24" fill="currentColor">
                                    <path d="M8 5.14v14l11-7-11-7z" />
                                </svg>
                            </div>
                        </div>
                    </>
                )}

                <div className="absolute left-3 top-3 flex flex-wrap gap-2">
                    {isPrimary && (
                        <span className="rounded-full bg-primary-600 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.16em] text-white">
                            Primary
                        </span>
                    )}

                    <span className="rounded-full bg-black/70 px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.12em] text-white">
                        {item.mediaType}
                    </span>
                </div>

                <div className="absolute right-3 top-3">
                    <button
                        type="button"
                        className="rounded-full bg-white/90 p-2 text-gray-600 shadow-sm transition hover:bg-white hover:text-gray-900"
                        aria-label="Drag media"
                        {...dragHandleProps}
                    >
                        <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 6h.01M8 12h.01M8 18h.01M16 6h.01M16 12h.01M16 18h.01" />
                        </svg>
                    </button>
                </div>

                <div className="absolute inset-x-0 bottom-0 flex translate-y-full items-center justify-end gap-2 bg-gradient-to-t from-black/80 via-black/40 to-transparent px-3 pb-3 pt-8 opacity-0 transition duration-200 group-hover:translate-y-0 group-hover:opacity-100 group-focus-within:translate-y-0 group-focus-within:opacity-100">
                    <button
                        type="button"
                        onClick={() => onPreview(item)}
                        className="rounded-lg bg-white/90 px-3 py-2 text-xs font-semibold text-gray-800 transition hover:bg-white"
                    >
                        Preview
                    </button>

                    {item.mediaType === 'image' && !isPrimary && (
                        <button
                            type="button"
                            onClick={() => onSetPrimary(item.id)}
                            disabled={disabled}
                            className="rounded-lg bg-primary-600 px-3 py-2 text-xs font-semibold text-white transition hover:bg-primary-700 disabled:cursor-not-allowed disabled:opacity-60"
                        >
                            Set Primary
                        </button>
                    )}

                    <button
                        type="button"
                        onClick={() => onDelete(item.id)}
                        disabled={disabled}
                        className="rounded-lg bg-red-600 px-3 py-2 text-xs font-semibold text-white transition hover:bg-red-700 disabled:cursor-not-allowed disabled:opacity-60"
                    >
                        Delete
                    </button>
                </div>
            </div>

            <div className="space-y-1 px-4 py-3">
                <div className="flex items-center justify-between gap-3">
                    <p className="truncate text-sm font-semibold text-gray-900">{getMediaLabel(item)}</p>
                    <span className="text-xs text-gray-400">#{item.position}</span>
                </div>

                <p className="truncate text-xs text-gray-500">{item.url}</p>
            </div>
        </article>
    )
}
