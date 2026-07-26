import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import MarketplaceLanguageSwitcher from './MarketplaceLanguageSwitcher'

export function MarketplaceHeader({
    language,
    statusLabel = null,
    badge,
    title,
    description = null,
    aside = null,
    children = null,
    showBrowseLink = true,
}) {
    const { t } = useTranslation()

    return (
        <header className="rounded-[40px] border border-amber-100 bg-white/95 px-8 py-10 shadow-[0_30px_90px_-50px_rgba(120,53,15,0.35)] backdrop-blur">
            <div className="flex flex-wrap items-start justify-between gap-6">
                <div className={language === 'ar' ? 'text-right' : 'text-left'}>
                    <div className={`flex flex-wrap items-center gap-3 ${language === 'ar' ? 'justify-end' : 'justify-start'}`}>
                        <Link
                            to={`/${language}/products`}
                            className="inline-flex rounded-full border border-amber-200 bg-amber-50 px-4 py-2 text-[11px] font-semibold uppercase tracking-[0.24em] text-amber-800 transition hover:border-amber-300 hover:bg-amber-100"
                        >
                            {t('marketplacePublic.header.brand', { lng: language })}
                        </Link>
                        {statusLabel ? (
                            <span className="inline-flex rounded-full border border-slate-200 bg-slate-50 px-3 py-2 text-[11px] font-semibold uppercase tracking-[0.22em] text-slate-600">
                                {statusLabel}
                            </span>
                        ) : null}
                    </div>
                    <p className="mt-5 text-xs font-semibold uppercase tracking-[0.24em] text-amber-700">{badge}</p>
                    <h1 className="mt-3 text-4xl font-semibold tracking-tight text-slate-950">{title}</h1>
                    {description ? (
                        <p className="mt-4 max-w-3xl text-base leading-7 text-slate-600">{description}</p>
                    ) : null}
                </div>

                <div className={`flex flex-col gap-4 ${language === 'ar' ? 'items-end' : 'items-start'}`}>
                    <MarketplaceLanguageSwitcher currentLanguage={language} />
                    {aside}
                    {showBrowseLink ? (
                        <Link
                            to={`/${language}/products`}
                            className="inline-flex items-center gap-2 text-sm font-medium text-slate-500 transition hover:text-slate-950"
                        >
                            <span aria-hidden="true">{language === 'ar' ? '\u2190' : '\u2192'}</span>
                            <span>{t('marketplacePublic.header.browseProducts', { lng: language })}</span>
                        </Link>
                    ) : null}
                </div>
            </div>

            {children ? <div className="mt-6">{children}</div> : null}
        </header>
    )
}

export default MarketplaceHeader
