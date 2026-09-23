import { useEffect, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAuthStore } from '../store/authStore'
import { useSettings } from '../contexts/SettingsContext'
import LanguageSwitcher from '../components/LanguageSwitcher'
import api from '../services/api'
import { requirePosVendorSessionUserAuthority, resolvePosRouteRedirect } from '../auth/posVendorSessionAuthority'

export default function Login() {
    const { t } = useTranslation()
    const [username, setUsername] = useState('')
    const [password, setPassword] = useState('')
    const [error, setError] = useState('')
    const [loading, setLoading] = useState(false)

    const { isAuthenticated, login } = useAuthStore()
    const { reloadSettings } = useSettings()
    const location = useLocation()
    const navigate = useNavigate()
    const isSwitchingAccount = new URLSearchParams(location.search).get('switchAccount') === '1'
    const redirectTo = resolvePosRouteRedirect(
        location.state?.from
            ? `${location.state.from.pathname || ''}${location.state.from.search || ''}${location.state.from.hash || ''}`
            : '/',
    )

    useEffect(() => {
        if (isAuthenticated && !isSwitchingAccount) {
            navigate(redirectTo, { replace: true })
        }
    }, [isAuthenticated, isSwitchingAccount, navigate, redirectTo])

    const handleSubmit = async (e) => {
        e.preventDefault()
        if (loading) {
            return
        }

        setError('')

        const normalizedUsername = username.trim()
        if (!normalizedUsername || !password.trim()) {
            setError(t('login.enterCredentials'))
            return
        }

        setLoading(true)

        try {
            await api.post('/auth/login', { username: normalizedUsername, password })
            const sessionResponse = await api.get('/auth/session')
            login(requirePosVendorSessionUserAuthority(sessionResponse.data?.user ?? null))

            // Reload settings after successful login
            try {
                await reloadSettings()
            } catch (settingsError) {
                if (import.meta.env.DEV) {
                    console.warn('Failed to load settings, but continuing with login:', settingsError)
                }
            }

            navigate(redirectTo, { replace: true })
        } catch (err) {
            if (import.meta.env.DEV) {
                console.error('Login error:', err)
            }

            const errorMessage = err.response?.data?.message || err.message || 'Login failed. Please try again.'
            setError(errorMessage)
        } finally {
            setLoading(false)
        }
    }

    return (
        <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-primary-500 to-primary-700">
            <div className="max-w-md w-full bg-white rounded-lg shadow-xl p-8 relative">
                {/* Language Switcher */}
                <div className="absolute top-4 right-4">
                    <LanguageSwitcher />
                </div>

                <div className="text-center mb-8">
                    <h1 className="text-3xl font-bold text-gray-900">{t('app.name')}</h1>
                    <p className="text-gray-600 mt-2">{t('login.welcomeBack')}</p>
                </div>

                {error && (
                    <div className="mb-4 p-3 bg-red-100 border border-red-400 text-red-700 rounded">
                        {error}
                    </div>
                )}

                <form onSubmit={handleSubmit} className="space-y-6">
                    <div>
                        <label className="label" htmlFor="pos-login-username">{t('login.usernameOrEmail')}</label>
                        <input
                            id="pos-login-username"
                            type="text"
                            value={username}
                            onChange={(e) => setUsername(e.target.value)}
                            className="input"
                            disabled={loading}
                            required
                            autoFocus
                            autoComplete="username"
                        />
                    </div>

                    <div>
                        <label className="label" htmlFor="pos-login-password">{t('login.password')}</label>
                        <input
                            id="pos-login-password"
                            type="password"
                            value={password}
                            onChange={(e) => setPassword(e.target.value)}
                            className="input"
                            disabled={loading}
                            required
                            autoComplete="current-password"
                        />
                    </div>

                    <button
                        type="submit"
                        disabled={loading}
                        className="w-full btn-primary disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                        {loading ? t('common.loading') : t('login.signIn')}
                    </button>
                </form>

                <div className="mt-6 text-center text-sm text-gray-600">
                    <p>Don't have an account?{' '}
                        <a href="/register" className="text-primary-600 hover:text-primary-700 font-medium">
                            Create your store
                        </a>
                    </p>
                </div>
            </div>
        </div>
    )
}
