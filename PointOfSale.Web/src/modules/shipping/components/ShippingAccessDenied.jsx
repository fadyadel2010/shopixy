import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

export default function ShippingAccessDenied({ reason }) {
    const { t } = useTranslation()

    return (
        <section role="alert" className="mx-auto max-w-2xl rounded-xl border border-amber-200 bg-amber-50 p-6">
            <h1 className="text-xl font-semibold text-amber-900">{t('shippingAccess.title')}</h1>
            <p className="mt-3 text-amber-900">
                {t(reason === 'permission' ? 'shippingAccess.permission' : 'shippingAccess.storefront')}
            </p>
            <p className="mt-2 text-sm text-amber-800">{t('shippingAccess.help')}</p>
            <Link to="/" className="mt-5 inline-block font-medium text-indigo-700 underline">
                {t('shippingAccess.home')}
            </Link>
        </section>
    )
}
