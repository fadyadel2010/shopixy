import { CircleHelp } from 'lucide-react'
import { useTranslation } from 'react-i18next'

export default function HelpButton({ onClick, hasArticle = false }) {
    const { t } = useTranslation()

    return (
        <button
            type="button"
            onClick={onClick}
            className="inline-flex items-center gap-2 rounded-xl border border-indigo-200 bg-white px-4 py-2 text-sm font-semibold text-indigo-700 shadow-sm transition-colors hover:border-indigo-300 hover:bg-indigo-50"
            title={hasArticle ? t('common.help', 'Help') : t('common.helpUnavailable', 'Help article coming soon')}
        >
            <CircleHelp className="h-4 w-4" />
            <span>{t('common.help', 'Help')}</span>
            <span className={`rounded-full px-2 py-0.5 text-[11px] font-medium ${hasArticle ? 'bg-emerald-100 text-emerald-700' : 'bg-amber-100 text-amber-700'}`}>
                {hasArticle ? t('common.available', 'Available') : t('common.soon', 'Soon')}
            </span>
        </button>
    )
}
