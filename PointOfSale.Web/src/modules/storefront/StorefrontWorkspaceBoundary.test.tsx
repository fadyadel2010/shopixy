import { act, fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, it } from 'vitest'
import { useAuthStore } from '../../store/authStore'
import { StorefrontWorkspaceBoundary } from './StorefrontWorkspaceBoundary'

function Candidate() {
    const [value, setValue] = useState('')
    return <input aria-label="Candidate" value={value} onChange={event => setValue(event.target.value)} />
}
afterEach(() => useAuthStore.setState({ user: null }))
it('clears candidates and confirmations when the authenticated tenant/account changes', () => {
    useAuthStore.setState({ user: { id: 'actor', tenantId: 'tenant-a' } })
    render(<MemoryRouter><Routes><Route element={<StorefrontWorkspaceBoundary />}><Route path="/" element={<Candidate />} /></Route></Routes></MemoryRouter>)
    fireEvent.change(screen.getByLabelText('Candidate'), { target: { value: 'Private tenant A draft' } })
    act(() => useAuthStore.setState({ user: { id: 'actor-b', tenantId: 'tenant-b' } }))
    expect(screen.getByLabelText('Candidate')).toHaveValue('')
})
