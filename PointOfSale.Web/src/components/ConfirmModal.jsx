import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { normalizeDocumentLanguage } from '../i18n/documentLanguage'

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
    const isRTL = normalizeDocumentLanguage(i18n.resolvedLanguage || i18n.language) === 'ar'
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

    // Latest handler/state values for the stable keydown listener, so focus
    // management never re-runs (and never re-captures or restores focus) when
    // an awaited action merely flips the loading state.
    const dialogStateRef = useRef({ onCancel, disableEscapeKeyDown, cancelDisabled, isLoading })
    dialogStateRef.current = { onCancel, disableEscapeKeyDown, cancelDisabled, isLoading }

    // Focus capture, trap and restoration run exactly once per mounted
    // dialog. Loading transitions must keep deterministic in-dialog focus
    // until settlement instead of bouncing focus to the opener and back.
    useEffect(() => {
        previousFocusRef.current = document.activeElement

        if (confirmButtonRef.current) {
            confirmButtonRef.current.focus()
        }

        const getDialogFocusableElements = () => (
            Array.from(modalRef.current?.querySelectorAll(
                'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"]):not([disabled])',
            ) ?? [])
        )

        const handleKeyDown = (event) => {
            const { onCancel, disableEscapeKeyDown, cancelDisabled, isLoading } = dialogStateRef.current

            if (event.key === 'Tab' && modalRef.current) {
                const focusableElements = getDialogFocusableElements()
                const activeElement = document.activeElement
                const focusIsInsideDialog = activeElement instanceof Node
                    && modalRef.current.contains(activeElement)

                if (focusableElements.length === 0) {
                    // Every control is disabled (awaited action pending):
                    // pin focus to the dialog itself instead of letting Tab
                    // leave it for content behind the overlay.
                    event.preventDefault()
                    modalRef.current?.focus()
                    return
                }

                const firstElement = focusableElements[0]
                const lastElement = focusableElements[focusableElements.length - 1]

                if (!focusIsInsideDialog) {
                    // Stray Tab with focus outside the overlay: pull it back
                    // into the dialog at the natural entry point.
                    event.preventDefault()
                    if (event.shiftKey) {
                        lastElement.focus()
                    } else {
                        firstElement.focus()
                    }
                    return
                }

                if (event.shiftKey) {
                    if (activeElement === firstElement) {
                        event.preventDefault()
                        lastElement.focus()
                    }
                } else if (activeElement === lastElement) {
                    event.preventDefault()
                    firstElement.focus()
                }
            }

            if (event.key === 'Escape' && !disableEscapeKeyDown && !isLoading && !cancelDisabled) {
                onCancel()
            }
        }

        window.addEventListener('keydown', handleKeyDown)
        return () => {
            window.removeEventListener('keydown', handleKeyDown)
            if (previousFocusRef.current?.focus) {
                previousFocusRef.current.focus()
            }
        }
    }, [])

    // A focused control that becomes disabled drops real-browser focus to
    // <body>. While a confirmed async action is pending (all controls
    // disabled), park focus on the dialog container so it stays inside and
    // deterministic instead of silently escaping behind the overlay.
    useEffect(() => {
        if (!isLoading) {
            return
        }
        const modal = modalRef.current
        if (modal && !(document.activeElement instanceof Node && modal.contains(document.activeElement))) {
            modal.focus()
        }
    }, [isLoading])

    return (
        <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black bg-opacity-50 p-4" dir={isRTL ? 'rtl' : 'ltr'}>
            <div
                ref={modalRef}
                role="dialog"
                aria-modal="true"
                aria-labelledby={titleId}
                aria-describedby={descriptionId}
                tabIndex={-1}
                className="max-h-[calc(100vh-2rem)] w-full max-w-md overflow-y-auto rounded-lg bg-white shadow-xl focus:outline-none"
            >
                <div className="p-6">
                    <div className="mb-4 flex items-start justify-between gap-4">
                        <div className="flex min-w-0 items-start gap-3 text-start">
                            <div
                                aria-hidden="true"
                                className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-lg ${tonePanelClasses[tone] || tonePanelClasses.primary}`}
                            >
                                {toneIcons[tone] || toneIcons.primary}
                            </div>
                            <div className="min-w-0">
                                <h2 id={titleId} className="break-words text-xl font-semibold text-gray-900">{title}</h2>
                                {description ? (
                                    <p id={descriptionId} className="mt-2 break-words text-sm text-gray-600">{description}</p>
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
                                <svg aria-hidden="true" className="h-5 w-5" fill="none" focusable="false" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                                </svg>
                            </button>
                        )}
                    </div>

                    {children ? (
                        <div className="mb-6">{children}</div>
                    ) : null}

                    <div className="flex gap-3">
                        <button
                            type="button"
                            onClick={onCancel}
                            disabled={isLoading || cancelDisabled}
                            className="min-w-0 flex-1 break-words rounded-lg border border-gray-300 px-4 py-3 font-medium text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            {cancelLabel || t('common.cancel')}
                        </button>
                        <button
                            ref={confirmButtonRef}
                            type="button"
                            onClick={handleConfirm}
                            disabled={isLoading || confirmDisabled}
                            className={`min-w-0 flex-1 break-words rounded-lg px-4 py-3 font-medium text-white disabled:cursor-not-allowed disabled:bg-gray-400 ${confirmToneClasses[tone] || confirmToneClasses.primary}`}
                        >
                            {isLoading ? t('common.loading') : confirmLabel}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    )
}
