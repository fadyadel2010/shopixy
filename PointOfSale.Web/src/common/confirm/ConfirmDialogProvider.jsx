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
    const resolverRef = useRef(null)

    const resolveDialog = useCallback((result) => {
        const resolver = resolverRef.current
        resolverRef.current = null
        setDialog(null)
        setPromptValue('')
        resolver?.(result)
    }, [])

    const openDialog = useCallback((nextDialog, initialPromptValue = '') => new Promise((resolve) => {
        resolverRef.current?.(nextDialog.type === 'prompt' ? null : false)
        resolverRef.current = resolve
        setPromptValue(initialPromptValue)
        setDialog(nextDialog)
    }), [])

    const confirm = useCallback((options = {}) => (
        openDialog({ type: 'confirm', options })
    ), [openDialog])

    const prompt = useCallback((options = {}) => (
        openDialog({ type: 'prompt', options }, options.defaultValue ?? '')
    ), [openDialog])

    const confirmAction = useCallback(async ({ onConfirm, ...options } = {}) => {
        const confirmed = await confirm(options)
        if (!confirmed) {
            return false
        }

        if (typeof onConfirm === 'function') {
            return await onConfirm()
        }

        return true
    }, [confirm])

    const contextValue = useMemo(() => (
        value || { confirm, prompt, confirmAction }
    ), [confirm, confirmAction, prompt, value])

    const handleConfirm = useCallback(() => {
        if (dialog?.type === 'prompt') {
            const normalizedResponse = dialog.options.trim ? promptValue.trim() : promptValue
            if (dialog.options.minLength && normalizedResponse.length < dialog.options.minLength) {
                resolveDialog(null)
                return
            }

            resolveDialog(normalizedResponse)
            return
        }

        resolveDialog(true)
    }, [dialog, promptValue, resolveDialog])

    const handleCancel = useCallback(() => {
        resolveDialog(dialog?.type === 'prompt' ? null : false)
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
