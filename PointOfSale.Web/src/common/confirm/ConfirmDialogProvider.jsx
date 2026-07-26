import { createContext, useContext } from 'react'

const buildMessage = ({ title, description, inputLabel }) => (
    [title, description, inputLabel].filter(Boolean).join('\n\n')
)

const defaultConfirmDialog = {
    confirm: async (options = {}) => window.confirm(buildMessage(options)),
    prompt: async (options = {}) => {
        const response = window.prompt(
            buildMessage(options),
            options.defaultValue ?? '',
        )

        if (response == null) {
            return null
        }

        const normalizedResponse = options.trim ? response.trim() : response
        if (options.minLength && normalizedResponse.length < options.minLength) {
            return null
        }

        return normalizedResponse
    },
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
    return (
        <ConfirmDialogContext.Provider value={value || defaultConfirmDialog}>
            {children}
        </ConfirmDialogContext.Provider>
    )
}

export function useConfirmDialog() {
    return useContext(ConfirmDialogContext)
}
