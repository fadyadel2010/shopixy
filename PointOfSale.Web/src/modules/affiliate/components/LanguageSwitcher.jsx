/**
 * AF-15: Language Switcher Toggle
 * 
 * Compact toggle button for switching between English and Arabic.
 */
import { useLanguage } from '../i18n/LanguageContext'
import { Globe } from 'lucide-react'

export default function LanguageSwitcher({ className = '' }) {
    const { toggleLang, t } = useLanguage()

    return (
        <button
            onClick={toggleLang}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-gray-200 dark:border-white/[0.08] bg-white dark:bg-white/[0.03] hover:bg-white/[0.06] text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white text-xs font-medium transition-all ${className}`}
            title="Switch Language"
        >
            <Globe className="w-3.5 h-3.5" />
            {t('langLabel')}
        </button>
    )
}
