import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

const confirmToneClasses = {
    primary: 'bg-primary-600 hover:bg-primary-700',
    danger: 'bg-red-600 hover:bg-red-700',
    warning: 'bg-amber-600 hover:bg-amber-700',
    info: 'bg-sky-600 hover:bg-sky-700',
}

const tonePanelClasses = {
    primary: 'bg-primary-50 text-primary-700',
    danger: 'bg-red-50 text-red-700',
    warning: 'bg-amber-50 text-amber-700',
    info: 'bg-sky-50 text-sky-700',
}

const toneIcons = {
    primary: '\u2713',
    danger: '\u26A0\uFE0F',
    warning: '\u26A0\uFE0F',
    info: '\u2139\uFE0F',
}

export default function ConfirmModal({
    title,
    description,
    confirmLabel,
    cancelLabel,
    onConfirm,
    onCancel,
    children,
    confirmTone = 'primary',
    confirmDisabled = false,
    cancelDisabled = false,
    disableEscapeKeyDown = false,
    hideCloseButton = false,
    loading: loadingProp,
    variant,
}) {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const modalRef = useRef(null)
    const previousFocusRef = useRef(null)
    const confirmButtonRef = useRef(null)
    const [internalLoading, setInternalLoading] = useState(false)
    const [modalId] = useState(() => `confirm-modal-${Math.random().toString(36).substr(2, 9)}`)
    const titleId = `${modalId}-title`
    const descriptionId = description ? `${modalId}-description` : undefined
    const tone = variant || confirmTone
    const isLoading = typeof loadingProp === 'boolean' ? loadingProp : internalLoading

    const handleConfirm = async () => {
        if (!onConfirm || isLoading) {
            return
        }

        const result = onConfirm()

        if (!result || typeof result.then !== 'function' || typeof loadingProp === 'boolean') {
            return
        }

        try {
            setInternalLoading(true)
            await result
        } finally {
            setInternalLoading(false)
        }
    }

    // Focus management and focus trapping
    useEffect(() => {
        // Save previous focus
        previousFocusRef.current = document.activeElement

        // Set initial focus to confirm button
        if (confirmButtonRef.current) {
            confirmButtonRef.current.focus()
        }

        // Focus trap: prevent tab from leaving the modal
        const handleKeyDown = (event) => {
            if (event.key === 'Tab' && modalRef.current) {
                const focusableElements = modalRef.current.querySelectorAll(
                    'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
                )
                const firstElement = focusableElements[0]
                const lastElement = focusableElements[focusableElements.length - 1]

                if (event.shiftKey) {
                    // Shift+Tab
                    if (document.activeElement === firstElement) {
                        event.preventDefault()
                        lastElement?.focus()
                    }
                } else {
                    // Tab
                    if (document.activeElement === lastElement) {
                        event.preventDefault()
                        firstElement?.focus()
                    }
                }
            }

            if (event.key === 'Escape' && !disableEscapeKeyDown && !isLoading && !cancelDisabled) {
                onCancel()
            }
        }

        window.addEventListener('keydown', handleKeyDown)
        return () => {
            window.removeEventListener('keydown', handleKeyDown)
            // Restore focus
            if (previousFocusRef.current?.focus) {
                previousFocusRef.current.focus()
            }
        }
    }, [cancelDisabled, disableEscapeKeyDown, isLoading, onCancel])

    return (
        <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black bg-opacity-50 p-4" dir={isRTL ? 'rtl' : 'ltr'}>
            <div
                ref={modalRef}
                role="dialog"
                aria-modal="true"
                aria-labelledby={titleId}
                aria-describedby={descriptionId}
                className="w-full max-w-md rounded-lg bg-white shadow-xl"
            >
                <div className="p-6">
                    <div className={`mb-4 flex items-start justify-between gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <div className={`flex items-start gap-3 ${isRTL ? 'flex-row-reverse text-right' : 'text-left'}`}>
                            <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-lg ${tonePanelClasses[tone] || tonePanelClasses.primary}`}>
                                {toneIcons[tone] || toneIcons.primary}
                            </div>
                            <div>
                                <h2 id={titleId} className="text-xl font-semibold text-gray-900">{title}</h2>
                                {description ? (
                                    <p id={descriptionId} className="mt-2 text-sm text-gray-600">{description}</p>
                                ) : null}
                            </div>
                        </div>
                        {hideCloseButton ? null : (
                            <button
                                type="button"
                                onClick={onCancel}
                                disabled={isLoading || cancelDisabled}
                                aria-label={t('common.close')}
                                className="text-gray-400 hover:text-gray-600 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                                </svg>
                            </button>
                        )}
                    </div>

                    {children ? (
                        <div className="mb-6">{children}</div>
                    ) : null}

                    <div className={`flex gap-3 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <button
                            type="button"
                            onClick={onCancel}
                            disabled={isLoading || cancelDisabled}
                            className="flex-1 rounded-lg border border-gray-300 px-4 py-3 font-medium text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            {cancelLabel || t('common.cancel')}
                        </button>
                        <button
                            ref={confirmButtonRef}
                            type="button"
                            onClick={handleConfirm}
                            disabled={isLoading || confirmDisabled}
                            className={`flex-1 rounded-lg px-4 py-3 font-medium text-white disabled:cursor-not-allowed disabled:bg-gray-400 ${confirmToneClasses[tone] || confirmToneClasses.primary}`}
                        >
                            {isLoading ? t('common.loading') : confirmLabel}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    )
}
