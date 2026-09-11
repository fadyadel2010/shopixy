import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useAsyncLookup } from '../hooks/useAsyncLookup'

const defaultGetOptionValue = (option) => option?.id
const defaultGetOptionLabel = (option) => option?.name || ''

export default function AsyncEntitySelect({
    value,
    onChange,
    loadOptions,
    getOptionValue = defaultGetOptionValue,
    getOptionLabel = defaultGetOptionLabel,
    renderOption,
    placeholder,
    disabled = false,
    allowClear = true,
    emptyText,
    className = '',
}) {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language?.startsWith('ar')
    const containerRef = useRef(null)
    const [isOpen, setIsOpen] = useState(false)
    const [hasTypedSinceOpen, setHasTypedSinceOpen] = useState(false)

    const {
        query,
        setQuery,
        options,
        loading,
        error,
        hasMore,
        loadMore,
    } = useAsyncLookup({
        loadOptions,
        enabled: isOpen && !disabled,
        loadOnEmptyQuery: true,
        initialQuery: value ? getOptionLabel(value) : '',
    })

    useEffect(() => {
        if (!isOpen) {
            setQuery(value ? getOptionLabel(value) : '')
        }
    }, [value, isOpen, setQuery, getOptionLabel])

    useEffect(() => {
        if (!isOpen) {
            return
        }

        const handlePointerDown = (event) => {
            if (!containerRef.current?.contains(event.target)) {
                setIsOpen(false)
                setHasTypedSinceOpen(false)
            }
        }

        document.addEventListener('mousedown', handlePointerDown)
        return () => document.removeEventListener('mousedown', handlePointerDown)
    }, [isOpen])

    const selectedOptionId = useMemo(
        () => (value ? getOptionValue(value) : null),
        [value, getOptionValue],
    )

    const handleSelect = (option) => {
        onChange(option)
        setQuery(getOptionLabel(option))
        setIsOpen(false)
        setHasTypedSinceOpen(false)
    }

    const handleOpen = () => {
        setIsOpen(true)
        if (!hasTypedSinceOpen && value) {
            setQuery('')
        }
    }

    return (
        <div ref={containerRef} className={`relative ${className}`} dir={isRTL ? 'rtl' : 'ltr'}>
            <div className="relative">
                <input
                    type="text"
                    value={query}
                    onChange={(event) => {
                        setQuery(event.target.value)
                        setHasTypedSinceOpen(true)
                    }}
                    onFocus={handleOpen}
                    onClick={handleOpen}
                    onKeyDown={(event) => {
                        if (event.key === 'Escape') {
                            setIsOpen(false)
                            setHasTypedSinceOpen(false)
                        }
                    }}
                    placeholder={placeholder}
                    disabled={disabled}
                    className={`input w-full ${allowClear && value ? (isRTL ? 'pl-20 text-right' : 'pr-20 text-left') : (isRTL ? 'pl-10 text-right' : 'pr-10 text-left')}`}
                />
                <div className={`absolute inset-y-0 flex items-center gap-2 ${isRTL ? 'left-3' : 'right-3'}`}>
                    {allowClear && value ? (
                        <button
                            type="button"
                            onClick={() => {
                                onChange(null)
                                setQuery('')
                                setHasTypedSinceOpen(true)
                            }}
                            disabled={disabled}
                            aria-label={t('common.clear') || 'Clear'}
                            className="text-sm text-gray-400 hover:text-gray-600 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            x
                        </button>
                    ) : null}
                    <span className="text-xs text-gray-400">v</span>
                </div>
            </div>

            {isOpen && !disabled ? (
                <div className="absolute z-50 mt-1 max-h-72 w-full overflow-y-auto rounded-lg border border-gray-200 bg-white shadow-lg">
                    {loading && options.length === 0 ? (
                        <div className={`px-4 py-3 text-sm text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                            {t('common.loading')}
                        </div>
                    ) : null}

                    {!loading && options.length === 0 ? (
                        <div className={`px-4 py-3 text-sm text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                            {error || emptyText || t('common.noResults')}
                        </div>
                    ) : null}

                    {options.map((option) => {
                        const optionId = getOptionValue(option)
                        const isSelected = selectedOptionId != null && optionId === selectedOptionId

                        return (
                            <button
                                key={optionId}
                                type="button"
                                onClick={() => handleSelect(option)}
                                className={`w-full border-b border-gray-100 px-4 py-3 transition-colors last:border-b-0 hover:bg-gray-50 ${isSelected ? 'bg-primary-50' : 'bg-white'} ${isRTL ? 'text-right' : 'text-left'}`}
                            >
                                {renderOption ? renderOption(option) : (
                                    <div className="font-medium text-gray-900">
                                        {getOptionLabel(option)}
                                    </div>
                                )}
                            </button>
                        )
                    })}

                    {error && options.length > 0 ? (
                        <div role="alert" className={`border-t border-red-100 bg-red-50 px-4 py-3 text-sm text-red-700 ${isRTL ? 'text-right' : 'text-left'}`}>
                            {error}
                        </div>
                    ) : null}

                    {hasMore ? (
                        <button
                            type="button"
                            onClick={loadMore}
                            disabled={loading}
                            className="w-full border-t border-gray-200 px-4 py-3 text-sm font-medium text-primary-700 hover:bg-primary-50 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            {loading ? t('common.loading') : (t('common.loadMore') || 'Load more')}
                        </button>
                    ) : options.length > 0 && !loading && !error ? (
                        <div role="status" className={`border-t border-gray-100 px-4 py-2 text-xs text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                            {t('common.endOfResults')}
                        </div>
                    ) : null}
                </div>
            ) : null}
        </div>
    )
}
