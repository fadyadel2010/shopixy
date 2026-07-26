import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ConnectionResultPanel } from './components/ConnectionResultPanel'
import { OperationalMessages } from './components/OperationalMessages'
import { ProviderHealthCard } from './components/ProviderHealthCard'
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
