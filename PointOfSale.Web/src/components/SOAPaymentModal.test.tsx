import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import SOAPaymentModal from './SOAPaymentModal.jsx'

vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: (key: string) => key }),
}))

vi.mock('../contexts/SettingsContext', () => ({
    useSettings: () => ({ formatCurrency: (value: number) => `EGP ${Number(value).toFixed(2)}` }),
}))

vi.mock('../hooks/useRTL', () => ({
    useRTL: () => ({ isRTL: false, dir: 'ltr' }),
}))

const instrument = {
    id: 'instrument-1',
    displayName: 'Company Visa',
    last4: '4521',
    authorityVersion: 4,
    availableBalance: 250,
    fundingSourceName: 'Main Bank',
}

const renderModal = (overrides = {}) => {
    const onSubmit = vi.fn()
    render(<SOAPaymentModal
        isOpen
        onClose={vi.fn()}
        onSubmit={onSubmit}
        partyName="Supplier A"
        currentBalance={100}
        balanceType="Credit"
        type="make"
        loading={false}
        cardInstruments={[instrument]}
        {...overrides}
    />)
    return onSubmit
}

describe('supplier payment card funding integration', () => {
    it('preserves supplier Cash payment behavior without card authority', async () => {
        const user = userEvent.setup()
        const onSubmit = renderModal()
        await user.type(screen.getByLabelText('cashbox.amountRequired'), '45')
        await user.click(screen.getByRole('button', { name: 'accounts.makePayment' }))

        expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ amount: 45, paymentMethod: 'Cash' }))
        expect(onSubmit.mock.calls[0][0]).not.toHaveProperty('fundingInstrumentId')
    })

    it('does not silently select a supplier card and preserves explicit identity', async () => {
        const user = userEvent.setup()
        const onSubmit = renderModal()
        await user.selectOptions(screen.getByLabelText('accounts.paymentMethodRequired'), 'Card')

        const instrumentSelect = screen.getByLabelText('purchases.fundingInstrument')
        expect(instrumentSelect).toHaveValue('')

        await user.type(screen.getByLabelText('cashbox.amountRequired'), '60')
        await user.selectOptions(instrumentSelect, 'instrument-1')
        await user.click(screen.getByRole('button', { name: 'accounts.makePayment' }))

        expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({
            amount: 60,
            paymentMethod: 'Card',
            fundingInstrumentId: 'instrument-1',
            fundingInstrumentVersion: 4,
        }))
    })

    it('blocks Card when no active instrument exists', async () => {
        const user = userEvent.setup()
        const onSubmit = renderModal({ cardInstruments: [] })
        await user.selectOptions(screen.getByLabelText('accounts.paymentMethodRequired'), 'Card')
        expect(screen.getByText('purchases.noCardInstruments')).toBeInTheDocument()
        await user.type(screen.getByLabelText('cashbox.amountRequired'), '20')
        await user.click(screen.getByRole('button', { name: 'accounts.makePayment' }))
        expect(onSubmit).not.toHaveBeenCalled()
    })

    it('leaves customer Card behavior free of supplier funding authority', async () => {
        const user = userEvent.setup()
        const onSubmit = renderModal({ type: 'receive', partyName: 'Customer A', cardInstruments: [] })
        await user.selectOptions(screen.getByLabelText('accounts.paymentMethodRequired'), 'Card')
        await user.type(screen.getByLabelText('cashbox.amountRequired'), '30')
        await user.click(screen.getByRole('button', { name: 'accounts.receivePayment' }))

        expect(screen.queryByLabelText('purchases.fundingInstrument')).not.toBeInTheDocument()
        expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ amount: 30, paymentMethod: 'Card' }))
        expect(onSubmit.mock.calls[0][0]).not.toHaveProperty('fundingInstrumentId')
    })
})
