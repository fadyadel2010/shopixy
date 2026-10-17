import { useState, useEffect } from 'react'
import api from '../services/api'

/**
 * Email Verification Banner Component
 * Phase 8.4: Email Verification & Account Trust
 * 
 * Shows a non-blocking banner for unverified users.
 * Can be dismissed but reappears on page refresh.
 */
export default function EmailVerificationBanner() {
    const [status, setStatus] = useState(null) // null = loading, { emailVerified: boolean }
    const [dismissed, setDismissed] = useState(false)
    const [resending, setResending] = useState(false)
    const [resendMessage, setResendMessage] = useState('')

    useEffect(() => {
        checkVerificationStatus()
    }, [])

    const checkVerificationStatus = async () => {
        try {
            const response = await api.get('/auth/verification-status')
            setStatus(response.data)
        } catch {
            // If not authenticated or error, don't show banner
            setStatus({ emailVerified: true })
        }
    }

    const handleResend = async () => {
        setResending(true)
        setResendMessage('')

        try {
            const response = await api.post('/auth/resend-verification')
            setResendMessage(response.data.message || 'Verification email sent!')
        } catch (err) {
            const errorData = err.response?.data
            if (err.response?.status === 429) {
                setResendMessage('Too many requests. Please try again later.')
            } else if (errorData?.error === 'ALREADY_VERIFIED') {
                setStatus({ emailVerified: true })
            } else {
                setResendMessage(errorData?.message || 'Failed to send email. Please try again.')
            }
        } finally {
            setResending(false)
        }
    }

    const handleDismiss = () => {
        setDismissed(true)
    }

    // Don't render if:
    // - Still loading
    // - Already verified
    // - User dismissed
    if (status === null || status?.emailVerified || dismissed) {
        return null
    }

    return (
        <div className="bg-amber-50 border-b border-amber-200">
            <div className="max-w-7xl mx-auto px-4 py-3">
                <div className="flex items-center justify-between flex-wrap gap-2">
                    <div className="flex items-center gap-3">
                        <svg className="w-5 h-5 text-amber-500 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                        </svg>
                        <span className="text-amber-800 text-sm">
                            <strong>Verify your email</strong> to publish your online store.
                            {resendMessage && (
                                <span className="ml-2 text-amber-600">
                                    {resendMessage}
                                </span>
                            )}
                        </span>
                    </div>

                    <div className="flex items-center gap-3">
                        <button
                            onClick={handleResend}
                            disabled={resending}
                            className="text-sm font-medium text-amber-700 hover:text-amber-800 underline focus:outline-none disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                            {resending ? 'Sending...' : 'Resend email'}
                        </button>

                        <button
                            onClick={handleDismiss}
                            className="text-amber-500 hover:text-amber-600 focus:outline-none"
                            aria-label="Dismiss"
                        >
                            <svg className="w-5 h-5" fill="currentColor" viewBox="0 0 20 20">
                                <path fillRule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clipRule="evenodd" />
                            </svg>
                        </button>
                    </div>
                </div>
            </div>
        </div>
    )
}
