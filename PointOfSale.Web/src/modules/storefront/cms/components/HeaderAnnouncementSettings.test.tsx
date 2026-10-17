import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { HeaderAnnouncementSettings } from './HeaderAnnouncementSettings'
import { admitNavigationSnapshot, mapNavigationDraftToRequest } from '../services/navigationAuthoringContract'
import en from '../../../../i18n/locales/en.json'
import ar from '../../../../i18n/locales/ar.json'
import { readTranslation } from '../../../../tests/readTranslation'

const locale = vi.hoisted(() => ({ language: 'en' }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ i18n: locale, t: (key: string) => readTranslation(locale.language === 'ar' ? ar : en, key) }) }))
afterEach(() => { cleanup(); locale.language = 'en' })

it.each(['en', 'ar'])('Header owns announcement controls and localized preview in %s', (language) => {
    locale.language = language
    const messages = language === 'ar' ? ar : en
    const config = { enabled: true, text: 'Collection', textAr: 'مجموعة', link: '/products', backgroundColor: '#123456', textColor: '#ffffff' }
    const onChange = vi.fn()
    render(<HeaderAnnouncementSettings value={config} onChange={onChange} />)
    expect(screen.getByLabelText(messages.headerAnnouncement.preview)).toHaveTextContent(language === 'ar' ? 'مجموعة' : 'Collection')
    fireEvent.click(screen.getByLabelText(messages.headerAnnouncement.enabled))
    expect(onChange).toHaveBeenLastCalledWith({ ...config, enabled: false })
    fireEvent.change(screen.getByLabelText(messages.headerAnnouncement.link), { target: { value: '/offers' } })
    expect(onChange).toHaveBeenLastCalledWith({ ...config, link: '/offers' })
    const snapshot = admitNavigationSnapshot({ rowVersion: '7', draft: { announcement: config, header: [], footer: [] }, live: null })
    expect(mapNavigationDraftToRequest(snapshot).announcement).toEqual(config)
})
