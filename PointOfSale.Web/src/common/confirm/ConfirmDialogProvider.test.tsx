import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ConfirmDialogProvider, useConfirmDialog } from './ConfirmDialogProvider'

// The modal reads translations for the close/cancel/loading fallbacks and the
// RTL direction from i18next; a deterministic double keeps these tests focused
// on dialog behavior instead of translation content.
let mockLanguage = 'en'
const mockT = vi.fn((key: string) => key)

vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: mockT, i18n: { language: mockLanguage } }),
}))

function createDeferred<T>() {
    let resolve!: (value: T) => void
    let reject!: (reason?: unknown) => void
    const promise = new Promise<T>((res, rej) => {
        resolve = res
        reject = rej
    })
    return { promise, resolve, reject }
}

type HarnessOptions = Record<string, unknown>

function Harness({ options, outcome }: { options: HarnessOptions; outcome?: (value: unknown) => void }) {
    const { confirmAction } = useConfirmDialog()
    return (
        <div>
            <button
                type="button"
                onClick={() => {
                    void confirmAction(options).then((result) => outcome?.(result))
                }}
            >
                start-confirmation
            </button>
            <span>harness-body</span>
        </div>
    )
}

function RejectHarness({ options, onRejected }: { options: HarnessOptions; onRejected: (error: unknown) => void }) {
    const { confirmAction } = useConfirmDialog()
    return (
        <button
            type="button"
            onClick={() => {
                confirmAction(options).catch(onRejected)
            }}
        >
            start-confirmation
        </button>
    )
}

function ReplacementHarness({ actionOptions, actionOutcome, replacementOutcome }: {
    actionOptions: HarnessOptions
    actionOutcome: (value: unknown) => void
    replacementOutcome: (value: unknown) => void
}) {
    const { confirm, confirmAction } = useConfirmDialog()
    return (
        <div>
            <button type="button" onClick={() => void confirmAction(actionOptions).then(actionOutcome)}>
                start-action
            </button>
            <button
                type="button"
                onClick={() => void confirm({ title: 'Replacement dialog' }).then(replacementOutcome)}
            >
                open-replacement
            </button>
        </div>
    )
}

function renderHarness(options: HarnessOptions) {
    const outcome = vi.fn()
    render(
        <ConfirmDialogProvider>
            <Harness options={options} outcome={outcome} />
        </ConfirmDialogProvider>,
    )
    return outcome
}

beforeEach(() => {
    mockLanguage = 'en'
    mockT.mockClear()
})

describe('ConfirmDialogProvider — legacy confirmAction (regression contract)', () => {
    it('closes the dialog immediately on decision, then runs onConfirm and propagates its result', async () => {
        const deferred = createDeferred<string>()
        const onConfirm = vi.fn(() => deferred.promise)
        const outcome = renderHarness({
            title: 'Legacy flow',
            description: 'Runs after close',
            onConfirm,
        })

        fireEvent.click(screen.getByText('start-confirmation'))
        const dialog = await screen.findByRole('dialog')
        expect(dialog.getAttribute('aria-labelledby')).toBeTruthy()
        expect(screen.getByText('Legacy flow')).toBeTruthy()

        fireEvent.click(screen.getByText('Confirm'))

        // Legacy semantics: the modal is gone while the action still runs.
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
        expect(onConfirm).toHaveBeenCalledTimes(1)
        expect(outcome).not.toHaveBeenCalled()

        await act(async () => {
            deferred.resolve('action-result')
        })
        await waitFor(() => expect(outcome).toHaveBeenCalledWith('action-result'))
    })

    it('resolves false without running onConfirm when cancelled', async () => {
        const onConfirm = vi.fn()
        const outcome = renderHarness({ title: 'Cancel me', onConfirm })

        fireEvent.click(screen.getByText('start-confirmation'))
        await screen.findByRole('dialog')
        fireEvent.click(screen.getByRole('button', { name: 'common.cancel' }))

        await waitFor(() => expect(outcome).toHaveBeenCalledWith(false))
        expect(onConfirm).not.toHaveBeenCalled()
        expect(screen.queryByRole('dialog')).toBeNull()
    })
})

describe('ConfirmDialogProvider — awaited destructive actions', () => {
    it('keeps the dialog open in a real pending state while the confirmed action runs', async () => {
        const deferred = createDeferred<string>()
        const onConfirm = vi.fn(() => deferred.promise)
        const outcome = renderHarness({
            title: 'Delete 3 selected product(s)?',
            description: 'Destructive intent',
            confirmLabel: 'Delete',
            variant: 'danger',
            awaitAction: true,
            onConfirm,
        })

        fireEvent.click(screen.getByText('start-confirmation'))
        await screen.findByRole('dialog')

        const confirmButton = screen.getAllByText('Delete')[0].closest('button') as HTMLButtonElement
        expect(confirmButton.className).toContain('bg-red-600')
        fireEvent.click(confirmButton)

        expect(onConfirm).toHaveBeenCalledTimes(1)
        // Pending state: dialog stays mounted, action button disabled with the
        // loading label, cancel/close disabled.
        await waitFor(() => expect(confirmButton.disabled).toBe(true))
        expect(screen.getByRole('dialog')).toBeTruthy()
        expect(screen.getByText('common.loading')).toBeTruthy()
        expect(screen.getByRole('button', { name: 'common.cancel' })).toBeDisabled()
        expect((screen.queryByLabelText('common.close')?.closest('button') as HTMLButtonElement | null)?.disabled ?? true)
            .toBe(true)
        expect(outcome).not.toHaveBeenCalled()

        // Escape is suppressed while pending.
        fireEvent.keyDown(window, { key: 'Escape' })
        expect(screen.getByRole('dialog')).toBeTruthy()

        await act(async () => {
            deferred.resolve('done')
        })
        await waitFor(() => expect(outcome).toHaveBeenCalledWith('done'))
        expect(screen.queryByRole('dialog')).toBeNull()
    })

    it('ignores duplicate synchronous confirm clicks via a real guard: exactly one onConfirm execution', async () => {
        const deferred = createDeferred<void>()
        const onConfirm = vi.fn(() => deferred.promise)
        renderHarness({
            title: 'Destructive',
            confirmLabel: 'Delete',
            awaitAction: true,
            onConfirm,
        })

        fireEvent.click(screen.getByText('start-confirmation'))
        await screen.findByRole('dialog')

        const confirmButton = screen.getAllByText('Delete')[0].closest('button') as HTMLButtonElement
        // Two clicks land before React can re-render any disabled state.
        fireEvent.click(confirmButton)
        fireEvent.click(confirmButton)

        expect(onConfirm).toHaveBeenCalledTimes(1)

        await act(async () => {
            deferred.resolve(undefined)
        })
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    })

    it('rejects the confirmAction promise when the awaited action fails', async () => {
        const deferred = createDeferred<never>()
        const failure = new Error('boom')
        const onConfirm = vi.fn(() => deferred.promise)
        const rejected = vi.fn()
        render(
            <ConfirmDialogProvider>
                <RejectHarness
                    options={{ title: 'Fails', awaitAction: true, onConfirm }}
                    onRejected={rejected}
                />
            </ConfirmDialogProvider>,
        )

        fireEvent.click(screen.getByText('start-confirmation'))
        await screen.findByRole('dialog')
        fireEvent.click(screen.getAllByText('Confirm')[0].closest('button')!)
        expect(onConfirm).toHaveBeenCalledTimes(1)

        await act(async () => {
            deferred.reject(failure)
            await Promise.resolve()
            await Promise.resolve()
        })

        expect(rejected).toHaveBeenCalledWith(failure)
        expect(screen.queryByRole('dialog')).toBeNull()
    })

    it('does not let a late awaited action settle a newer replacement dialog', async () => {
        const deferred = createDeferred<boolean>()
        const actionOutcome = vi.fn()
        const replacementOutcome = vi.fn()
        render(
            <ConfirmDialogProvider>
                <ReplacementHarness
                    actionOptions={{
                        title: 'Original action',
                        confirmLabel: 'Delete',
                        awaitAction: true,
                        onConfirm: () => deferred.promise,
                    }}
                    actionOutcome={actionOutcome}
                    replacementOutcome={replacementOutcome}
                />
            </ConfirmDialogProvider>,
        )

        fireEvent.click(screen.getByText('start-action'))
        await screen.findByText('Original action')
        fireEvent.click(screen.getByText('Delete'))
        await waitFor(() => expect(screen.getByText('common.loading')).toBeTruthy())

        fireEvent.click(screen.getByText('open-replacement'))
        await screen.findByText('Replacement dialog')
        await waitFor(() => expect(actionOutcome).toHaveBeenCalledWith(false))

        await act(async () => {
            deferred.resolve(true)
        })
        expect(screen.getByText('Replacement dialog')).toBeTruthy()
        expect(replacementOutcome).not.toHaveBeenCalled()

        fireEvent.click(screen.getByRole('button', { name: 'common.cancel' }))
        await waitFor(() => expect(replacementOutcome).toHaveBeenCalledWith(false))
    })

    it('keeps accessible title/description linkage and restores focus to the opener after settle', async () => {
        const deferred = createDeferred<boolean>()
        renderHarness({
            title: 'Accessible delete',
            description: 'Describes the danger',
            confirmLabel: 'Delete',
            cancelLabel: 'Keep',
            variant: 'danger',
            awaitAction: true,
            onConfirm: () => deferred.promise,
        })

        const opener = screen.getByText('start-confirmation').closest('button')!
        opener.focus()
        fireEvent.click(opener)
        const dialog = await screen.findByRole('dialog')
        const labelledBy = dialog.getAttribute('aria-labelledby')
        expect(labelledBy).toBeTruthy()
        expect(document.getElementById(labelledBy!)?.textContent).toBe('Accessible delete')
        expect(dialog.getAttribute('aria-describedby')).toBeTruthy()
        expect(document.getElementById(dialog.getAttribute('aria-describedby')!)?.textContent)
            .toBe('Describes the danger')

        fireEvent.click(screen.getAllByText('Delete')[0].closest('button')!)
        await act(async () => {
            deferred.resolve(true)
        })
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
        expect(document.activeElement).toBe(opener)
    })

    it('renders right-to-left direction for Arabic', async () => {
        mockLanguage = 'ar'
        const deferred = createDeferred<boolean>()
        renderHarness({
            title: 'عربي',
            confirmLabel: 'حذف',
            awaitAction: true,
            onConfirm: () => deferred.promise,
        })

        fireEvent.click(screen.getByText('start-confirmation'))
        const wrapper = (await screen.findByRole('dialog')).parentElement!
        expect(wrapper.getAttribute('dir')).toBe('rtl')
        expect((await screen.findByRole('dialog')).querySelector('.flex-row-reverse')).toBeNull()
        expect((await screen.findByRole('dialog')).className).toContain('overflow-y-auto')

        fireEvent.click(screen.getAllByText('حذف')[0].closest('button')!)
        await act(async () => {
            deferred.resolve(true)
        })
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    })

    it('renders right-to-left direction for regional Arabic variants (ar-JO)', async () => {
        mockLanguage = 'ar-JO'
        renderHarness({ title: 'عربي أردني' })

        fireEvent.click(screen.getByText('start-confirmation'))
        const wrapper = await screen.findByRole('dialog')
        expect(wrapper.parentElement!.getAttribute('dir')).toBe('rtl')

        fireEvent.click(screen.getByRole('button', { name: 'common.cancel' }))
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    })

    it('keeps focus inside the dialog when an awaited action starts loading — no jump to the opener', async () => {
        const deferred = createDeferred<void>()
        renderHarness({
            title: 'Destructive pending focus',
            confirmLabel: 'Delete',
            awaitAction: true,
            onConfirm: () => deferred.promise,
        })

        const openerButton = screen.getByText('start-confirmation').closest('button')!
        openerButton.focus()
        fireEvent.click(openerButton)
        const dialog = await screen.findByRole('dialog')

        fireEvent.click(screen.getAllByText('Delete')[0].closest('button')!)
        await waitFor(() => expect(screen.getByText('common.loading')).toBeTruthy())

        // Focus must remain deterministic inside the dialog while every
        // control is disabled, never falling back to the page opener.
        expect(document.activeElement).not.toBe(openerButton)
        expect(dialog.contains(document.activeElement as Node)).toBe(true)

        await act(async () => {
            deferred.resolve(undefined)
        })
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    })

    it('pins Tab focus to the dialog while all controls are disabled during a pending awaited action', async () => {
        const deferred = createDeferred<void>()
        renderHarness({
            title: 'Pinned tab',
            confirmLabel: 'Delete',
            awaitAction: true,
            onConfirm: () => deferred.promise,
        })

        fireEvent.click(screen.getByText('start-confirmation'))
        const dialog = await screen.findByRole('dialog')
        fireEvent.click(screen.getAllByText('Delete')[0].closest('button')!)
        await waitFor(() => expect(screen.getByText('common.loading')).toBeTruthy())

        // Simulate real-browser behavior: a focused control that becomes
        // disabled drops focus to <body>; Tab must not escape the overlay.
        // jsdom keeps activeElement pinned to the disabled button, so move
        // focus outside explicitly before pressing Tab.
        screen.getByText('start-confirmation').closest('button')!.focus()
        expect(dialog.contains(document.activeElement as Node)).toBe(false)

        fireEvent.keyDown(window, { key: 'Tab' })
        expect(document.activeElement).toBe(dialog)

        await act(async () => {
            deferred.resolve(undefined)
        })
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    })
})
