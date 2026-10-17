import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react'
import { CheckCircle2, CircleAlert, Info, TriangleAlert, X } from 'lucide-react'
import { useRTL } from '../hooks/useRTL'

const ToastContext = createContext()

export function ToastProvider({ children }) {
    const [toasts, setToasts] = useState([])
    const timeoutsRef = useRef(new Map())

    const removeToast = useCallback((id) => {
        const timeoutId = timeoutsRef.current.get(id)
        if (timeoutId !== undefined) {
            clearTimeout(timeoutId)
            timeoutsRef.current.delete(id)
        }

        setToasts((prev) => prev.filter((toast) => toast.id !== id))
    }, [])

    const addToast = useCallback((message, type = 'info', duration = 3000) => {
        const id = Date.now() + Math.random()
        const toast = { id, message, type, duration }

        setToasts((prev) => [...prev, toast])

        if (duration > 0) {
            const timeoutId = setTimeout(() => {
                removeToast(id)
            }, duration)

            timeoutsRef.current.set(id, timeoutId)
        }
    }, [removeToast])

    useEffect(() => {
        const timeouts = timeoutsRef.current
        return () => {
            timeouts.forEach((timeoutId) => {
                clearTimeout(timeoutId)
            })
            timeouts.clear()
        }
    }, [])

    const showSuccess = useCallback((message, duration) => {
        addToast(message, 'success', duration)
    }, [addToast])

    const showError = useCallback((message, duration) => {
        addToast(message, 'error', duration)
    }, [addToast])

    const showInfo = useCallback((message, duration) => {
        addToast(message, 'info', duration)
    }, [addToast])

    const showWarning = useCallback((message, duration) => {
        addToast(message, 'warning', duration)
    }, [addToast])

    return (
        <ToastContext.Provider value={{ showSuccess, showError, showInfo, showWarning }}>
            {children}
            <ToastContainer toasts={toasts} onRemove={removeToast} />
        </ToastContext.Provider>
    )
}

export function useToast() {
    const context = useContext(ToastContext)
    if (!context) {
        throw new Error('useToast must be used within ToastProvider')
    }
    return context
}

function ToastContainer({ toasts, onRemove }) {
    const { isRTL } = useRTL()

    return (
        <div className={`fixed top-4 z-50 space-y-2 pointer-events-none ${isRTL ? 'left-4' : 'right-4'}`}>
            {toasts.map((toast) => (
                <div
                    key={toast.id}
                    aria-live={toast.type === 'error' ? 'assertive' : 'polite'}
                    role={toast.type === 'error' ? 'alert' : 'status'}
                    aria-atomic="true"
                >
                    <Toast toast={toast} onRemove={onRemove} />
                </div>
            ))}
        </div>
    )
}

function Toast({ toast, onRemove }) {
    const { id, message, type } = toast
    const { dir, isRTL } = useRTL()

    const styles = {
        success: 'bg-green-500 text-white',
        error: 'bg-red-500 text-white',
        warning: 'bg-yellow-500 text-white',
        info: 'bg-blue-500 text-white',
    }

    const icons = {
        success: CheckCircle2,
        error: CircleAlert,
        warning: TriangleAlert,
        info: Info,
    }

    const Icon = icons[type] || Info

    return (
        <div
            dir={dir}
            className={`${styles[type]} px-6 py-3 rounded-lg shadow-lg flex items-center gap-3 min-w-[300px] max-w-md pointer-events-auto ${isRTL ? 'animate-slide-in-left' : 'animate-slide-in-right'}`}
            onClick={() => onRemove(id)}
        >
            <Icon className="h-5 w-5 shrink-0" />
            <div className="flex-1 text-sm font-medium">{message}</div>
            <button
                onClick={(event) => {
                    event.stopPropagation()
                    onRemove(id)
                }}
                className="text-white hover:text-gray-200 transition-colors"
                aria-label="Dismiss notification"
            >
                <X className="h-4 w-4" />
            </button>
        </div>
    )
}
