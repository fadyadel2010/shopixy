import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import Login from './Login'
import { useAuthStore } from '../store/authStore'

vi.mock('../contexts/SettingsContext', () => ({
    useSettings: () => ({ reloadSettings: vi.fn() }),
}))
vi.mock('../services/api', () => ({
    default: { get: vi.fn(), post: vi.fn() },
}))
vi.mock('../components/LanguageSwitcher', () => ({
    default: () => null,
}))
vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: (key: string) => key }),
}))

describe('Login account switching', () => {
    afterEach(() => {
        useAuthStore.setState({ user: null, isAuthenticated: false, hasHydrated: true })
    })

    it('does not redirect an authenticated session during an explicit account switch', () => {
        useAuthStore.setState({
            user: { id: 'owner-1', role: 'Owner' },
            isAuthenticated: true,
            hasHydrated: true,
        })

        render(
            <MemoryRouter initialEntries={['/login?switchAccount=1']}>
                <Routes>
                    <Route path="/login" element={<Login />} />
                    <Route path="/" element={<div>Dashboard destination</div>} />
                </Routes>
            </MemoryRouter>,
        )

        expect(screen.getByText('login.usernameOrEmail')).toBeInTheDocument()
        expect(screen.queryByText('Dashboard destination')).not.toBeInTheDocument()
    })

    it('associates each visible credential label with its input', () => {
        render(
            <MemoryRouter initialEntries={['/login?switchAccount=1']}>
                <Login />
            </MemoryRouter>,
        )

        expect(screen.getByLabelText('login.usernameOrEmail')).toHaveAttribute('id', 'pos-login-username')
        expect(screen.getByLabelText('login.password')).toHaveAttribute('id', 'pos-login-password')
    })
})
