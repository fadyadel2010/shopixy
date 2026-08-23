import { createContext, useCallback, useContext, useMemo, useRef, useState } from 'react'
import ConfirmModal from '../../components/ConfirmModal'

const defaultConfirmDialog = {
    confirm: async () => false,
    prompt: async () => null,
    confirmAction: async ({ onConfirm, ...options } = {}) => {
        const confirmed = await defaultConfirmDialog.confirm(options)
        if (!confirmed) {
            return false
        }

        if (typeof onConfirm === 'function') {
            return await onConfirm()
        }

        return true
    },
}

const ConfirmDialogContext = createContext(defaultConfirmDialog)

export function ConfirmDialogProvider({ children, value }) {
    const [dialog, setDialog] = useState(null)
    const [promptValue, setPromptValue] = useState('')
    // While an awaited confirmed action (see confirmAction below) is running,
    // the dialog stays mounted in a pending state: both buttons are disabled,
    // Escape/cancel are suppressed, and the destructive action shows loading.
    const [actionPending, setActionPending] = useState(false)
    const pendingSettleRef = useRef(null)
    // Synchronous duplicate-submission guard keyed by the exact dialog object:
    // a second confirm click in the same tick can never re-run the action,
    // independent of any visual disabled state.
    const runningActionDialogRef = useRef(null)

    // A settlement may close only the dialog instance that created it. This
    // prevents a late async action from resolving a newer replacement dialog.
    const settleCurrent = useCallback((settle, expectedDialog = null) => {
        const pending = pendingSettleRef.current
        if (expectedDialog && pending?.dialog !== expectedDialog) {
            return false
        }
        pendingSettleRef.current = null
        setDialog(null)
        setPromptValue('')
        setActionPending(false)
        runningActionDialogRef.current = null
        if (pending) {
            settle(pending)
        }
        return true
    }, [])

    const resolveDialog = useCallback((result, expectedDialog = null) => {
        settleCurrent((pending) => pending.resolve(result), expectedDialog)
    }, [settleCurrent])

    const openDialog = useCallback((nextDialog, initialPromptValue = '') => new Promise((resolve, reject) => {
        const previous = pendingSettleRef.current
        previous?.resolve(previous.dialog?.type === 'prompt' ? null : false)
        pendingSettleRef.current = { resolve, reject, dialog: nextDialog }
        runningActionDialogRef.current = null
        setActionPending(false)
        setPromptValue(initialPromptValue)
        setDialog(nextDialog)
    }), [])

    const confirm = useCallback((options = {}) => (
        openDialog({ type: 'confirm', options })
    ), [openDialog])

    const prompt = useCallback((options = {}) => (
        openDialog({ type: 'prompt', options }, options.defaultValue ?? '')
    ), [openDialog])

    // Legacy behavior (default): close the dialog as soon as the user decides,
    // then run onConfirm and propagate its result. Existing consumers keep
    // working unchanged.
    //
    // Opt-in `awaitAction`: keep the dialog open while onConfirm runs. The
    // modal renders a real pending state (disabled buttons, suppressed
    // Escape/cancel, loading label) until the action settles; the promise
    // resolves with the action result or rejects with its error. A
    // synchronous per-dialog guard makes double submissions impossible even
    // for clicks landing in the same tick.
    const confirmAction = useCallback(async ({ onConfirm, awaitAction, ...options } = {}) => {
        if (awaitAction) {
            return openDialog({ type: 'action', options: { ...options, onConfirm } })
        }

        const confirmed = await confirm(options)
        if (!confirmed) {
            return false
        }

        if (typeof onConfirm === 'function') {
            return await onConfirm()
        }

        return true
    }, [confirm, openDialog])

    const contextValue = useMemo(() => (
        value || { confirm, prompt, confirmAction }
    ), [confirm, confirmAction, prompt, value])

    const runConfirmedAction = useCallback(() => {
        if (!dialog || dialog.type !== 'action') {
            return
        }
        if (runningActionDialogRef.current === dialog) {
            return
        }
        runningActionDialogRef.current = dialog
        setActionPending(true)

        // The action starts synchronously within the confirm click (matching
        // the legacy confirmAction contract), while settlement keeps the
        // dialog pending until the returned promise resolves or rejects.
        const { onConfirm } = dialog.options
        let actionResult
        try {
            actionResult = typeof onConfirm === 'function' ? onConfirm() : undefined
        } catch (error) {
            if (runningActionDialogRef.current === dialog) {
                runningActionDialogRef.current = null
            }
            settleCurrent((pending) => {
                if (typeof pending.reject === 'function') {
                    pending.reject(error)
                    return
                }
                pending.resolve(false)
            }, dialog)
            return
        }

        Promise.resolve(actionResult)
            .then((result) => {
                if (runningActionDialogRef.current === dialog) {
                    runningActionDialogRef.current = null
                }
                resolveDialog(result === undefined ? true : result, dialog)
            })
            .catch((error) => {
                if (runningActionDialogRef.current === dialog) {
                    runningActionDialogRef.current = null
                }
                settleCurrent((pending) => {
                    if (typeof pending.reject === 'function') {
                        pending.reject(error)
                        return
                    }
                    pending.resolve(false)
                }, dialog)
            })
    }, [dialog, resolveDialog, settleCurrent])

    const handleConfirm = useCallback(() => {
        if (dialog?.type === 'prompt') {
            const normalizedResponse = dialog.options.trim ? promptValue.trim() : promptValue
            if (dialog.options.minLength && normalizedResponse.length < dialog.options.minLength) {
                resolveDialog(null, dialog)
                return
            }

            resolveDialog(normalizedResponse, dialog)
            return
        }

        if (dialog?.type === 'action') {
            runConfirmedAction()
            return
        }

        resolveDialog(true, dialog)
    }, [dialog, promptValue, resolveDialog, runConfirmedAction])

    const handleCancel = useCallback(() => {
        resolveDialog(dialog?.type === 'prompt' ? null : false, dialog)
    }, [dialog, resolveDialog])

    const dialogOptions = dialog?.options || {}

    return (
        <ConfirmDialogContext.Provider value={contextValue}>
            {children}
            {!value && dialog ? (
                <ConfirmModal
                    title={dialogOptions.title || dialogOptions.confirmLabel || 'Confirm action'}
                    description={dialogOptions.description}
                    confirmLabel={dialogOptions.confirmLabel || 'Confirm'}
                    cancelLabel={dialogOptions.cancelLabel}
                    variant={dialogOptions.variant}
                    confirmTone={dialogOptions.confirmTone}
                    confirmDisabled={
                        dialog.type === 'prompt'
                        && dialogOptions.minLength
                        && (dialogOptions.trim ? promptValue.trim() : promptValue).length < dialogOptions.minLength
                    }
                    loading={dialog.type === 'action' && actionPending}
                    onConfirm={handleConfirm}
                    onCancel={handleCancel}
                >
                    {dialog.type === 'prompt' ? (
                        <label className="block text-sm font-medium text-gray-700">
                            <span>{dialogOptions.inputLabel || dialogOptions.label || 'Response'}</span>
                            <input
                                autoFocus
                                className="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-500"
                                maxLength={dialogOptions.maxLength}
                                onChange={(event) => setPromptValue(event.target.value)}
                                placeholder={dialogOptions.placeholder}
                                type="text"
                                value={promptValue}
                            />
                        </label>
                    ) : null}
                </ConfirmModal>
            ) : null}
        </ConfirmDialogContext.Provider>
    )
}

export function useConfirmDialog() {
    return useContext(ConfirmDialogContext)
}
