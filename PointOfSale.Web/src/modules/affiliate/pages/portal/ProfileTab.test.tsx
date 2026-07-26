import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ProfileTab from './ProfileTab'

const getProfileMock = vi.hoisted(() => vi.fn())
const updatePayoutWalletMock = vi.hoisted(() => vi.fn())

vi.mock('../../../../services/affiliateApi', () => ({
    default: {
        getProfile: getProfileMock,
        updatePayoutWallet: updatePayoutWalletMock,
    },
}))

vi.mock('../../i18n/LanguageContext', () => ({
    useLanguage: () => ({
        t: (key: string) => key,
    }),
}))

vi.mock('framer-motion', () => ({
    motion: {
        div: ({ children, ...props }: { children: React.ReactNode }) => <div {...props}>{children}</div>,
    },
}))

const profile = (overrides = {}) => ({
    affiliateAccountId: '11111111-1111-4111-8111-111111111111',
    displayName: 'Backend Affiliate',
    email: 'affiliate@example.test',
    referralCode: 'AFF123',
    status: 'Active',
    createdAt: '2026-06-25T00:00:00Z',
    payoutWalletNumber: '01012345678',
    payoutWalletType: 'VodafoneCash',
    payoutWalletUpdatedAtUtc: '2026-06-25T00:00:00Z',
    ...overrides,
})

const deferred = <T,>() => {
    let resolve!: (value: T) => void
    let reject!: (reason?: unknown) => void
    const promise = new Promise<T>((promiseResolve, promiseReject) => {
        resolve = promiseResolve
        reject = promiseReject
    })
    return { promise, resolve, reject }
}

describe('Affiliate ProfileTab candidate authority', () => {
    beforeEach(() => {
        getProfileMock.mockReset()
        updatePayoutWalletMock.mockReset()
    })

    it('promotes payout wallet edits only after a fresh backend profile baseline is accepted', async () => {
        const acceptedProfile = profile()
        const refreshedProfile = profile({
            payoutWalletNumber: 'IBAN123456',
            payoutWalletType: 'Bank',
            payoutWalletUpdatedAtUtc: '2026-06-25T01:00:00Z',
        })
        const profileRefresh = deferred<typeof refreshedProfile>()

        getProfileMock
            .mockResolvedValueOnce(acceptedProfile)
            .mockReturnValueOnce(profileRefresh.promise)
        updatePayoutWalletMock.mockResolvedValue({ message: 'Payout wallet updated successfully' })

        render(<ProfileTab />)

        await screen.findByText('010*****678')

        fireEvent.click(screen.getByRole('button', { name: /edit payout wallet/i }))
        fireEvent.change(screen.getByLabelText('Wallet Type'), { target: { value: 'Bank' } })
        fireEvent.change(screen.getByLabelText('Wallet Number'), { target: { value: 'IBAN123456' } })
        fireEvent.click(screen.getByRole('button', { name: /save wallet/i }))

        await waitFor(() => {
            expect(updatePayoutWalletMock).toHaveBeenCalledWith('IBAN123456', 'Bank')
        })

        expect(screen.queryByText('Wallet updated successfully')).not.toBeInTheDocument()
        expect(screen.queryByText('IBA*****456')).not.toBeInTheDocument()

        profileRefresh.resolve(refreshedProfile)

        await screen.findByText('Wallet updated successfully')
        expect(screen.getByText('IBA*****456')).toBeInTheDocument()
    })
})
