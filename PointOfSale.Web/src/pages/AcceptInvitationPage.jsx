import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { acceptPosUserInvitation } from '../services/userManagementApi'
import { useRTL } from '../hooks/useRTL'
import { userErrorKey } from '../components/users/userErrors'
import { useAuthStore } from '../store/authStore'
import api from '../services/api'
import { invalidatePosRuntimeCache } from '../services/posRuntimeCache'
import { broadcastPosVendorLogout } from '../auth/posVendorSessionAuthority'

const inputClass = 'w-full rounded-lg border px-4 py-2 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500'

export default function AcceptInvitationPage() {
    const { t } = useTranslation()
    const { dir } = useRTL()
    const navigate = useNavigate()
    const [searchParams] = useSearchParams()
    const token = searchParams.get('token') || ''
    const { isAuthenticated, logout } = useAuthStore()

    const [password, setPassword] = useState('')
    const [error, setError] = useState('')
    const [success, setSuccess] = useState(false)
    const [submitting, setSubmitting] = useState(false)
    const [switchingAccount, setSwitchingAccount] = useState(false)

    const submit = async (event) => {
        event.preventDefault()
        if (submitting || !token) return
        setSubmitting(true)
        setError('')
        try {
            await acceptPosUserInvitation(token, password)
            setSuccess(true)
        } catch (err) {
            setError(t(`userManagement.${userErrorKey(err)}`))
        } finally {
            setSubmitting(false)
        }
    }

    const goToLogin = async () => {
        if (switchingAccount) return
        setSwitchingAccount(true)

        if (isAuthenticated) {
            try {
                await api.post('/auth/logout')
            } catch {
                // Continue locally so an existing account cannot mask the invited account.
            } finally {
                invalidatePosRuntimeCache({ reason: 'invitation-account-switch', broadcast: false })
                broadcastPosVendorLogout()
                logout()
            }
        }

        navigate('/login?switchAccount=1', { replace: true })
    }

    return (
        <div dir={dir} className="flex min-h-screen items-center justify-center bg-gray-50 p-4">
            <div className="w-full max-w-md rounded-xl bg-white p-8 shadow">
                <h1 className="mb-6 text-center text-2xl font-bold text-gray-800">{t('userManagement.acceptPage.title')}</h1>
                {!token && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{t('userManagement.acceptPage.missingToken')}</p>}
                {error && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
                {success ? (
                    <div>
                        <p role="status" className="mb-4 rounded-lg bg-green-50 p-3 text-sm text-green-800">{t('userManagement.acceptPage.success')}</p>
                        {isAuthenticated && (
                            <p className="mb-4 text-sm text-gray-600">{t('userManagement.acceptPage.existingSession')}</p>
                        )}
                        <button
                            type="button"
                            onClick={goToLogin}
                            disabled={switchingAccount}
                            className="block w-full rounded-lg bg-indigo-600 px-4 py-2 text-center text-white hover:bg-indigo-700 disabled:opacity-50"
                        >
                            {switchingAccount
                                ? t('userManagement.acceptPage.switchingAccount')
                                : t('userManagement.acceptPage.goToLogin')}
                        </button>
                    </div>
                ) : (
                    token && (
                        <form onSubmit={submit} className="space-y-4">
                            <label className="block text-sm font-medium text-gray-700">
                                {t('userManagement.acceptPage.password')}
                                <input
                                    type="password"
                                    required
                                    minLength={8}
                                    maxLength={128}
                                    autoComplete="new-password"
                                    value={password}
                                    onChange={(event) => setPassword(event.target.value)}
                                    className={inputClass}
                                />
                            </label>
                            <p className="text-xs text-gray-500">{t('userManagement.acceptPage.passwordHint')}</p>
                            <button
                                type="submit"
                                disabled={submitting}
                                className="w-full rounded-lg bg-indigo-600 px-4 py-2 text-white hover:bg-indigo-700 disabled:opacity-50"
                            >
                                {submitting ? t('userManagement.acceptPage.submitting') : t('userManagement.acceptPage.submit')}
                            </button>
                        </form>
                    )
                )}
            </div>
        </div>
    )
}
