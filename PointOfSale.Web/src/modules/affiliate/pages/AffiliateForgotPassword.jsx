import { useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { CheckCircle2, Loader2, Mail } from 'lucide-react'
import { affiliateAuthApi } from '../../../services/affiliateAuthApi'
import AffiliateIdentityShell from '../components/AffiliateIdentityShell'
import { useLanguage } from '../i18n/LanguageContext'

export default function AffiliateForgotPassword() {
    const { t } = useLanguage()
    const [email, setEmail] = useState('')
    const [status, setStatus] = useState('idle')
    const inFlightRef = useRef(false)

    const handleSubmit = async (event) => {
        event.preventDefault()
        if (inFlightRef.current) return
        inFlightRef.current = true
        setStatus('submitting')
        try {
            await affiliateAuthApi.forgotPassword(email)
            setStatus('success')
        } catch {
            setStatus('error')
        } finally {
            inFlightRef.current = false
        }
    }

    return (
        <AffiliateIdentityShell title={t('identity.forgot.title')} subtitle={t('identity.forgot.subtitle')}>
            {status === 'success' ? (
                <div className="text-center">
                    <CheckCircle2 className="w-10 h-10 mx-auto text-emerald-500" />
                    <p className="mt-4 text-sm text-gray-600 dark:text-gray-300">{t('identity.forgot.success')}</p>
                    <Link className="inline-block mt-6 text-sm font-semibold text-[#6C63FF]" to="/affiliate/login">{t('identity.signIn')}</Link>
                </div>
            ) : (
                <form onSubmit={handleSubmit} className="space-y-4">
                    {status === 'error' && <p className="text-sm text-red-500">{t('identity.forgot.failed')}</p>}
                    <label className="block text-sm text-gray-600 dark:text-gray-300" htmlFor="affiliate-forgot-email">{t('identity.email')}</label>
                    <div className="relative">
                        <Mail className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                        <input id="affiliate-forgot-email" type="email" required autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} className="w-full pl-10 pr-4 py-3.5 rounded-xl bg-gray-50 dark:bg-[#0B0F19]/60 border border-gray-200 dark:border-white/[0.08] text-gray-900 dark:text-white" />
                    </div>
                    <button disabled={status === 'submitting'} className="w-full py-3.5 rounded-xl bg-[#6C63FF] text-white font-semibold disabled:opacity-50">
                        {status === 'submitting' ? <Loader2 className="w-4 h-4 mx-auto animate-spin" /> : t('identity.forgot.submit')}
                    </button>
                </form>
            )}
        </AffiliateIdentityShell>
    )
}
