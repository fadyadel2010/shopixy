/**
 * Video Block Section
 * Phase 13: Schema-driven media section
 */

import { useTranslation } from 'react-i18next'

function getYoutubeEmbedUrl(url, autoplay = false, showControls = true) {
    if (!url) {
        return null
    }

    try {
        const parsedUrl = new URL(url)
        let videoId = ''

        if (parsedUrl.hostname.includes('youtu.be')) {
            videoId = parsedUrl.pathname.replace('/', '')
        } else if (parsedUrl.hostname.includes('youtube.com')) {
            if (parsedUrl.pathname === '/watch') {
                videoId = parsedUrl.searchParams.get('v') || ''
            } else if (parsedUrl.pathname.startsWith('/shorts/')) {
                videoId = parsedUrl.pathname.split('/')[2] || ''
            } else if (parsedUrl.pathname.startsWith('/embed/')) {
                videoId = parsedUrl.pathname.split('/')[2] || ''
            }
        }

        if (!videoId) {
            return null
        }

        const embedUrl = new URL(`https://www.youtube.com/embed/${videoId}`)
        embedUrl.searchParams.set('rel', '0')
        embedUrl.searchParams.set('autoplay', autoplay ? '1' : '0')
        embedUrl.searchParams.set('controls', showControls ? '1' : '0')

        return embedUrl.toString()
    } catch {
        return null
    }
}

export const settingsSchema = [
    { key: 'videoUrl', type: 'video', label: 'Video URL' },
    { key: 'autoplay', type: 'toggle', label: 'Autoplay' },
    { key: 'showControls', type: 'toggle', label: 'Show Controls' },
]

export function VideoBlockEditor({ section, onUpdate }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const settings = section.settings

    const handleChange = (key, value) => {
        onUpdate({ [key]: value })
    }

    return (
        <div className="space-y-4">
            <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                    {isRTL ? 'رابط الفيديو' : 'Video URL'}
                </label>
                <input
                    type="text"
                    value={settings.videoUrl || ''}
                    onChange={(e) => handleChange('videoUrl', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                />
            </div>

            <label className="flex items-center gap-2">
                <input
                    type="checkbox"
                    checked={settings.autoplay ?? false}
                    onChange={(e) => handleChange('autoplay', e.target.checked)}
                    className="w-4 h-4 text-indigo-600 rounded focus:ring-indigo-500"
                />
                <span className="text-sm text-gray-700">{isRTL ? 'تشغيل تلقائي' : 'Autoplay'}</span>
            </label>

            <label className="flex items-center gap-2">
                <input
                    type="checkbox"
                    checked={settings.showControls ?? true}
                    onChange={(e) => handleChange('showControls', e.target.checked)}
                    className="w-4 h-4 text-indigo-600 rounded focus:ring-indigo-500"
                />
                <span className="text-sm text-gray-700">{isRTL ? 'إظهار أدوات التحكم' : 'Show Controls'}</span>
            </label>
        </div>
    )
}

export function VideoBlockPreview({ section }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const settings = section.settings
    const embedUrl = getYoutubeEmbedUrl(settings.videoUrl, settings.autoplay, settings.showControls !== false)

    return (
        <div className="px-6 py-10 bg-white">
            {embedUrl ? (
                <div className="overflow-hidden rounded-2xl border border-gray-200 shadow-sm">
                    <div className="aspect-video">
                        <iframe
                            src={embedUrl}
                            title="Video block"
                            className="h-full w-full"
                            allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                            allowFullScreen
                        />
                    </div>
                </div>
            ) : settings.videoUrl ? (
                <div className="overflow-hidden rounded-2xl border border-gray-200 shadow-sm bg-black">
                    <video
                        src={settings.videoUrl}
                        className="aspect-video h-full w-full"
                        autoPlay={settings.autoplay}
                        controls={settings.showControls !== false}
                        muted={settings.autoplay}
                        playsInline
                    />
                </div>
            ) : (
                <div className="rounded-2xl border border-dashed border-gray-300 bg-gray-50 px-6 py-14 text-center text-sm text-gray-500">
                    {isRTL ? 'أضف رابط فيديو لعرضه هنا' : 'Add a video URL to preview it here'}
                </div>
            )}
        </div>
    )
}
