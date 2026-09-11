import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ConnectionResultPanel } from './components/ConnectionResultPanel'
import { DevelopmentOnlinePaymentSimulationPanel } from './components/DevelopmentOnlinePaymentSimulationPanel'
import { OperationalMessages } from './components/OperationalMessages'
import { ProviderHealthCard } from './components/ProviderHealthCard'
import { ProviderSessionReconciliationPanel } from './components/ProviderSessionReconciliationPanel'
import { TestConnectionButton } from './components/TestConnectionButton'
import type { PaymentProviderActionResult, PaymentProviderHealth } from './paymentProviderTypes'

describe('payment provider health components', () => {
    it('renders health, operational messages, test action, and latest result', () => {
        const result: PaymentProviderActionResult = {
            success: true,
            status: 'Healthy',
            provider: 'PAYMOB',
            environment: 'Sandbox',
            message: 'Provider authentication and profile verification succeeded.',
            checkedAtUtc: new Date().toISOString(),
            latencyMs: 42,
            errorCategory: '',
            warnings: [],
            errors: [],
            providerVersion: 'Paymob Accept API',
            capabilities: [],
        }
        const health: PaymentProviderHealth = {
            status: 'Healthy',
            environment: 'Sandbox',
            lastSuccessfulCheckUtc: result.checkedAtUtc,
            lastFailedCheckUtc: null,
            lastCheckDurationMs: 42,
            providerVersion: 'Paymob Accept API',
            connectionQuality: 'Excellent',
            operationalMessages: ['Provider profile verified.'],
            latestResult: result,
            previousResult: null,
        }

        render(
            <>
                <ProviderHealthCard health={health} />
                <OperationalMessages health={health} />
                <TestConnectionButton running={false} disabled={false} onClick={vi.fn()} />
                <ConnectionResultPanel result={result} />
            </>,
        )

        expect(screen.getByText('Provider health')).toBeInTheDocument()
        expect(screen.getByText('Provider profile verified.')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: /test connection/i })).toBeEnabled()
        expect(screen.getByText('Provider authentication and profile verification succeeded.')).toBeInTheDocument()
        expect(screen.getByText('Excellent')).toBeInTheDocument()
    })
})

describe('provider session reconciliation panel', () => {
    it('requires exact operator evidence and submits the fixed evidence code', () => {
        const onSubmit = vi.fn()
        render(<ProviderSessionReconciliationPanel submitting={false} disabled={false} onSubmit={onSubmit} />)

        const submit = screen.getByRole('button', { name: /reconcile as not created/i })
        expect(submit).toBeDisabled()

        fireEvent.change(screen.getByLabelText('Payment ID'), {
            target: { value: 'e548556d-b49c-45f5-8f29-9c1bee2ec2f6' },
        })
        fireEvent.change(screen.getByLabelText('Exact provider reference'), {
            target: { value: 'e548556db49c45f58f299c1bee2ec2f6' },
        })
        fireEvent.change(screen.getByLabelText(/Dashboard evidence note/i), {
            target: { value: 'MID 645875 Test mode showed no matching Paymob order or transaction.' },
        })
        fireEvent.click(screen.getByRole('checkbox'))
        fireEvent.click(submit)

        expect(onSubmit).toHaveBeenCalledWith({
            paymentId: 'e548556d-b49c-45f5-8f29-9c1bee2ec2f6',
            providerReference: 'e548556db49c45f58f299c1bee2ec2f6',
            evidenceCode: 'PAYMOB_DASHBOARD_NO_ORDER',
            evidenceNote: 'MID 645875 Test mode showed no matching Paymob order or transaction.',
            administratorConfirmation: true,
        })
    })
})

describe('Development online payment simulation panel', () => {
    it('requires the explicit no-external-money confirmation and preserves the stable idempotency key', () => {
        const onSubmit = vi.fn()
        render(<DevelopmentOnlinePaymentSimulationPanel submitting={false} onSubmit={onSubmit} />)

        const submit = screen.getByRole('button', { name: /simulate accepted online payment/i })
        expect(submit).toBeDisabled()

        fireEvent.change(screen.getByPlaceholderText('CommerceHub payment ID'), {
            target: { value: ' 11111111-2222-3333-4444-555555555555 ' },
        })
        fireEvent.change(screen.getByPlaceholderText(/Stable idempotency key/i), {
            target: { value: ' gate-3r-success-0001 ' },
        })
        fireEvent.click(screen.getByRole('checkbox', {
            name: /Development simulation with no external Paymob charge or money movement/i,
        }))
        fireEvent.click(submit)

        expect(onSubmit).toHaveBeenCalledWith(
            '11111111-2222-3333-4444-555555555555',
            {
                idempotencyKey: 'gate-3r-success-0001',
                reason: 'Gate 3R Development online success-path certification',
                administratorConfirmation: true,
            },
        )
    })

    it('labels returned evidence as Development-only with no external money movement', () => {
        render(
            <DevelopmentOnlinePaymentSimulationPanel
                submitting={false}
                onSubmit={vi.fn()}
                result={{
                    paymentId: '11111111-2222-3333-4444-555555555555',
                    processed: true,
                    provider: 'PAYMOB',
                    providerReference: '11111111222233334444555555555555',
                    transactionId: 'DEV-SIM:ONLINE:11111111222233334444555555555555:evidence',
                    message: 'Payment completed through the canonical Development success path.',
                    evidenceType: 'DevelopmentOnlinePaymentSuccessSimulation',
                    isDevelopmentSimulation: true,
                    externalMoneyMovementConfirmed: false,
                }}
            />,
        )

        expect(screen.getByText('Development simulation · No external money movement')).toBeInTheDocument()
        expect(screen.getByText(/does not claim a Paymob charge/i)).toBeInTheDocument()
    })
})
