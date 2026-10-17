/**
 * AF-19: Global Marketplace Affiliate Registration
 *
 * Affiliates register globally (no store binding required).
 * StoreSlug is resolved in background for backward compatibility but is optional.
 * Affiliates can promote products from ANY store after registration.
 *
 * Route: /affiliate/register
 */
import { useState, useEffect, useRef } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { motion } from 'framer-motion'
import { affiliateAuthApi } from '../../../services/affiliateAuthApi'
import {
    Rocket, User, Mail, Lock, AlertCircle, Loader2,
    DollarSign, BarChart3, Link2
} from 'lucide-react'
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

const AffiliateRegister = () => {
    const navigate = useNavigate()
    const { t, dir, isRTL } = useLanguage()
    const [form, setForm] = useState({ displayName: '', email: '', password: '', confirmPassword: '' })
    const [error, setError] = useState('')
    const [loading, setLoading] = useState(false)
    const registrationInFlightRef = useRef(false)

    useEffect(() => {
        let active = true

        affiliateAuthApi.restoreSession()
            .then(() => {
                if (active) {
                    navigate('/affiliate/portal', { replace: true })
                }
            })
            .catch(() => {})

        return () => {
            active = false
        }
    }, [navigate])

    const handleSubmit = async (e) => {
        e.preventDefault()
        if (registrationInFlightRef.current) {
            return
        }

        setError('')

        if (form.displayName.trim().length < 2) {
            setError(t('register.nameRequired'))
            return
        }
        if (!/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{8,}$/.test(form.password)) {
            setError(t('register.passwordStrong'))
            return
        }
        if (form.password !== form.confirmPassword) {
            setError(t('register.passwordMismatch'))
            return
        }

        registrationInFlightRef.current = true
        setLoading(true)
        try {
            // AF-19: Global affiliates - no storeSlug needed
            await affiliateAuthApi.register({
                email: form.email,
                password: form.password,
                displayName: form.displayName.trim(),
            })
            navigate(`/affiliate/login?registered=1&email=${encodeURIComponent(form.email.trim())}`, { replace: true })
        } catch (err) {
            if (err.response?.status === 409) setError(t('register.duplicateEmail'))
            else setError(t('register.failed'))
        } finally {
            registrationInFlightRef.current = false
            setLoading(false)
        }
    }

    const perks = [
        { icon: DollarSign, text: t('register.perk1') },
        { icon: BarChart3, text: t('register.perk2') },
        { icon: Link2, text: t('register.perk3') },
    ]

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
                className="relative z-10 w-full max-w-[480px]"
            >
                <div className="bg-white/90 dark:bg-[#121826]/80 backdrop-blur-xl border border-gray-200 dark:border-white/[0.06] rounded-2xl p-10 shadow-2xl shadow-black/20">
                    {/* Header */}
                    <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={0} className="text-center mb-8">
                        <div className="w-14 h-14 rounded-2xl bg-gradient-to-br from-[#6C63FF] to-[#8B5CF6] flex items-center justify-center mx-auto mb-5 shadow-lg shadow-[#6C63FF]/20">
                            <Rocket className="w-7 h-7 text-white" />
                        </div>
                        <h1 className="text-2xl font-bold text-gray-900 dark:text-white mb-2">{t('register.title')}</h1>
                        <p className="text-gray-500 text-sm">{t('register.subtitle')}</p>
                    </motion.div>

                    {/* Form */}
                    <form onSubmit={handleSubmit} className="space-y-4">
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

                        {/* Display Name */}
                        <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={1}>
                            <label htmlFor="aff-reg-name" className="block text-xs font-medium text-gray-600 dark:text-gray-400 uppercase tracking-wider mb-2">
                                {t('register.displayName')}
                            </label>
                            <div className="relative">
                                <User className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                                <input
                                    id="aff-reg-name"
                                    type="text"
                                    placeholder={t('register.namePlaceholder')}
                                    value={form.displayName}
                                    onChange={e => setForm(f => ({ ...f, displayName: e.target.value }))}
                                    className="w-full pl-10 pr-4 py-3.5 rounded-xl bg-gray-50 dark:bg-[#0B0F19]/60 border border-gray-200 dark:border-white/[0.08] text-gray-900 dark:text-white placeholder:text-gray-600 text-sm focus:border-[#6C63FF]/50 focus:ring-2 focus:ring-[#6C63FF]/20 outline-none transition-all"
                                    required
                                    autoComplete="name"
                                    autoFocus
                                />
                            </div>
                        </motion.div>

                        {/* Email */}
                        <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={2}>
                            <label htmlFor="aff-reg-email" className="block text-xs font-medium text-gray-600 dark:text-gray-400 uppercase tracking-wider mb-2">
                                {t('register.email')}
                            </label>
                            <div className="relative">
                                <Mail className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                                <input
                                    id="aff-reg-email"
                                    type="email"
                                    placeholder="affiliate@example.com"
                                    value={form.email}
                                    onChange={e => setForm(f => ({ ...f, email: e.target.value }))}
                                    className="w-full pl-10 pr-4 py-3.5 rounded-xl bg-gray-50 dark:bg-[#0B0F19]/60 border border-gray-200 dark:border-white/[0.08] text-gray-900 dark:text-white placeholder:text-gray-600 text-sm focus:border-[#6C63FF]/50 focus:ring-2 focus:ring-[#6C63FF]/20 outline-none transition-all"
                                    required
                                    autoComplete="email"
                                />
                            </div>
                        </motion.div>

                        {/* Password row */}
                        <div className="grid grid-cols-2 gap-3">
                            <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={3}>
                                <label htmlFor="aff-reg-password" className="block text-xs font-medium text-gray-600 dark:text-gray-400 uppercase tracking-wider mb-2">
                                    {t('register.password')}
                                </label>
                                <div className="relative">
                                    <Lock className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                                    <input
                                        id="aff-reg-password"
                                        type="password"
                                        placeholder="••••••••"
                                        value={form.password}
                                        onChange={e => setForm(f => ({ ...f, password: e.target.value }))}
                                        className="w-full pl-10 pr-4 py-3.5 rounded-xl bg-gray-50 dark:bg-[#0B0F19]/60 border border-gray-200 dark:border-white/[0.08] text-gray-900 dark:text-white placeholder:text-gray-600 text-sm focus:border-[#6C63FF]/50 focus:ring-2 focus:ring-[#6C63FF]/20 outline-none transition-all"
                                        required
                                        minLength={8}
                                        autoComplete="new-password"
                                    />
                                </div>
                            </motion.div>

                            <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={4}>
                                <label htmlFor="aff-reg-confirm" className="block text-xs font-medium text-gray-600 dark:text-gray-400 uppercase tracking-wider mb-2">
                                    {t('register.confirm')}
                                </label>
                                <div className="relative">
                                    <Lock className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                                    <input
                                        id="aff-reg-confirm"
                                        type="password"
                                        placeholder="••••••••"
                                        value={form.confirmPassword}
                                        onChange={e => setForm(f => ({ ...f, confirmPassword: e.target.value }))}
                                        className="w-full pl-10 pr-4 py-3.5 rounded-xl bg-gray-50 dark:bg-[#0B0F19]/60 border border-gray-200 dark:border-white/[0.08] text-gray-900 dark:text-white placeholder:text-gray-600 text-sm focus:border-[#6C63FF]/50 focus:ring-2 focus:ring-[#6C63FF]/20 outline-none transition-all"
                                        required
                                        minLength={8}
                                        autoComplete="new-password"
                                    />
                                </div>
                            </motion.div>
                        </div>

                        {/* Submit */}
                        <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={5}>
                            <button
                                type="submit"
                                disabled={loading}
                                className="w-full flex items-center justify-center gap-2 py-3.5 rounded-xl bg-gradient-to-r from-[#6C63FF] to-[#8B5CF6] text-white font-semibold text-sm shadow-lg shadow-[#6C63FF]/20 hover:shadow-[#6C63FF]/30 hover:scale-[1.01] disabled:opacity-50 disabled:cursor-not-allowed transition-all duration-300 mt-2"
                            >
                                {loading ? (
                                    <>
                                        <Loader2 className="w-4 h-4 animate-spin" />
                                        {t('register.submitting')}
                                    </>
                                ) : (
                                    <>
                                        <Rocket className="w-4 h-4" />
                                        {t('register.submit')}
                                    </>
                                )}
                            </button>
                        </motion.div>
                    </form>

                    {/* Perks */}
                    <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={6}
                        className="mt-6 p-4 rounded-xl bg-white dark:bg-white/[0.02] border border-white/[0.04] space-y-3"
                    >
                        {perks.map((perk, i) => {
                            const Icon = perk.icon
                            return (
                                <div key={i} className="flex items-center gap-3 text-sm text-gray-600 dark:text-gray-400">
                                    <div className="w-7 h-7 rounded-lg bg-[#6C63FF]/10 flex items-center justify-center flex-shrink-0">
                                        <Icon className="w-3.5 h-3.5 text-[#6C63FF]" />
                                    </div>
                                    {perk.text}
                                </div>
                            )
                        })}
                    </motion.div>

                    {/* Footer */}
                    <motion.div variants={fadeUp} initial="hidden" animate="visible" custom={7} className="mt-8 pt-6 border-t border-gray-200 dark:border-white/[0.06] text-center space-y-2">
                        <p className="text-sm text-gray-500">
                            {t('register.hasAccount')}{' '}
                            <Link to="/affiliate/login" className="text-[#6C63FF] hover:text-[#8B5CF6] font-medium transition-colors">
                                {t('register.signIn')}
                            </Link>
                        </p>
                        <p className="text-xs text-gray-600">
                            <Link to="/affiliate" className="hover:text-gray-400 transition-colors">
                                {t('register.backToHub')}
                            </Link>
                        </p>
                    </motion.div>
                </div>
            </motion.div>
        </div>
    )
}

export default AffiliateRegister
