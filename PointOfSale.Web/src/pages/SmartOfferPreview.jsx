import { useTranslation } from 'react-i18next'

export default function SmartOfferPreview({ offer, dir = 'ltr' }) {
    const { t } = useTranslation()
    // Preview only the public display DTO. Coupon codes are private; Storefront
    // claims a server-verified token instead of displaying the configured code.
    return (
        <div dir={dir} className="overflow-hidden rounded-xl border border-gray-200 bg-white shadow-sm">
            <div className="relative px-5 py-6" style={{ background: `radial-gradient(circle at top, rgba(255,255,255,0.68), rgba(255,255,255,0.14)), ${offer.backgroundColor.trim() || '#f97316'}` }}>
                <div className="space-y-4 break-words text-slate-950 [overflow-wrap:anywhere]">
                    <span className="inline-flex rounded-full bg-slate-950 px-3 py-1 text-xs font-semibold text-white">{t('SmartOfferPreview.badge')}</span>
                    <h3 className="text-2xl font-semibold leading-tight">{offer.popupTitle.trim() || t('SmartOfferPreview.emptyTitle')}</h3>
                    {offer.popupDescription.trim() && <p className="whitespace-pre-wrap text-sm leading-7 text-slate-800">{offer.popupDescription.trim()}</p>}
                    <span className="inline-flex rounded-xl bg-white/80 px-3 py-2 text-xs font-semibold text-slate-600">{t('SmartOfferPreview.verified')}</span>
                    <div className="flex flex-wrap gap-2">
                        <span className="rounded-xl bg-slate-950 px-4 py-2 text-sm font-semibold text-white">{offer.buttonText.trim() || t('SmartOfferPreview.emptyButton')}</span>
                        <span className="rounded-xl border border-slate-300 bg-white/70 px-4 py-2 text-sm text-slate-700">{t('common.close')}</span>
                    </div>
                </div>
            </div>
            {offer.imageUrl.trim() && <img key={offer.imageUrl.trim()} src={offer.imageUrl.trim()} alt={offer.popupTitle.trim()} className="max-h-52 w-full object-cover" />}
        </div>
    )
}
