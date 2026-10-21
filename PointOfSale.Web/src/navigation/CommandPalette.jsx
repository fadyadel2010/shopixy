import { useEffect, useId, useMemo, useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { useLocation, useNavigate } from 'react-router-dom'
import { Search, Star, X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useRTL } from '../hooks/useRTL'
import { useSmartNavigation } from './SmartNavigationContext'
import { searchNavigation } from './navigationRegistry'
import { FAVORITES_LIMIT } from './navigationPreferences'

export function canOpenNavigationShortcut(event, document) {
    if (!event.ctrlKey || event.metaKey || event.altKey || event.shiftKey || event.repeat || event.isComposing
        || event.key.toLowerCase() !== 'k' || event.defaultPrevented) return false
    const target = event.target
    if (target?.closest?.('input, textarea, select, [contenteditable]:not([contenteditable="false"]), [role="textbox"]')) return false
    return ![...document.querySelectorAll('[role="dialog"], [aria-modal="true"]')]
        .some(dialog => dialog.getAttribute('data-state') !== 'closed' && !dialog.hidden)
}

export default function CommandPalette({ onNavigate }) {
    const navigation = useSmartNavigation()
    const { t } = useTranslation()
    const { language, dir } = useRTL()
    const navigate = useNavigate()
    const location = useLocation()
    const [open, setOpen] = useState(false)
    const [query, setQuery] = useState('')
    const [tab, setTab] = useState('all')
    const [selected, setSelected] = useState(0)
    const input = useRef(null)
    const restoreFocus = useRef(null)
    const pendingLocation = useRef(null)
    const listId = useId()
    const openPalette = () => {
        restoreFocus.current = document.activeElement
        setQuery('')
        setTab('all')
        setSelected(0)
        setOpen(true)
    }
    useEffect(() => {
        const shortcut = event => {
            if (!canOpenNavigationShortcut(event, document)) return
            event.preventDefault()
            openPalette()
        }
        window.addEventListener('keydown', shortcut)
        return () => window.removeEventListener('keydown', shortcut)
    }, [])
    // Drop stale dialog/search state immediately when session authority changes.
    useEffect(() => { setOpen(false); setQuery(''); pendingLocation.current = null }, [navigation.user])
    useEffect(() => {
        if (pendingLocation.current !== null && location.key !== pendingLocation.current) {
            pendingLocation.current = null
            setOpen(false)
        }
    }, [location.key])

    const results = useMemo(() => {
        const byId = ids => ids.map(id => navigation.entries.find(entry => entry.id === id)).filter(Boolean)
        const candidates = tab === 'favorites' ? byId(navigation.favorites)
            : tab === 'recent' ? byId(navigation.recent)
                : !query.trim() ? [...byId(navigation.recent), ...navigation.entries.filter(entry => !navigation.recent.includes(entry.id))]
                    : navigation.entries
        return searchNavigation(candidates, query)
    }, [navigation.entries, navigation.favorites, navigation.recent, query, tab])
    const active = Math.min(selected, Math.max(0, results.length - 1))
    useEffect(() => { document.getElementById(`${listId}-${active}`)?.scrollIntoView?.({ block: 'nearest' }) }, [active, listId, query, tab])
    const launch = entry => {
        // Revalidate identity against current authority; router still owns admission.
        const current = navigation.entries.find(value => value.id === entry.id)
        if (!current) return
        if (location.pathname === current.to.pathname && location.search === (current.to.search || '')) {
            setOpen(false)
            return
        }
        pendingLocation.current = location.key
        navigate(current.to)
        onNavigate?.()
    }
    const keyboard = event => {
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault()
            if (results.length) setSelected((active + (event.key === 'ArrowDown' ? 1 : -1) + results.length) % results.length)
        } else if (event.key === 'Enter' && results[active] && !event.nativeEvent.isComposing) {
            event.preventDefault()
            launch(results[active])
        }
    }
    return <Dialog.Root open={open} onOpenChange={value => value ? openPalette() : setOpen(false)}>
        <Dialog.Trigger asChild>
            <button type="button" title={t('smartNavigation.search')} aria-label={t('smartNavigation.search')}
                className="flex shrink-0 items-center gap-2 rounded-lg border border-gray-200 p-2 text-sm text-gray-600 hover:bg-gray-100">
                <Search aria-hidden="true" size={18} /><span className="hidden xl:inline">{t('smartNavigation.search')}</span>
                <kbd dir="ltr" className="hidden rounded bg-gray-100 px-1 text-xs lg:inline">Ctrl K</kbd>
            </button>
        </Dialog.Trigger>
        <Dialog.Portal>
            <Dialog.Overlay className="fixed inset-0 z-[70] bg-slate-950/50" />
            <Dialog.Content dir={dir} data-testid="navigation-palette" onOpenAutoFocus={event => { event.preventDefault(); input.current?.focus() }}
                onCloseAutoFocus={event => { event.preventDefault(); if (restoreFocus.current?.isConnected) restoreFocus.current.focus() }}
                className="fixed start-1/2 top-[8vh] z-[71] flex max-h-[84dvh] w-[calc(100%-2rem)] max-w-2xl -translate-x-1/2 flex-col rounded-xl bg-white p-4 text-start shadow-2xl rtl:translate-x-1/2">
                <div className="flex items-center justify-between gap-2">
                    <Dialog.Title className="text-lg font-semibold text-gray-900">{t('smartNavigation.title')}</Dialog.Title>
                    <Dialog.Close aria-label={t('smartNavigation.close')} className="rounded p-2 hover:bg-gray-100"><X size={20} /></Dialog.Close>
                </div>
                <Dialog.Description className="mb-3 text-xs text-gray-500">{t('smartNavigation.instructions')}</Dialog.Description>
                <input ref={input} value={query} maxLength={200} onChange={event => { setQuery(event.target.value); setSelected(0) }} onKeyDown={keyboard}
                    role="combobox" aria-expanded="true" aria-haspopup="grid" aria-controls={listId}
                    aria-activedescendant={results.length ? `${listId}-${active}` : undefined}
                    aria-label={t('smartNavigation.search')} placeholder={t('smartNavigation.placeholder')} autoComplete="off"
                    className="w-full rounded-lg border border-gray-300 p-3 text-sm outline-none focus:ring-2 focus:ring-indigo-500" />
                <div className="my-3 flex flex-wrap items-center gap-2">
                    {['all', 'favorites', 'recent'].map(value => <button type="button" key={value} aria-pressed={tab === value}
                        onClick={() => { setTab(value); setSelected(0); input.current?.focus() }}
                        className={`rounded-lg px-3 py-1.5 text-sm ${tab === value ? 'bg-indigo-100 text-indigo-800' : 'text-gray-600 hover:bg-gray-100'}`}>
                        {t(`smartNavigation.${value}`)}
                    </button>)}
                    {(tab === 'recent' || (tab === 'all' && !query.trim())) && navigation.recent.length > 0 && <button type="button"
                        onClick={navigation.clearRecent} className="ms-auto rounded p-1 text-xs text-gray-500 hover:bg-gray-100">{t('smartNavigation.clearRecent')}</button>}
                </div>
                <p role="status" className="mb-2 text-xs text-gray-500">{t('smartNavigation.resultCount', { count: results.length })}</p>
                <div className="min-h-0 overflow-y-auto overscroll-contain" id={listId} role="grid" aria-label={t('smartNavigation.results')}>
                    {!results.length && <div role="row"><div role="gridcell" className="p-6 text-center text-sm text-gray-500">{t(query.trim() ? 'smartNavigation.noResults'
                        : tab === 'favorites' ? 'smartNavigation.noFavorites' : tab === 'recent' ? 'smartNavigation.noRecent' : 'smartNavigation.unavailable')}</div></div>}
                    {results.map((entry, index) => <div key={entry.id}>
                        {tab === 'all' && !query.trim() && (index === 0 || (navigation.recent.includes(results[index - 1]?.id) && !navigation.recent.includes(entry.id)))
                            && <div role="row"><div role="gridcell" aria-colspan="2"><h3 className="px-2 py-2 text-xs font-semibold text-gray-500">{t(navigation.recent.includes(entry.id) ? 'smartNavigation.recent' : 'smartNavigation.all')}</h3></div></div>}
                        <div role="row" id={`${listId}-${index}`} aria-selected={index === active} data-navigation-id={entry.id}
                            className={`flex items-center rounded-lg ${index === active ? 'bg-indigo-50 ring-1 ring-inset ring-indigo-300' : 'hover:bg-gray-50'}`}>
                            <div role="gridcell" className="min-w-0 flex-1">
                                <button type="button" onClick={() => launch(entry)}
                                    aria-label={`${entry.labels[language]} — ${entry.modules[language]} — ${entry.breadcrumbs[language].join(' / ')}`}
                                    className="w-full min-w-0 rounded-lg p-3 text-start focus-visible:outline-indigo-500">
                                    <span className="block text-sm font-semibold text-gray-900"><bdi>{entry.labels[language]}</bdi></span>
                                    <span className="block text-xs font-medium text-indigo-700"><bdi>{entry.modules[language]}</bdi></span>
                                    <span className="block break-words text-xs text-gray-500">{entry.breadcrumbs[language].map((label, i) => <span key={i}>{i > 0 && <span aria-hidden="true"> / </span>}<bdi>{label}</bdi></span>)}</span>
                                </button>
                            </div>
                            <div role="gridcell" className="shrink-0 pe-2">
                                <button type="button" aria-pressed={navigation.favorites.includes(entry.id)}
                                    disabled={!navigation.favorites.includes(entry.id) && navigation.favorites.length >= FAVORITES_LIMIT}
                                    aria-label={t(navigation.favorites.includes(entry.id) ? 'smartNavigation.removeFavorite' : 'smartNavigation.addFavorite', { title: entry.labels[language] })}
                                    title={navigation.favorites.length >= FAVORITES_LIMIT ? t('smartNavigation.favoriteLimit', { count: FAVORITES_LIMIT }) : undefined}
                                    onClick={() => navigation.toggleFavorite(entry.id)} className="rounded p-2 text-indigo-600 hover:bg-indigo-100 disabled:opacity-40">
                                    <Star aria-hidden="true" size={18} fill={navigation.favorites.includes(entry.id) ? 'currentColor' : 'none'} />
                                </button>
                            </div>
                        </div>
                    </div>)}
                </div>
            </Dialog.Content>
        </Dialog.Portal>
    </Dialog.Root>
}
