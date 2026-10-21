import { Link } from 'react-router-dom'
import { Star, X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useRTL } from '../hooks/useRTL'

export function SidebarFavorites({ navigation, onNavigate }) {
    const { t } = useTranslation()
    const { language } = useRTL()
    const entries = navigation.favorites.map(id => navigation.entries.find(entry => entry.id === id)).filter(Boolean)
    return <details className="mb-3 rounded-lg bg-white/10" data-testid="navigation-favorites">
        <summary className="cursor-pointer rounded-lg px-4 py-3 text-sm font-medium">
            <Star aria-hidden="true" className="mx-1 inline h-4 w-4" />{t('smartNavigation.favorites')} ({entries.length})
        </summary>
        <div className="max-h-52 space-y-1 overflow-y-auto px-2 pb-2">
            {!entries.length && <p className="px-2 py-2 text-xs text-indigo-100">{t('smartNavigation.noFavorites')}</p>}
            {entries.map(entry => <div key={entry.id} className="flex items-center gap-1">
                <Link to={entry.to} onClick={onNavigate} className="min-w-0 flex-1 rounded p-2 text-sm hover:bg-white/15">
                    <bdi className="block truncate">{entry.labels[language]}</bdi>
                    <bdi className="block truncate text-xs text-indigo-100">{entry.modules[language]}</bdi>
                    <bdi className="block truncate text-xs text-indigo-200" title={entry.breadcrumbs[language].join(' / ')}>{entry.breadcrumbs[language].slice(0, -1).join(' / ')}</bdi>
                </Link>
                <button type="button" onClick={() => navigation.toggleFavorite(entry.id)} className="shrink-0 rounded p-2 hover:bg-white/15"
                    aria-label={t('smartNavigation.removeFavorite', { title: entry.labels[language] })}
                    aria-description={`${entry.modules[language]} / ${entry.breadcrumbs[language].join(' / ')}`}>
                    <X aria-hidden="true" size={16} />
                </button>
            </div>)}
        </div>
    </details>
}
