import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../../../i18n/config'
import { DomainHistoryPage, RedirectsPage } from './index'

const state = vi.hoisted(() => ({ isNotFound: true, navigate: vi.fn() }))
vi.mock('react-router-dom', () => ({ useParams: () => ({ id: '11111111-1111-4111-8111-111111111111' }) }))
vi.mock('../hooks', () => ({ useDomainHistory: () => state, useDomainRedirects: () => state }))
vi.mock('../../authoringPlatform', async importOriginal => ({ ...await importOriginal<Record<string, unknown>>(), useAuthoringGuard: () => ({ requestNavigation: state.navigate }) }))

describe('Domain auxiliary screens preserve not-found semantics', () => {
    beforeEach(async () => { state.navigate.mockClear(); await i18n.changeLanguage('en') })
    afterEach(cleanup)
    for (const [name, Page] of [['History', DomainHistoryPage], ['Redirects', RedirectsPage]] as const) {
        it(`${name} reports a missing tenant-scoped domain and offers a safe portfolio return`, () => {
            render(<Page />)
            expect(screen.getByRole('heading', { name: 'Domain not found' })).toBeInTheDocument()
            expect(screen.queryByText('example.com')).not.toBeInTheDocument()
            fireEvent.click(screen.getByRole('button', { name: 'Back' }))
            expect(state.navigate).toHaveBeenCalledWith('/storefront/domains')
        })
    }
})
