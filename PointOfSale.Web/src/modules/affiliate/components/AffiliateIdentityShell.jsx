import { Link } from 'react-router-dom'
import { DollarSign } from 'lucide-react'
import LanguageSwitcher from './LanguageSwitcher'
import ThemeSwitcher from './ThemeSwitcher'
import { useLanguage } from '../i18n/LanguageContext'

export default function AffiliateIdentityShell({ title, subtitle, children }) {
    const { dir, isRTL, t } = useLanguage()

    return (
        <div
            dir={dir}
            className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-[#0B0F19] px-4 py-12 relative overflow-hidden"
            style={{ fontFamily: isRTL ? "'Noto Kufi Arabic', 'Inter', sans-serif" : "'Inter', -apple-system, system-ui, sans-serif" }}
        >
            <div className="absolute top-4 right-4 z-20 flex items-center gap-3">
                <ThemeSwitcher />
                <LanguageSwitcher />
            </div>
            <div className="absolute top-0 left-1/2 -translate-x-1/2 w-[800px] h-[600px] rounded-full bg-[radial-gradient(circle,rgba(108,99,255,0.12)_0%,transparent_65%)] blur-3xl pointer-events-none" />
            <main className="relative z-10 w-full max-w-[440px] bg-white/90 dark:bg-[#121826]/80 backdrop-blur-xl border border-gray-200 dark:border-white/[0.06] rounded-2xl p-8 shadow-2xl shadow-black/20">
                <div className="w-14 h-14 rounded-2xl bg-gradient-to-br from-[#6C63FF] to-[#8B5CF6] flex items-center justify-center mx-auto mb-5 shadow-lg shadow-[#6C63FF]/20">
                    <DollarSign className="w-7 h-7 text-white" />
                </div>
                <h1 className="text-2xl font-bold text-center text-gray-900 dark:text-white">{title}</h1>
                {subtitle && <p className="text-sm text-center text-gray-500 mt-2 mb-7">{subtitle}</p>}
                {children}
                <p className="text-center text-xs text-gray-500 mt-7">
                    <Link to="/affiliate" className="hover:text-[#6C63FF]">{t('identity.backToHub')}</Link>
                </p>
            </main>
        </div>
    )
}
