import { useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { AlertCircle, CheckCircle2, Loader2, Lock } from 'lucide-react'
import { affiliateAuthApi } from '../../../services/affiliateAuthApi'
import AffiliateIdentityShell from '../components/AffiliateIdentityShell'
import { useLanguage } from '../i18n/LanguageContext'

export default function AffiliateResetPassword() {
    const { t } = useLanguage()
    const [searchParams] = useSearchParams()
    const token = searchParams.get('token')?.trim() || ''
    const [form, setForm] = useState({ password: '', confirmPassword: '' })
    const [status, setStatus] = useState(token ? 'idle' : 'error')
    const [message, setMessage] = useState(token ? '' : t('identity.reset.missingToken'))
    const inFlightRef = useRef(false)

    const handleSubmit = async (event) => {
        event.preventDefault()
        if (inFlightRef.current || !token) return
        if (form.password !== form.confirmPassword) {
            setStatus('error')
            setMessage(t('identity.reset.mismatch'))
            return
        }
        if (!/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{8,}$/.test(form.password)) {
            setStatus('error')
            setMessage(t('identity.reset.passwordStrong'))
            return
        }

        inFlightRef.current = true
        setStatus('submitting')
        setMessage('')
        try {
            await affiliateAuthApi.resetPassword(token, form.password)
            setStatus('success')
        } catch (error) {
            setStatus('error')
            setMessage(t('identity.reset.failed'))
        } finally {
            inFlightRef.current = false
        }
    }

    return (
        <AffiliateIdentityShell title={t('identity.reset.title')} subtitle={t('identity.reset.subtitle')}>
            {status === 'success' ? (
                <div className="text-center">
                    <CheckCircle2 className="w-10 h-10 mx-auto text-emerald-500" />
                    <p className="mt-4 text-sm text-gray-600 dark:text-gray-300">{t('identity.reset.success')}</p>
                    <Link className="inline-block mt-6 text-sm font-semibold text-[#6C63FF]" to="/affiliate/login">{t('identity.signIn')}</Link>
                </div>
            ) : (
                <form onSubmit={handleSubmit} className="space-y-4">
                    {message && <p className="flex gap-2 text-sm text-red-500"><AlertCircle className="w-4 h-4 shrink-0" />{message}</p>}
                    {['password', 'confirmPassword'].map((field) => (
                        <div key={field}>
                            <label className="block text-sm text-gray-600 dark:text-gray-300 mb-2" htmlFor={`affiliate-reset-${field}`}>{t(`identity.reset.${field}`)}</label>
                            <div className="relative">
                                <Lock className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                                <input id={`affiliate-reset-${field}`} type="password" minLength={8} required autoComplete="new-password" value={form[field]} onChange={(event) => setForm((current) => ({ ...current, [field]: event.target.value }))} className="w-full pl-10 pr-4 py-3.5 rounded-xl bg-gray-50 dark:bg-[#0B0F19]/60 border border-gray-200 dark:border-white/[0.08] text-gray-900 dark:text-white" />
                            </div>
                        </div>
                    ))}
                    <button disabled={status === 'submitting' || !token} className="w-full py-3.5 rounded-xl bg-[#6C63FF] text-white font-semibold disabled:opacity-50">
                        {status === 'submitting' ? <Loader2 className="w-4 h-4 mx-auto animate-spin" /> : t('identity.reset.submit')}
                    </button>
                </form>
            )}
        </AffiliateIdentityShell>
    )
}
