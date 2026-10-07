import { useTranslation } from 'react-i18next'

const defaults = { enabled: true, text: '', textAr: '', link: '', backgroundColor: '', textColor: '' }

export function HeaderAnnouncementSettings({ value, onChange }) {
    const { t, i18n } = useTranslation()
    const config = value || defaults
    const update = (key, next) => onChange({ ...config, [key]: next })
    const text = i18n.language === 'ar' && config.textAr ? config.textAr : config.text
    return <section className="min-w-0 space-y-4 rounded-lg border bg-white p-4 sm:p-6" aria-label={t('headerAnnouncement.title')}>
        <h2 className="text-lg font-semibold">{t('headerAnnouncement.title')}</h2>
        <p className="text-sm text-gray-600">{t('headerAnnouncement.description')}</p>
        {!value && <p className="text-sm text-gray-600">{t('headerAnnouncement.default')}</p>}
        {value && <button type="button" onClick={() => onChange(null)} className="rounded-lg border px-3 py-2 text-sm text-indigo-700">{t('headerAnnouncement.reset')}</button>}
        <label className="flex items-center gap-2"><input type="checkbox" checked={config.enabled} onChange={(event) => update('enabled', event.target.checked)} />{t('headerAnnouncement.enabled')}</label>
        <div className="grid min-w-0 gap-4 sm:grid-cols-2">
            {['text', 'textAr', 'link'].map((key) => <label key={key} className={`min-w-0 space-y-1 text-sm ${key === 'link' ? 'sm:col-span-2' : ''}`}>
                <span>{t(`headerAnnouncement.${key}`)}</span>
                <input value={config[key]} maxLength={key === 'link' ? 2048 : 500} dir={key === 'textAr' ? 'rtl' : 'ltr'}
                    onChange={(event) => update(key, event.target.value)} className="w-full min-w-0 rounded-lg border px-3 py-2" />
            </label>)}
            {['backgroundColor', 'textColor'].map((key) => <label key={key} className="min-w-0 space-y-1 text-sm">
                <span>{t(`headerAnnouncement.${key}`)}</span>
                <input type="color" value={config[key] || (key === 'textColor' ? '#ffffff' : '#111827')} onChange={(event) => update(key, event.target.value)} className="block h-10 w-full" />
                <button type="button" onClick={() => update(key, '')} className="text-xs text-indigo-600">{t('headerAnnouncement.themeDefault')}</button>
            </label>)}
        </div>
        {config.enabled && text && <div aria-label={t('headerAnnouncement.preview')} className="break-words rounded p-3 text-center" style={{ backgroundColor: config.backgroundColor || '#111827', color: config.textColor || '#ffffff' }}>{text}</div>}
    </section>
}
