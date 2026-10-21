/**
 * AF-15: Premium Affiliate Login Page
 * 
 * Store-agnostic login. Affiliate identity is global and does not depend on
 * storefront discovery.
 */
import { useState, useEffect, useRef } from 'react'
import { useNavigate, Link, useSearchParams } from 'react-router-dom'
import { motion } from 'framer-motion'
import { affiliateAuthApi, resolveAffiliatePortalReturnUrl } from '../../../services/affiliateAuthApi'
import { DollarSign, LogIn, Mail, Lock, AlertCircle, CheckCircle2, Loader2 } from 'lucide-react'
import { useLanguage } from '../i18n/LanguageContext'
import LanguageSwitcher from '../components/LanguageSwitcher'
import ThemeSwitcher from '../components/ThemeSwitcher'

const fadeUp = {
    hidden: { opacity: 0, y: 20 },
    visible: (i = 0) => ({
        opacity: 1, y: 0,
        transition: { delay: i * 0.08, duration: 0.5, ease: [0.25, 0.46, 0.45, 0.94] }
    })
}

const AffiliateLogin = () => {
    const navigate = useNavigate()
    const [searchParams] = useSearchParams()
    const registeredEmail = searchParams.get('email')?.trim() || ''
    const registrationPending = searchParams.get('registered') === '1'
    const returnUrl = resolveAffiliatePortalReturnUrl(searchParams.get('returnUrl'))
    const [form, setForm] = useState({ email: '', password: '' })
    const [error, setError] = useState('')
    const [loading, setLoading] = useState(false)
    const [resendStatus, setResendStatus] = useState('idle')
    const loginInFlightRef = useRef(false)
    const resendInFlightRef = useRef(false)

    useEffect(() => {
        let active = true

        affiliateAuthApi.restoreSession()
            .then(() => {
                if (active) {
                    navigate(returnUrl, { replace: true })
                }
            })
            .catch(() => {})

        return () => {
            active = false
        }
    }, [navigate, returnUrl])

    const handleSubmit = async (e) => {
        e.preventDefault()
        if (loginInFlightRef.current) {
            return
        }

        setError('')

        loginInFlightRef.current = true
        setLoading(true)
        try {
            await affiliateAuthApi.login({
                email: form.email,
                password: form.password,
            })
            navigate(returnUrl, { replace: true })
        } catch (err) {
            const status = err.response?.status
            const message = err.response?.data?.message
            if (status === 400) setError(t('login.malformed'))
            else if (message === 'Email verification is required before login') setError(t('login.verificationRequired'))
            else if (message === 'Affiliate account is suspended') setError(t('login.suspended'))
            else if (status === 401) setError(t('login.invalidCredentials'))
            else setError(t('login.failed'))
        } finally {
            loginInFlightRef.current = false
            setLoading(false)
        }
    }

    const handleResendVerification = async () => {
        if (!registeredEmail || resendInFlightRef.current) return
        resendInFlightRef.current = true
        setResendStatus('submitting')
        try {
            await affiliateAuthApi.resendVerification(registeredEmail)
            setResendStatus('success')
        } catch {
            setResendStatus('error')
        } finally {
            resendInFlightRef.current = false
        }
    }

    const { t, dir, isRTL } = useLanguage()

    return (
        <div dir={dir} className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-[#0B0F19] px-4 relative overflow-hidden" style={{ fontFamily: isRTL ? "'Noto Kufi Arabic', 'Inter', sans-serif" : "'Inter', -apple-system, system-ui, sans-serif" }}>
            {/* Language and Theme switcher */}
            <div className="absolute top-4 right-4 z-20 flex items-center gap-3">
                <ThemeSwitcher />
                <LanguageSwitcher />
            </div>
            {/* Background effects */}
            <div className="absolute top-0 left-1/2 -translate-x-1/2 w-[800px] h-[600px] rounded-full bg-[radial-gradient(circle,rgba(108,99,255,0.12)_0%,transparent_65%)] blur-3xl pointer-events-none" />
            <div className="absolute bottom-0 right-0 w-[400px] h-[400px] rounded-full bg-[radial-gradient(circle,rgba(139,92,246,0.08)_0%,transparent_65%)] blur-2xl pointer-events-none" />
            <div className="absolute inset-0 bg-[linear-gradient(rgba(255,255,255,0.015)_1px,transparent_1px),linear-gradient(90deg,rgba(255,255,255,0.015)_1px,transparent_1px)] bg-[size:60px_60px] pointer-events-none" />

            <motion.div
                initial={{ opacity: 0, y: 30, scale: 0.98 }}
                animate={{ opacity: 1, y: 0, scale: 1 }}
                transition={{ duration: 0.6, ease: [0.25, 0.46, 0.45, 0.94] }}
                className="relative z-10 w-full max-w-[440px]"
            >
                <div className="bg-white/90 dark:bg-[#121826]/80 backdrop-blur-xl border border-gray-200 dark:border-white/[0.06] rounded-2xl p-10 shadow-2xl shadow-black/20">
                    {/* Header */}
                    <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={0} className="text-center mb-8">
                        <div className="w-14 h-14 rounded-2xl bg-gradient-to-br from-[#6C63FF] to-[#8B5CF6] flex items-center justify-center mx-auto mb-5 shadow-lg shadow-[#6C63FF]/20">
                            <DollarSign className="w-7 h-7 text-white" />
                        </div>
                        <h1 className="text-2xl font-bold text-gray-900 dark:text-white mb-2">{t('login.title')}</h1>
                        <p className="text-gray-500 text-sm">{t('login.subtitle')}</p>
                    </motion.div>

                    {/* Form */}
                    <form onSubmit={handleSubmit} className="space-y-5">
                        {registrationPending && (
                            <div className="p-3.5 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-600 dark:text-emerald-400 text-sm">
                                <div className="flex items-start gap-3">
                                    <CheckCircle2 className="w-4 h-4 flex-shrink-0 mt-0.5" />
                                    <span>{t('login.registrationPending')}</span>
                                </div>
                                {searchParams.get('deliveryFailed') === '1' && <p role="alert" className="mt-2 text-red-500">{t('login.resendFailed')}</p>}
                                {registeredEmail && (
                                    <button type="button" onClick={handleResendVerification} disabled={resendStatus === 'submitting'} className="mt-2 text-xs font-semibold underline disabled:opacity-50">
                                        {resendStatus === 'submitting' ? t('login.resending') : resendStatus === 'success' ? t('login.resendSent') : t('login.resendVerification')}
                                    </button>
                                )}
                                {resendStatus === 'error' && <p className="mt-2 text-xs text-red-500">{t('login.resendFailed')}</p>}
                            </div>
                        )}
                        {error && (
                            <motion.div
                                initial={{ opacity: 0, y: -10 }}
                                animate={{ opacity: 1, y: 0 }}
                                className="flex items-center gap-3 p-3.5 rounded-xl bg-red-500/10 border border-red-500/20 text-red-400 text-sm"
                            >
                                <AlertCircle className="w-4 h-4 flex-shrink-0" />
                                {error}
                            </motion.div>
                        )}

                        {/* Email */}
                        <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={1}>
                            <label htmlFor="aff-login-email" className="block text-xs font-medium text-gray-600 dark:text-gray-400 uppercase tracking-wider mb-2">
                                {t('login.email')}
                            </label>
                            <div className="relative">
                                <Mail className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                                <input
                                    id="aff-login-email"
                                    type="email"
                                    placeholder={t('login.emailPlaceholder')}
                                    value={form.email}
                                    onChange={e => setForm(f => ({ ...f, email: e.target.value }))}
                                    className="w-full pl-10 pr-4 py-3.5 rounded-xl bg-gray-50 dark:bg-[#0B0F19]/60 border border-gray-200 dark:border-white/[0.08] text-gray-900 dark:text-white placeholder:text-gray-600 text-sm focus:border-[#6C63FF]/50 focus:ring-2 focus:ring-[#6C63FF]/20 outline-none transition-all"
                                    required
                                    autoComplete="email"
                                    autoFocus
                                />
                            </div>
                        </motion.div>

                        {/* Password */}
                        <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={2}>
                            <label htmlFor="aff-login-password" className="block text-xs font-medium text-gray-600 dark:text-gray-400 uppercase tracking-wider mb-2">
                                {t('login.password')}
                            </label>
                            <div className="relative">
                                <Lock className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                                <input
                                    id="aff-login-password"
                                    type="password"
                                    placeholder="••••••••"
                                    value={form.password}
                                    onChange={e => setForm(f => ({ ...f, password: e.target.value }))}
                                    className="w-full pl-10 pr-4 py-3.5 rounded-xl bg-gray-50 dark:bg-[#0B0F19]/60 border border-gray-200 dark:border-white/[0.08] text-gray-900 dark:text-white placeholder:text-gray-600 text-sm focus:border-[#6C63FF]/50 focus:ring-2 focus:ring-[#6C63FF]/20 outline-none transition-all"
                                    required
                                    autoComplete="current-password"
                                />
                            </div>
                            <div className="mt-2 text-right">
                                <Link to="/affiliate/forgot-password" className="text-xs font-medium text-[#6C63FF] hover:text-[#8B5CF6]">
                                    {t('login.forgotPassword')}
                                </Link>
                            </div>
                        </motion.div>

                        {/* Submit */}
                        <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={3}>
                            <button
                                type="submit"
                                disabled={loading}
                                className="w-full flex items-center justify-center gap-2 py-3.5 rounded-xl bg-gradient-to-r from-[#6C63FF] to-[#8B5CF6] text-white font-semibold text-sm shadow-lg shadow-[#6C63FF]/20 hover:shadow-[#6C63FF]/30 hover:scale-[1.01] disabled:opacity-50 disabled:cursor-not-allowed transition-all duration-300 mt-2"
                            >
                                {loading ? (
                                    <>
                                        <Loader2 className="w-4 h-4 animate-spin" />
                                        {t('login.submitting')}
                                    </>
                                ) : (
                                    <>
                                        <LogIn className="w-4 h-4" />
                                        {t('login.submit')}
                                    </>
                                )}
                            </button>
                        </motion.div>
                    </form>

                    {/* Footer */}
                    <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={4} className="mt-8 pt-6 border-t border-gray-200 dark:border-white/[0.06] text-center">
                        <p className="text-sm text-gray-500">
                            {t('login.noAccount')}{' '}
                            <Link to="/affiliate/register" className="text-[#6C63FF] hover:text-[#8B5CF6] font-medium transition-colors">
                                {t('login.signUp')}
                            </Link>
                        </p>
                    </motion.div>
                </div>
            </motion.div>
        </div>
    )
}

export default AffiliateLogin
