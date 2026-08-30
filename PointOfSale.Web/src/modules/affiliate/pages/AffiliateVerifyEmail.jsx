import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { AlertCircle, CheckCircle2, Loader2 } from 'lucide-react'
import { affiliateAuthApi } from '../../../services/affiliateAuthApi'
import AffiliateIdentityShell from '../components/AffiliateIdentityShell'
import { useLanguage } from '../i18n/LanguageContext'

export default function AffiliateVerifyEmail() {
    const { t } = useLanguage()
    const [searchParams] = useSearchParams()
    const token = searchParams.get('token')?.trim() || ''
    const [status, setStatus] = useState(token ? 'verifying' : 'error')
    const [message, setMessage] = useState(token ? '' : t('identity.verify.missingToken'))
    const startedRef = useRef(false)

    useEffect(() => {
        if (!token || startedRef.current) return
        startedRef.current = true

        affiliateAuthApi.verifyEmail(token)
            .then(() => {
                setStatus('success')
                setMessage(t('identity.verify.success'))
            })
            .catch((error) => {
                setStatus('error')
                setMessage(t('identity.verify.failed'))
            })
    }, [t, token])

    return (
        <AffiliateIdentityShell title={t('identity.verify.title')} subtitle={t('identity.verify.subtitle')}>
            <div className="text-center">
                {status === 'verifying' && <Loader2 className="w-10 h-10 mx-auto text-[#6C63FF] animate-spin" />}
                {status === 'success' && <CheckCircle2 className="w-10 h-10 mx-auto text-emerald-500" />}
                {status === 'error' && <AlertCircle className="w-10 h-10 mx-auto text-red-500" />}
                <p className="mt-4 text-sm text-gray-600 dark:text-gray-300">
                    {status === 'verifying' ? t('identity.verify.verifying') : message}
                </p>
                {status !== 'verifying' && (
                    <Link className="inline-flex mt-6 px-5 py-3 rounded-xl bg-[#6C63FF] text-white text-sm font-semibold" to="/affiliate/login">
                        {t('identity.signIn')}
                    </Link>
                )}
            </div>
        </AffiliateIdentityShell>
    )
}
