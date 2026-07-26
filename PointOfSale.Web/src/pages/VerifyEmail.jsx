import { useState, useEffect } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import api from '../services/api'

/**
 * Email Verification Result Page
 * Phase 8.4: Email Verification & Account Trust
 * 
 * Handles verification link clicks with states:
 * - Loading (validating token)
 * - Success (email verified)
 * - Expired (token expired)
 * - Already verified
 * - Invalid token
 */
export default function VerifyEmail() {
    const [searchParams] = useSearchParams()
    const navigate = useNavigate()

    const [status, setStatus] = useState('loading')
    const [message, setMessage] = useState('')

    useEffect(() => {
        const token = searchParams.get('token')

        if (!token) {
            setStatus('invalid')
            setMessage('Invalid verification link')
            return
        }

        verifyEmail(token)
    }, [searchParams])

    const verifyEmail = async (token) => {
        try {
            const response = await api.get(`/auth/verify-email?token=${encodeURIComponent(token)}`)

            if (response.data.success) {
                setStatus('success')
                setMessage(response.data.message || 'Your email has been verified!')
            } else {
                setStatus('error')
                setMessage(response.data.message || 'Verification failed')
            }
        } catch (err) {
            const errorData = err.response?.data
            const errorCode = errorData?.error

            switch (errorCode) {
                case 'TOKEN_EXPIRED':
                    setStatus('expired')
                    setMessage('This verification link has expired. Please request a new one.')
                    break
                case 'TOKEN_USED':
                    setStatus('already-verified')
                    setMessage('This link has already been used. Your email is verified!')
                    break
                case 'ALREADY_VERIFIED':
                    setStatus('already-verified')
                    setMessage('Your email is already verified!')
                    break
                case 'INVALID_TOKEN':
                default:
                    setStatus('invalid')
                    setMessage('This verification link is invalid.')
            }
        }
    }

    const handleGoToDashboard = () => {
        navigate('/', { replace: true })
    }

    const handleGoToLogin = () => {
        navigate('/login', { replace: true })
    }

    const handleResendEmail = async () => {
        // Redirect to login - user needs to be authenticated to resend
        navigate('/login', { replace: true })
    }

    const renderIcon = () => {
        switch (status) {
            case 'loading':
                return (
                    <div className="w-16 h-16 mx-auto mb-6">
                        <svg className="animate-spin w-full h-full text-primary-600" fill="none" viewBox="0 0 24 24">
                            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                        </svg>
                    </div>
                )
            case 'success':
            case 'already-verified':
                return (
                    <div className="w-16 h-16 mx-auto mb-6 bg-green-100 rounded-full flex items-center justify-center">
                        <svg className="w-10 h-10 text-green-600" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                        </svg>
                    </div>
                )
            case 'expired':
            case 'invalid':
            default:
                return (
                    <div className="w-16 h-16 mx-auto mb-6 bg-red-100 rounded-full flex items-center justify-center">
                        <svg className="w-10 h-10 text-red-600" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clipRule="evenodd" />
                        </svg>
                    </div>
                )
        }
    }

    const renderTitle = () => {
        switch (status) {
            case 'loading':
                return 'Verifying your email...'
            case 'success':
                return 'Email Verified!'
            case 'already-verified':
                return 'Already Verified'
            case 'expired':
                return 'Link Expired'
            case 'invalid':
            default:
                return 'Invalid Link'
        }
    }

    const renderActions = () => {
        switch (status) {
            case 'success':
            case 'already-verified':
                return (
                    <button
                        onClick={handleGoToDashboard}
                        className="w-full py-3 px-4 bg-primary-600 hover:bg-primary-700 text-white font-medium rounded-lg transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2"
                    >
                        Go to Dashboard
                    </button>
                )
            case 'expired':
                return (
                    <div className="space-y-3">
                        <button
                            onClick={handleResendEmail}
                            className="w-full py-3 px-4 bg-primary-600 hover:bg-primary-700 text-white font-medium rounded-lg transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2"
                        >
                            Request New Link
                        </button>
                        <button
                            onClick={handleGoToLogin}
                            className="w-full py-3 px-4 bg-gray-100 hover:bg-gray-200 text-gray-700 font-medium rounded-lg transition-colors focus:outline-none focus:ring-2 focus:ring-gray-300"
                        >
                            Go to Login
                        </button>
                    </div>
                )
            case 'invalid':
                return (
                    <button
                        onClick={handleGoToLogin}
                        className="w-full py-3 px-4 bg-primary-600 hover:bg-primary-700 text-white font-medium rounded-lg transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2"
                    >
                        Go to Login
                    </button>
                )
            default:
                return null
        }
    }

    return (
        <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-primary-500 to-primary-700 py-12 px-4">
            <div className="w-full max-w-md">
                <div className="bg-white rounded-xl shadow-2xl p-8 text-center">
                    {renderIcon()}

                    <h1 className="text-2xl font-bold text-gray-900 mb-3">
                        {renderTitle()}
                    </h1>

                    <p className="text-gray-600 mb-8">
                        {message}
                    </p>

                    {renderActions()}
                </div>
            </div>
        </div>
    )
}
