import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import HomepageManagerPage from './HomepageManagerPage'

describe('HomepageManagerPage ownership', () => {
    it('redirects the legacy mutation surface to the canonical Homepage Builder', async () => {
        render(
            <MemoryRouter initialEntries={['/super-admin/homepage/manager']}>
                <Routes>
                    <Route path="/super-admin/homepage/manager" element={<HomepageManagerPage />} />
                    <Route path="/super-admin/homepage" element={<div>Canonical Homepage Builder</div>} />
                </Routes>
            </MemoryRouter>,
        )

        expect(await screen.findByText('Canonical Homepage Builder')).toBeInTheDocument()
    })
})
