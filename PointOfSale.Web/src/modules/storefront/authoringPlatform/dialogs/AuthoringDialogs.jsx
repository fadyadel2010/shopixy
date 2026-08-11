import { useEffect, useId, useRef } from 'react'
import { createPortal } from 'react-dom'
import { Loader2 } from 'lucide-react'

import { getAuthoringFocusableElements } from '../accessibility/focus'
import { AUTHORING_BUTTON_TONES } from '../contracts/authoringContracts'

export function AuthoringDialog({
    open,
    title,
    description,
    children,
    onClose,
    initialFocusRef,
    panelClassName = '',
    overlayClassName = '',
    titleClassName = '',
    descriptionClassName = '',
}) {
    const titleId = useId()
    const descriptionId = useId()
    const panelRef = useRef(null)
    const previousFocusRef = useRef(null)
    const onCloseRef = useRef(onClose)

    useEffect(() => {
        onCloseRef.current = onClose
    }, [onClose])

    useEffect(() => {
        if (!open) return undefined
        previousFocusRef.current = document.activeElement
        const timer = window.setTimeout(() => {
            const target = initialFocusRef?.current || getAuthoringFocusableElements(panelRef.current)[0] || panelRef.current
            target?.focus()
        }, 0)

        const handleKeyDown = (event) => {
            if (event.key === 'Escape' && onCloseRef.current) {
                event.preventDefault()
                onCloseRef.current()
                return
            }
            if (event.key !== 'Tab') return
            const focusable = getAuthoringFocusableElements(panelRef.current)
            if (!focusable.length) {
                event.preventDefault()
                panelRef.current?.focus()
                return
            }
            const first = focusable[0]
            const last = focusable[focusable.length - 1]
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault()
                last.focus()
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault()
                first.focus()
            }
        }

        document.addEventListener('keydown', handleKeyDown)
        return () => {
            window.clearTimeout(timer)
            document.removeEventListener('keydown', handleKeyDown)
            previousFocusRef.current?.focus?.()
        }
    }, [initialFocusRef, open])

    if (!open) return null

    const dialog = (
        <div
            className={`fixed inset-0 z-[100] flex items-center justify-center bg-gray-950/50 p-4 ${overlayClassName}`}
            role="presentation"
            onMouseDown={(event) => event.target === event.currentTarget && onClose?.()}
        >
            <section
                ref={panelRef}
                role="dialog"
                aria-modal="true"
                aria-labelledby={titleId}
                aria-describedby={description ? descriptionId : undefined}
                tabIndex={-1}
                className={`w-full max-w-lg rounded-xl bg-white p-6 shadow-2xl outline-none ${panelClassName}`}
            >
                <h2 id={titleId} className={`text-lg font-semibold text-gray-900 ${titleClassName}`}>{title}</h2>
                {description ? <p id={descriptionId} className={`mt-2 text-sm text-gray-600 ${descriptionClassName}`}>{description}</p> : null}
                {children}
            </section>
        </div>
    )

    return typeof document === 'undefined' ? dialog : createPortal(dialog, document.body)
}

export function AuthoringConfirmDialog({ open, title, description, confirmLabel = 'Continue', confirmTone = 'primary', busy = false, onConfirm, onClose }) {
    const cancelRef = useRef(null)
    return (
        <AuthoringDialog open={open} title={title} description={description} onClose={busy ? undefined : onClose} initialFocusRef={cancelRef}>
            <div className="mt-6 flex justify-end gap-2">
                <button ref={cancelRef} type="button" onClick={onClose} disabled={busy} className={`${AUTHORING_BUTTON_TONES.secondary} min-h-10 rounded-lg px-4 py-2 text-sm font-medium focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 disabled:opacity-50`}>Keep editing</button>
                <button type="button" onClick={onConfirm} disabled={busy} aria-busy={busy || undefined} className={`${AUTHORING_BUTTON_TONES[confirmTone] || AUTHORING_BUTTON_TONES.primary} inline-flex min-h-10 items-center gap-2 rounded-lg px-4 py-2 text-sm font-medium focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 disabled:opacity-50`}>
                    {busy ? <Loader2 size={16} className="animate-spin" aria-hidden="true" /> : null}
                    {confirmLabel}
                </button>
            </div>
        </AuthoringDialog>
    )
}

export function AuthoringPromptDialog({ open, title, description, label, value, placeholder, confirmLabel = 'Continue', busy = false, onChange, onConfirm, onClose }) {
    const inputRef = useRef(null)
    return (
        <AuthoringDialog open={open} title={title} description={description} onClose={busy ? undefined : onClose} initialFocusRef={inputRef}>
            <label className="mt-4 block text-sm font-medium text-gray-700">
                {label}
                <input
                    ref={inputRef}
                    value={value}
                    onChange={(event) => onChange(event.target.value)}
                    onKeyDown={(event) => {
                        if (event.key === 'Enter' && value.trim() && !busy) {
                            event.preventDefault()
                            onConfirm()
                        }
                    }}
                    placeholder={placeholder}
                    className="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100"
                />
            </label>
            <div className="mt-6 flex justify-end gap-2">
                <button type="button" onClick={onClose} disabled={busy} className={`${AUTHORING_BUTTON_TONES.secondary} min-h-10 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50`}>Cancel</button>
                <button type="button" onClick={onConfirm} disabled={busy || !value.trim()} className={`${AUTHORING_BUTTON_TONES.primary} min-h-10 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50`}>{confirmLabel}</button>
            </div>
        </AuthoringDialog>
    )
}

export function UnsavedChangesDialog({ open, busy = false, onDiscard, onClose }) {
    return (
        <AuthoringConfirmDialog
            open={open}
            title={busy ? 'Authoring action in progress' : 'Unsaved changes'}
            description={busy ? 'Wait for the current action to finish before leaving this screen.' : 'Leaving now will discard changes that have not been saved.'}
            confirmLabel="Discard and leave"
            confirmTone="danger"
            busy={busy}
            onConfirm={onDiscard}
            onClose={onClose}
        />
    )
}

export function AuthoringConflictDialog({ open, message, hasLatest = true, canRetry = true, busy = false, onLoadLatest, onRetry, onClose }) {
    const closeRef = useRef(null)
    return (
        <AuthoringDialog
            open={open}
            title="A newer version is available"
            description={message || 'Someone else changed this content while you were editing. Your draft is still here.'}
            onClose={busy ? undefined : onClose}
            initialFocusRef={closeRef}
        >
            <p className="mt-3 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-900" role="status">Choose Load Latest to adopt the accepted server version, or Retry My Draft to reapply your retained changes against it.</p>
            <div className="mt-6 flex flex-wrap justify-end gap-2">
                <button ref={closeRef} type="button" onClick={onClose} disabled={busy} className={`${AUTHORING_BUTTON_TONES.secondary} min-h-10 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50`}>Keep editing</button>
                <button type="button" onClick={onLoadLatest} disabled={busy || !hasLatest} className={`${AUTHORING_BUTTON_TONES.secondary} min-h-10 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50`}>Load Latest</button>
                {canRetry ? <button type="button" onClick={onRetry} disabled={busy || !hasLatest} className={`${AUTHORING_BUTTON_TONES.primary} min-h-10 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50`}>Retry My Draft</button> : null}
            </div>
        </AuthoringDialog>
    )
}
