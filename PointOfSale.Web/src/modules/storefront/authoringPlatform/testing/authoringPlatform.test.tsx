import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import {
    AUTHORING_ACTION_ORDER,
    AuthoringActionBar,
    AuthoringConflictDialog,
    AuthoringPlatformProvider,
    AuthoringStatusBadge,
    UnsavedChangesDialog,
    useAuthoringGuard,
} from '../index'

function GuardHarness({ onSave = () => {} }) {
    const { requestNavigation, unsavedDialogProps } = useAuthoringGuard({ isDirty: true, onSave })
    return (
        <>
            <button type="button" onClick={() => requestNavigation('/storefront/appearance/themes')}>Leave editor</button>
            <UnsavedChangesDialog {...unsavedDialogProps} />
        </>
    )
}

describe('Storefront Authoring Platform', () => {
    afterEach(() => window.history.replaceState({}, '', '/'))

    it('publishes the constitutional action contract and renders that order', () => {
        expect(AUTHORING_ACTION_ORDER).toEqual(['back', 'title', 'status', 'preview', 'history', 'discard', 'save', 'publish'])
        const { container } = render(
            <AuthoringActionBar
                title="Homepage"
                status={<AuthoringStatusBadge state="dirty" />}
                backAction={{ label: 'Back', onClick: vi.fn() }}
                previewAction={{ label: 'Preview Saved Draft', onClick: vi.fn() }}
                historyAction={{ label: 'StoreConfig History', onClick: vi.fn() }}
                discardAction={{ label: 'Discard', onClick: vi.fn() }}
                saveAction={{ label: 'Save Draft', onClick: vi.fn() }}
                publishAction={{ label: 'Publish Homepage', onClick: vi.fn() }}
            />,
        )

        expect([...container.querySelectorAll('button')].map((button) => button.textContent?.trim())).toEqual([
            'Back',
            'Preview Saved Draft',
            'StoreConfig History',
            'Discard',
            'Save Draft',
            'Publish Homepage',
        ])
    })

    it('owns unload, guarded navigation, keyboard Save, dialog focus, and focus restoration', async () => {
        const onSave = vi.fn()
        render(
            <AuthoringPlatformProvider>
                <button type="button">Before editor</button>
                <GuardHarness onSave={onSave} />
            </AuthoringPlatformProvider>,
        )

        const trigger = screen.getByRole('button', { name: 'Leave editor' })
        trigger.focus()
        expect(window.dispatchEvent(new Event('beforeunload', { cancelable: true }))).toBe(false)
        fireEvent.keyDown(document, { key: 's', ctrlKey: true })
        expect(onSave).toHaveBeenCalledTimes(1)

        fireEvent.click(trigger)
        const dialog = screen.getByRole('dialog', { name: 'Unsaved changes' })
        await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Keep editing' })).toHaveFocus())
        fireEvent.keyDown(document, { key: 'Escape' })
        expect(screen.queryByRole('dialog', { name: 'Unsaved changes' })).not.toBeInTheDocument()
        expect(trigger).toHaveFocus()
    })

    it('owns the normalized conflict recovery contract', () => {
        render(
            <AuthoringConflictDialog
                open
                message="The accepted version changed."
                onClose={vi.fn()}
                onLoadLatest={vi.fn()}
                onRetry={vi.fn()}
            />,
        )

        const dialog = screen.getByRole('dialog', { name: 'A newer version is available' })
        expect(within(dialog).getByText('The accepted version changed.')).toBeInTheDocument()
        expect(within(dialog).getByRole('button', { name: 'Load Latest' })).toBeEnabled()
        expect(within(dialog).getByRole('button', { name: 'Retry My Draft' })).toBeEnabled()
    })
})
