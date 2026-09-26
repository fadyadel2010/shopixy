import { useTranslation } from 'react-i18next'
import { CuratedProductSelector } from './CuratedProductSelector'

function getLocalizedText(value, isRTL) {
    if (!value) {
        return ''
    }

    if (typeof value === 'string') {
        return value
    }

    return isRTL ? value.ar || value.en || '' : value.en || value.ar || ''
}

function cloneValue(value) {
    if (value === undefined) {
        return undefined
    }

    return JSON.parse(JSON.stringify(value))
}

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

function normalizeImageItem(item) {
    if (typeof item === 'string') {
        return { url: item }
    }

    if (item && typeof item === 'object') {
        return { ...item, url: item.url || '' }
    }

    return { url: '' }
}

function FieldLabel({ children }) {
    return (
        <label className="block text-sm font-medium text-gray-700 mb-1">
            {children}
        </label>
    )
}

function ListCard({ children }) {
    return (
        <div className="rounded-lg border border-gray-200 bg-gray-50 p-3">
            {children}
        </div>
    )
}

export function SchemaSettingsForm({ section, schema = [], onUpdate }) {
    const { i18n, t } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const settings = section.settings || {}

    const handleChange = (key, value) => {
        onUpdate({ [key]: value })
    }

    const renderField = (field, value, onValueChange, fieldKey) => {
        const label = t(`storefront.homepageEditor.fields.${field.key}`, { defaultValue: getLocalizedText(field.label, isRTL) })
        const helpText = getLocalizedText(field.helpText, isRTL)

        switch (field.type) {
            case 'textarea':
                return (
                    <div key={fieldKey}>
                        <FieldLabel>{label}</FieldLabel>
                        <textarea
                            value={value || ''}
                            rows={field.rows || 4}
                            onChange={(e) => onValueChange(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                        />
                        {helpText ? <p className="mt-1 text-xs text-gray-500">{helpText}</p> : null}
                    </div>
                )

            case 'color':
                return (
                    <div key={fieldKey}>
                        <FieldLabel>{label}</FieldLabel>
                        <input
                            type="color"
                            value={value || field.defaultValue || '#000000'}
                            onChange={(e) => onValueChange(e.target.value)}
                            className="w-full h-10 rounded-lg cursor-pointer"
                        />
                        {helpText ? <p className="mt-1 text-xs text-gray-500">{helpText}</p> : null}
                    </div>
                )

            case 'select':
                return (
                    <div key={fieldKey}>
                        <FieldLabel>{label}</FieldLabel>
                        <select
                            value={value ?? field.defaultValue ?? ''}
                            onChange={(e) => onValueChange(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                        >
                            {(field.options || []).map((option) => (
                                <option key={`${fieldKey}.${option.value}`} value={option.value}>
                                    {t(`storefront.homepageEditor.options.${option.value}`, { defaultValue: getLocalizedText(option.label, isRTL) })}
                                </option>
                            ))}
                        </select>
                        {helpText ? <p className="mt-1 text-xs text-gray-500">{helpText}</p> : null}
                    </div>
                )

            case 'number':
                return (
                    <div key={fieldKey}>
                        <FieldLabel>{label}</FieldLabel>
                        <input
                            type="number"
                            value={value ?? field.defaultValue ?? ''}
                            min={field.min}
                            max={field.max}
                            step={field.step || 1}
                            onChange={(e) => onValueChange(e.target.value === '' ? '' : Number(e.target.value))}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                        />
                        {helpText ? <p className="mt-1 text-xs text-gray-500">{helpText}</p> : null}
                    </div>
                )

            case 'toggle':
                return (
                    <div key={fieldKey}>
                        <label className="flex items-center gap-2">
                            <input
                                type="checkbox"
                                checked={Boolean(value ?? field.defaultValue)}
                                onChange={(e) => onValueChange(e.target.checked)}
                                className="w-4 h-4 text-indigo-600 rounded focus:ring-indigo-500"
                            />
                            <span className="text-sm text-gray-700">{label}</span>
                        </label>
                        {helpText ? <p className="mt-1 text-xs text-gray-500">{helpText}</p> : null}
                    </div>
                )

            case 'image':
                return (
                    <div key={fieldKey}>
                        <FieldLabel>{label}</FieldLabel>
                        <input
                            type="text"
                            value={value || ''}
                            placeholder={field.placeholder || 'https://example.com/image.jpg'}
                            onChange={(e) => onValueChange(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                        />
                        {value ? (
                            <div className="mt-3 rounded-lg border border-gray-200 bg-gray-50 p-2">
                                <img
                                    src={value}
                                    alt={label || field.key}
                                    className="h-24 w-full rounded object-cover"
                                />
                            </div>
                        ) : null}
                        {helpText ? <p className="mt-1 text-xs text-gray-500">{helpText}</p> : null}
                    </div>
                )

            case 'image-list': {
                const items = Array.isArray(value) ? value : []

                const updateItem = (index, nextUrl) => {
                    const nextItems = [...items]
                    const currentItem = nextItems[index]

                    nextItems[index] = typeof currentItem === 'string'
                        ? nextUrl
                        : { ...normalizeImageItem(currentItem), url: nextUrl }

                    onValueChange(nextItems)
                }

                return (
                    <div key={fieldKey}>
                        <div className="flex items-center justify-between mb-2">
                            <FieldLabel>{label}</FieldLabel>
                            <button
                                type="button"
                                onClick={() => onValueChange([...items, cloneValue(field.defaultItem || { url: '' })])}
                                className="text-xs font-medium text-indigo-600 hover:text-indigo-700"
                            >
                                {isRTL ? 'إضافة صورة' : 'Add Image'}
                            </button>
                        </div>

                        <div className="space-y-3">
                            {items.map((item, index) => {
                                const normalizedItem = normalizeImageItem(item)

                                return (
                                    <ListCard key={`${fieldKey}.${index}`}>
                                        <div className="flex items-start gap-2">
                                            <input
                                                type="text"
                                                value={normalizedItem.url}
                                                placeholder={field.placeholder || 'https://example.com/image.jpg'}
                                                onChange={(e) => updateItem(index, e.target.value)}
                                                className="flex-1 px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                                            />
                                            <button
                                                type="button"
                                                onClick={() => onValueChange(items.filter((_, itemIndex) => itemIndex !== index))}
                                                className="px-2.5 py-2 text-xs font-medium text-red-600 hover:text-red-700"
                                            >
                                                {isRTL ? 'حذف' : 'Remove'}
                                            </button>
                                        </div>

                                        {normalizedItem.url ? (
                                            <img
                                                src={normalizedItem.url}
                                                alt={`${label} ${index + 1}`}
                                                className="mt-3 h-24 w-full rounded object-cover"
                                            />
                                        ) : (
                                            <div className="mt-3 flex h-24 items-center justify-center rounded border border-dashed border-gray-300 bg-white text-xs text-gray-400">
                                                {isRTL ? 'لا توجد صورة' : 'No image selected'}
                                            </div>
                                        )}
                                    </ListCard>
                                )
                            })}
                        </div>

                        {helpText ? <p className="mt-1 text-xs text-gray-500">{helpText}</p> : null}
                    </div>
                )
            }

            case 'guid-list': {
                const items = Array.isArray(value) ? value : []

                if (field.key === 'productIds') {
                    if (settings.source !== 'curated') return null
                    return <div key={fieldKey}>
                        <CuratedProductSelector ids={items} onChange={onValueChange}
                            maxItems={settings.maxItems} limit={field.maxItems || 50} />
                    </div>
                }

                return (
                    <div key={fieldKey}>
                        <FieldLabel>{label}</FieldLabel>
                        <textarea
                            value={items.join('\n')}
                            rows={field.rows || 4}
                            placeholder={field.placeholder || 'One ID per line'}
                            onChange={(e) => onValueChange(
                                e.target.value
                                    .split(/\r?\n/)
                                    .map((item) => item.trim())
                                    .filter(Boolean)
                            )}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                        />
                        {helpText ? <p className="mt-1 text-xs text-gray-500">{helpText}</p> : null}
                    </div>
                )
            }

            case 'video': {
                const embedUrl = getYoutubeEmbedUrl(value)

                return (
                    <div key={fieldKey}>
                        <FieldLabel>{label}</FieldLabel>
                        <input
                            type="text"
                            value={value || ''}
                            placeholder={field.placeholder || 'https://www.youtube.com/watch?v=...'}
                            onChange={(e) => onValueChange(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                        />

                        {embedUrl ? (
                            <div className="mt-3 overflow-hidden rounded-lg border border-gray-200 bg-black">
                                <div className="aspect-video">
                                    <iframe
                                        src={embedUrl}
                                        title={label || field.key}
                                        className="h-full w-full"
                                        allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                                        allowFullScreen
                                    />
                                </div>
                            </div>
                        ) : value ? (
                            <div className="mt-3 overflow-hidden rounded-lg border border-gray-200 bg-black">
                                <video
                                    src={value}
                                    className="aspect-video h-full w-full"
                                    controls
                                    muted
                                    playsInline
                                />
                            </div>
                        ) : null}

                        {helpText ? <p className="mt-1 text-xs text-gray-500">{helpText}</p> : null}
                    </div>
                )
            }

            case 'list': {
                const items = Array.isArray(value) ? value : []
                const itemFields = field.fields || []

                return (
                    <div key={fieldKey}>
                        <div className="flex items-center justify-between mb-2">
                            <FieldLabel>{label}</FieldLabel>
                            <button
                                type="button"
                                disabled={typeof field.maxItems === 'number' && items.length >= field.maxItems}
                                onClick={() => onValueChange([...items, cloneValue(field.defaultItem || {})])}
                                className="text-xs font-medium text-indigo-600 hover:text-indigo-700 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                 {t(field.key === 'highlights' ? 'storefront.homepageEditor.addHighlight' : 'storefront.homepageEditor.addItem')}
                            </button>
                        </div>

                        <div className="space-y-3">
                            {items.map((item, index) => (
                                <ListCard key={`${fieldKey}.${index}`}>
                                    <div className="flex items-center justify-between">
                                        <p className="text-sm font-medium text-gray-700">
                                             {field.key === 'highlights' ? label : field.itemLabel || label} {index + 1}
                                        </p>
                                        <button
                                            type="button"
                                            onClick={() => onValueChange(items.filter((_, itemIndex) => itemIndex !== index))}
                                            className="text-xs font-medium text-red-600 hover:text-red-700"
                                        >
                                             {t('storefront.homepageEditor.remove')}
                                        </button>
                                    </div>

                                    <div className="mt-3 space-y-4">
                                        {itemFields.map((itemField) =>
                                            renderField(
                                                itemField,
                                                item?.[itemField.key],
                                                (nextItemValue) => {
                                                    const nextItems = [...items]
                                                    nextItems[index] = {
                                                        ...(item || {}),
                                                        [itemField.key]: nextItemValue,
                                                    }
                                                    onValueChange(nextItems)
                                                },
                                                `${fieldKey}.${index}.${itemField.key}`
                                            )
                                        )}
                                    </div>
                                </ListCard>
                            ))}
                        </div>

                        {helpText ? <p className="mt-1 text-xs text-gray-500">{helpText}</p> : null}
                    </div>
                )
            }

            case 'text':
            default:
                return (
                    <div key={fieldKey}>
                        <FieldLabel>{label}</FieldLabel>
                        <input
                            type="text"
                            value={value || ''}
                            placeholder={field.placeholder || ''}
                            onChange={(e) => onValueChange(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                        />
                        {helpText ? <p className="mt-1 text-xs text-gray-500">{helpText}</p> : null}
                    </div>
                )
        }
    }

    return (
        <div className="space-y-4">
            {schema.map((field) =>
                renderField(
                    field,
                    settings[field.key],
                    (nextValue) => handleChange(field.key, nextValue),
                    field.key
                )
            )}
        </div>
    )
}

export default SchemaSettingsForm
