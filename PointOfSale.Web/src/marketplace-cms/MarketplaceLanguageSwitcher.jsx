import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router-dom'
import { buildMarketplaceLanguageUrl, setLanguage } from './marketplaceLanguage'

export function MarketplaceLanguageSwitcher({ currentLanguage }) {
    const { t } = useTranslation()
    const location = useLocation()

    return (
        <div className="inline-flex items-center gap-2 rounded-2xl border border-slate-200 bg-white p-1 shadow-sm">
            <span className="px-2 text-sm" aria-hidden="true">
                {'\ud83c\udf10'}
            </span>
            {['en', 'ar'].map((language) => (
                <Link
                    key={language}
                    to={buildMarketplaceLanguageUrl(location.pathname, location.search, language)}
                    onClick={() => setLanguage(language)}
                    aria-current={currentLanguage === language ? 'page' : undefined}
                    title={language === 'ar' ? t('languageSwitcher.switchToArabic') : t('languageSwitcher.switchToEnglish')}
                    className={[
                        'rounded-xl px-4 py-2 text-sm font-semibold transition-all duration-200',
                        currentLanguage === language
                            ? 'bg-slate-900 text-white shadow-sm'
                            : 'text-slate-600 hover:bg-slate-50 hover:text-slate-950',
                    ].join(' ')}
                >
                    {language === 'ar' ? t('languageSwitcher.shortArabic') : t('languageSwitcher.shortEnglish')}
                </Link>
            ))}
        </div>
    )
}

export default MarketplaceLanguageSwitcher
