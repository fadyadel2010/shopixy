import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../../../i18n/config'
import { AddDomainWizardPage } from './AddDomainWizardPage'

const service = vi.hoisted(() => ({ createDomain: vi.fn(), startDomainVerification: vi.fn() }))
vi.mock('../services/domainManagementService', () => ({ default: service }))

async function review() {
    const user = userEvent.setup()
    render(<AddDomainWizardPage />)
    await user.type(screen.getByRole('textbox', { name: 'Hostname' }), 'phase1.example.test')
    for (let step = 1; step < 7; step++) await user.click(screen.getByRole('button', { name: 'Next' }))
    return user
}

describe('Domain wizard accepted creation and verification recovery', () => {
    beforeEach(async () => {
        vi.resetAllMocks()
        await i18n.changeLanguage('en')
        window.history.replaceState({}, '', '/storefront/domains/add')
        service.createDomain.mockResolvedValue({ domainId: '11111111-1111-4111-8111-111111111111', version: 1 })
        service.startDomainVerification.mockResolvedValue({ version: 2 })
    })
    afterEach(cleanup)

    it('navigates to verification after the accepted command, without blocking its own success navigation', async () => {
        const user = await review()
        await user.click(screen.getByRole('button', { name: 'Create Domain' }))
        await waitFor(() => expect(window.location.pathname).toBe('/storefront/domains/11111111-1111-4111-8111-111111111111/verification'))
    })

    it('retries only verification setup after a partial success, never creating a second domain', async () => {
        service.startDomainVerification.mockRejectedValueOnce({ response: { status: 503 } }).mockResolvedValueOnce({ version: 2 })
        const user = await review()
        await user.click(screen.getByRole('button', { name: 'Create Domain' }))
        await screen.findByText(/domain.*saved.*verification/i)
        await user.click(screen.getByRole('button', { name: /Retry verification setup/i }))
        await waitFor(() => expect(service.startDomainVerification).toHaveBeenCalledTimes(2))
        expect(service.createDomain).toHaveBeenCalledTimes(1)
    })

    it('issues one create for two same-tick activation events', async () => {
        let accept!: (value: unknown) => void
        service.createDomain.mockImplementation(() => new Promise(resolve => { accept = resolve }))
        await review()
        const create = screen.getByRole('button', { name: 'Create Domain' })
        act(() => { fireEvent.click(create); fireEvent.click(create) })
        expect(service.createDomain).toHaveBeenCalledTimes(1)
        await act(async () => accept({ domainId: '11111111-1111-4111-8111-111111111111', version: 1 }))
    })
})
