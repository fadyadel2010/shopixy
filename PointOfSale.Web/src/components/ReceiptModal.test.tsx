import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor, act } from '@testing-library/react'
import ReceiptModal from './ReceiptModal'

// Mock dependencies
vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string, fallback?: string) => fallback || key
    })
}))

vi.mock('../contexts/SettingsContext', () => ({
    useSettings: () => ({
        settings: {
            companyName: 'Test Company',
            printFormat: 'thermal'
        },
        formatCurrency: (amount: number) => `$${amount.toFixed(2)}`
    })
}))

const mockReceiptData = {
    receipt: {
        invoiceNumber: 'INV-001',
        invoiceDate: '2026-04-24T12:00:00Z',
        warehouseName: 'Main Warehouse',
        customerName: 'Test Customer',
        items: [
            {
                productName: 'Test Product',
                variantDescription: 'Color: Red, Size: M',
                variantSku: 'TEST-RED-M',
                unitName: 'Box',
                conversionFactor: 5,
                quantity: 2,
                unitPrice: 10.00,
                total: 20.00
            }
        ],
        subtotal: 20.00,
        discount: 0,
        tax: 2.00,
        total: 22.00,
        paymentMethod: 'Cash',
        paid: 25.00,
        change: 3.00
    }
}

describe('ReceiptModal print resilience', () => {
    let originalPrint: typeof window.print
    let printMock: ReturnType<typeof vi.fn<() => void | Promise<void>>>

    beforeEach(() => {
        originalPrint = window.print
        printMock = vi.fn()
        window.print = printMock
        vi.useFakeTimers()
    })

    afterEach(() => {
        window.print = originalPrint
        vi.restoreAllMocks()
        vi.useRealTimers()
    })

    it('shows the exact variant SKU and sale unit on the receipt line', () => {
        render(<ReceiptModal receiptData={mockReceiptData} onClose={vi.fn()} />)

        expect(screen.getByText(/Variant: Color: Red, Size: M/i)).toBeInTheDocument()
        expect(screen.getByText(/SKU: TEST-RED-M/i)).toBeInTheDocument()
        expect(screen.getByText(/Unit: Box \(x5\)/i)).toBeInTheDocument()
    })

    it('successfully prints and shows no error alert when window.print works', async () => {
        const onClose = vi.fn()

        render(<ReceiptModal receiptData={mockReceiptData} onClose={onClose} />)

        // Auto-print should trigger after 500ms
        await act(async () => {
            vi.advanceTimersByTime(500)
        })
        
        expect(printMock).toHaveBeenCalledTimes(1)

        // No error alert should be visible
        expect(screen.queryByRole('alert')).not.toBeInTheDocument()

        // Print button should be available for manual retry
        const printButton = screen.getByRole('button', { name: /print receipt/i })
        expect(printButton).toBeInTheDocument()

        // Manual print should work
        await act(async () => {
            printButton.click()
        })
        
        expect(printMock).toHaveBeenCalledTimes(2)

        // Still no error alert
        expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('shows error alert and retry button when window.print is not available', async () => {
        const onClose = vi.fn()

        // Simulate window.print not being a function
        Object.defineProperty(window, 'print', { configurable: true, writable: true, value: undefined })

        render(<ReceiptModal receiptData={mockReceiptData} onClose={onClose} />)

        // Auto-print should trigger and fail
        await act(async () => {
            vi.advanceTimersByTime(500)
        })

        // Error alert should be visible
        const alert = screen.getByRole('alert')
        expect(alert).toBeInTheDocument()
        expect(alert).toHaveTextContent(/could not open the print dialog/i)

        // Print button should still be available for retry
        const printButton = screen.getByRole('button', { name: /print receipt/i })
        expect(printButton).toBeInTheDocument()

        // Restore window.print for retry
        window.print = printMock

        // Click retry button
        await act(async () => {
            printButton.click()
        })

        // Now print should succeed
        expect(printMock).toHaveBeenCalledTimes(1)
    })

    it('shows error alert and retry button when window.print throws an error', async () => {
        const onClose = vi.fn()

        // Simulate window.print throwing an error
        printMock.mockImplementationOnce(() => {
            throw new Error('Print failed')
        })

        render(<ReceiptModal receiptData={mockReceiptData} onClose={onClose} />)

        // Auto-print should trigger and fail
        await act(async () => {
            vi.advanceTimersByTime(500)
        })

        // Error alert should be visible
        const alert = screen.getByRole('alert')
        expect(alert).toBeInTheDocument()
        expect(alert).toHaveTextContent(/could not open the print dialog/i)

        // Print button should be available for retry
        const printButton = screen.getByRole('button', { name: /print receipt/i })
        expect(printButton).toBeInTheDocument()

        // Fix the mock for retry
        printMock.mockImplementation(() => {})

        // Click retry button - should call handlePrint again
        await act(async () => {
            printButton.click()
        })

        // Print should be called again (once for failed auto-print, once for retry)
        expect(printMock).toHaveBeenCalledTimes(2)

        // After successful retry, error alert should disappear
        expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('updates button text to "Printing..." during print operation', async () => {
        // Use real timers for this test to avoid timing issues with async state updates
        vi.useRealTimers()
        
        const onClose = vi.fn()

        // Create promises we can control to simulate async print operations
        let resolveAutoPrint: () => void
        const autoPrintPromise = new Promise<void>((resolve) => {
            resolveAutoPrint = resolve
        })

        let resolveManualPrint: () => void
        const manualPrintPromise = new Promise<void>((resolve) => {
            resolveManualPrint = resolve
        })

        // Mock window.print to return auto-print promise first, then manual print promise
        printMock
            .mockImplementationOnce(() => autoPrintPromise)
            .mockImplementationOnce(() => manualPrintPromise)

        render(<ReceiptModal receiptData={mockReceiptData} onClose={onClose} />)

        // Wait for auto-print to be called and resolve it
        await waitFor(() => expect(printMock).toHaveBeenCalledTimes(1), { timeout: 2000 })
        await act(async () => {
            resolveAutoPrint!()
            await autoPrintPromise
        })

        const printButton = screen.getByRole('button', { name: /print receipt/i })
        
        // Click print button for manual print
        await act(async () => {
            printButton.click()
        })

        // While print is pending, button should show "Printing..."
        await waitFor(() => {
            expect(screen.getByRole('button', { name: /printing\.\.\./i })).toBeInTheDocument()
        })

        // Resolve the manual print operation
        await act(async () => {
            resolveManualPrint!()
            await manualPrintPromise
        })

        // Verify print was called twice (auto + manual)
        expect(printMock).toHaveBeenCalledTimes(2)

        // After print completes, button should revert to "Print receipt"
        await waitFor(() => {
            expect(screen.getByRole('button', { name: /print receipt/i })).toBeInTheDocument()
        })
        
        // Restore fake timers for other tests
        vi.useFakeTimers()
    })
})
