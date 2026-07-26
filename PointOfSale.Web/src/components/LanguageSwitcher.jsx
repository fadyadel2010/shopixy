import { useTranslation } from 'react-i18next'

export default function LanguageSwitcher() {
    const { t, i18n } = useTranslation()
    const isArabic = (i18n.resolvedLanguage || i18n.language || '').startsWith('ar')

    const toggleLanguage = () => {
        const newLang = isArabic ? 'en' : 'ar'
        void i18n.changeLanguage(newLang)
    }

    return (
        <button
            onClick={toggleLanguage}
            className="flex items-center gap-2 px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg transition-colors"
            title={isArabic ? t('languageSwitcher.switchToEnglish') : t('languageSwitcher.switchToArabic')}
        >
            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 5h12M9 3v2m1.048 9.5A18.022 18.022 0 016.412 9m6.088 9h7M11 21l5-10 5 10M12.751 5C11.783 10.77 8.07 15.61 3 18.129" />
            </svg>
            <span className="font-semibold">
                {isArabic ? t('languageSwitcher.shortEnglish') : t('languageSwitcher.shortArabic')}
            </span>
        </button>
    )
}
