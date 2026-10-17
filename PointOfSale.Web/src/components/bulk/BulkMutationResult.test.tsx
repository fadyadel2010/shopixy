import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it, vi } from 'vitest'
import BulkMutationResult from './BulkMutationResult.jsx'
import {
    buildBulkMutationPresentation,
    resolveBulkActionPresentation,
    resolveBulkItemReasonKey,
} from './bulkMutationPresentation'

// The overlay mirrors the en.json entries for the Phase 6G.6 bulk result
// strings so tests can assert explicit localized text, not raw keys.
const mockT = vi.hoisted(() => {
    const overlay: Record<string, string> = {
        'products.bulkProcessingActivate': 'Activating {{count}} selected product(s)…',
        'products.bulkProcessingDeactivate': 'Deactivating {{count}} selected product(s)…',
        'products.bulkProcessingPublish': 'Publishing {{count}} selected product(s)…',
        'products.bulkProcessingUnpublish': 'Unpublishing {{count}} selected product(s)…',
        'products.bulkProcessingDelete': 'Deleting {{count}} selected product(s)…',
        'products.bulkSuccessTitleActivate': 'Products activated',
        'products.bulkSuccessTitleDeactivate': 'Products deactivated',
        'products.bulkSuccessTitlePublish': 'Products published',
        'products.bulkSuccessTitleUnpublish': 'Products unpublished',
        'products.bulkSuccessTitleDelete': 'Selected products deleted',
        'products.bulkPartialTitle': '{{action}} completed, but some products were not changed',
        'products.bulkActionNameActivate': 'Activate',
        'products.bulkActionNameDeactivate': 'Deactivate',
        'products.bulkActionNamePublish': 'Publish',
        'products.bulkActionNameUnpublish': 'Unpublish',
        'products.bulkActionNameDelete': 'Delete',
        'products.bulkFailedTitle': 'No products were changed',
        'products.bulkNoChangesTitle': 'No products were changed',
        'products.bulkCountRequested': 'Selected: {{count}}',
        'products.bulkCountAccepted': 'Accepted: {{count}}',
        'products.bulkCountProcessedActivate': 'Activated: {{count}}',
        'products.bulkCountProcessedDeactivate': 'Deactivated: {{count}}',
        'products.bulkCountProcessedPublish': 'Published: {{count}}',
        'products.bulkCountProcessedUnpublish': 'Unpublished: {{count}}',
        'products.bulkCountProcessedDelete': 'Deleted: {{count}}',
        'products.bulkCountUnchangedActivate': 'Already active: {{count}}',
        'products.bulkCountUnchangedDeactivate': 'Already inactive: {{count}}',
        'products.bulkCountUnchangedPublish': 'Already published: {{count}}',
        'products.bulkCountUnchangedUnpublish': 'Already unpublished: {{count}}',
        'products.bulkCountUnchangedDelete': 'Unchanged: {{count}}',
        'products.bulkCountFailed': 'Failed: {{count}}',
        'products.bulkItemAlreadyActive': '{{name}} is already active.',
        'products.bulkItemAlreadyInactive': '{{name}} is already inactive.',
        'products.bulkItemAlreadyPublished': '{{name}} is already published.',
        'products.bulkItemAlreadyUnpublished': '{{name}} is already unpublished.',
        'products.bulkItemConcurrencyConflict': '{{name}} changed before the operation completed.',
        'products.bulkItemNotFound': '{{name}} is no longer available.',
        'products.bulkItemValidationFailed': '{{name}} could not be processed.',
        'products.bulkItemUnknownReason': '{{name}} could not be changed.',
        'products.bulkItemUnknownUnchangedReason': '{{name}} was not changed.',
        'products.bulkUnknownProduct': 'Product',
        'products.bulkItemDependencyBlocked': '{{name}} cannot be deleted because it has blocking dependencies.',
        'products.bulkItemDeleteConcurrencyConflict': '{{name}} changed before deletion. Refresh the product list and try again.',
        'products.bulkItemRequestCancelled': '{{name}} was not processed because the bulk operation was cancelled.',
        'products.bulkItemIdempotencyConflict': '{{name}} could not be deleted because this delete request conflicts with an earlier request.',
        'products.bulkItemIdempotencyInProgress': '{{name}} is already being deleted by a request still in progress.',
        'products.bulkItemInactiveCannotPublish': 'Product is inactive and cannot be published.',
        'products.bulkItemPublicationNotReady': 'Product is not ready for publication.',
        'products.bulkItemProductName': '{{name}}:',
        'products.bulkItemStatusFailed': 'Failed',
        'products.bulkItemStatusUnchanged': 'Unchanged',
        'products.bulkRequestValidationError': 'This bulk request was not valid. Please review the selection and try again.',
        'products.bulkRequestPermissionError': 'You do not have permission to perform this bulk action.',
        'products.bulkRequestConflictError': 'This bulk request conflicts with another change. Please review the products and try again.',
        'products.bulkRequestServiceError': 'The bulk operation could not be completed. Please try again later.',
        'products.bulkStartFailed': 'The bulk operation could not be started.',
        'products.bulkRefreshErrorNotice': 'The operation completed, but the products list could not be refreshed.',
        'products.bulkRefreshErrorNoticePublish': 'Publishing completed, but the product list could not be refreshed.',
        'products.bulkRefreshErrorNoticeUnpublish': 'Unpublishing completed, but the product list could not be refreshed.',
        'products.bulkRefreshErrorNoticeDelete': 'Deletion completed, but the product list could not be refreshed.',
        'products.bulkRequestRefreshErrorNotice': 'The request outcome is uncertain, and the product list could not be refreshed.',
        'products.bulkRequestRefreshWarning': 'The product list could not be refreshed.',
        'products.bulkRequestFailedTitle': 'The bulk request could not be completed',
        'products.bulkRequestAttentionTitle': 'The bulk request needs attention',
        'products.bulkRequestUncertainTitle': 'The bulk request could not be confirmed',
        'products.bulkRequestUncertainMessage': 'The request could not be confirmed and may still be processing. Refresh the product list before retrying; the operation will not repeat automatically.',
        'products.bulkDeleteUncertainTitle': 'Deletion could not be confirmed',
        'products.bulkDeleteUncertainMessage': 'The delete request could not be confirmed and may still be processing. Refresh the product list before retrying; the deletion will not repeat automatically.',
        'products.bulkRequestIdempotencyConflictMessage': 'This bulk request conflicts with an earlier request. Refresh the product list before trying again.',
        'products.bulkRequestIdempotencyInProgressMessage': 'A similar bulk request is already in progress. Refresh the product list before trying again.',
        'products.bulkDeleteIdempotencyConflictMessage': 'This deletion request conflicts with an earlier request. Refresh the product list before trying again.',
        'products.bulkDeleteIdempotencyInProgressMessage': 'A deletion request is already in progress. Refresh the product list before trying again.',
        'products.bulkResultDismiss': 'Dismiss',
        'products.bulkResultRegionAria': 'Bulk operation result',
    }
    return (key: string, options?: Record<string, unknown>) => {
        const template = overlay[key] ?? key
        if (!options || typeof options !== 'object') return template
        return Object.entries(options).reduce(
            (acc, [name, value]) => acc.split(`{{${name}}}`).join(String(value)),
            template,
        )
    }
})

vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: mockT }),
}))

const ALL_ACTIONS = ['activate', 'deactivate', 'publish', 'unpublish', 'delete'] as const

const aggregate = (overrides: Record<string, unknown> = {}): Record<string, unknown> => ({
    requested: 1,
    accepted: 1,
    processed: 1,
    unchanged: 0,
    failed: 0,
    correlationId: 'corr-component-test',
    results: [
        { productId: 'p1', status: 'processed', action: null, code: null, message: null, previousVersion: '7', newVersion: '8' },
    ],
    ...overrides,
})

const item = (overrides: Record<string, unknown>) => ({
    productId: 'p1',
    status: 'processed',
    action: null,
    code: null,
    message: null,
    previousVersion: '7',
    newVersion: '8',
    ...overrides,
})

const namesSnapshot = (map: Record<string, string>) => (productId: unknown) => {
    const name = productId ? map[String(productId)] : null
    return typeof name === 'string' && name.trim().length > 0 ? name : 'Product'
}

function resultPanel() {
    return screen.getByTestId('bulk-mutation-result')
}

function resultLiveRegion() {
    return within(resultPanel()).getByRole('status')
}

type ResultProps = Record<string, unknown>

function renderResult(props: ResultProps = {}) {
    return render(
        <BulkMutationResult
            action="activate"
            resolveProductName={namesSnapshot({ p1: 'Alpha Tea', p2: 'Beta Coffee', p3: 'Gamma Blend' })}
            {...props}
        />,
    )
}

describe('BulkMutationResult — aggregate truthfulness', () => {
    it('renders an action-aware success with authoritative requested/accepted counts', () => {
        renderResult({
            result: aggregate({
                requested: 5,
                accepted: 4,
                processed: 3,
                unchanged: 1,
                results: [
                    item({ productId: 'p1', status: 'processed' }),
                    item({ productId: 'p2', status: 'processed' }),
                    item({ productId: 'p3', status: 'processed' }),
                    item({ productId: 'p1', status: 'unchanged', code: 'ALREADY_ACTIVE' }),
                ],
            }),
        })

        expect(screen.getByText('Products activated')).toBeTruthy()
        expect(screen.getByText('Selected: 5')).toBeTruthy()
        expect(screen.getByText('Accepted: 4')).toBeTruthy()
        expect(screen.getByText('Activated: 3')).toBeTruthy()
        expect(screen.getByText('Already active: 1')).toBeTruthy()
        expect(screen.getByText('Failed: 0')).toBeTruthy()

        const panel = screen.getByText('Products activated').closest('div.p-3')
        expect(panel?.className).toContain('bg-green-50')
    })

    it('renders mixed outcomes as partial success with failures visible', () => {
        renderResult({
            result: aggregate({
                requested: 2,
                accepted: 2,
                processed: 1,
                failed: 1,
                results: [
                    item({ productId: 'p1', status: 'processed' }),
                    item({ productId: 'p2', status: 'failed', code: 'CONCURRENCY_CONFLICT' }),
                ],
            }),
        })

        expect(screen.getByText('Activate completed, but some products were not changed')).toBeTruthy()
        expect(screen.getByText('Beta Coffee changed before the operation completed.')).toBeTruthy()
        const panel = screen.getByText('Activate completed, but some products were not changed').closest('div.p-3')
        expect(panel?.className).toContain('bg-amber-50')
    })

    it('headlines all-unchanged with the shared neutral summary, never an action-specific success title', () => {
        renderResult({
            action: 'publish',
            result: aggregate({
                requested: 2,
                accepted: 2,
                processed: 0,
                unchanged: 2,
                failed: 0,
                results: [
                    item({ productId: 'p1', status: 'unchanged', action: 'publish', code: 'ALREADY_PUBLISHED', newVersion: null }),
                    item({ productId: 'p2', status: 'unchanged', action: 'publish', code: 'ALREADY_PUBLISHED', newVersion: null }),
                ],
            }),
        })

        // Shared localized summary headline with informational tone…
        expect(screen.getByText('No products were changed')).toBeTruthy()
        const panel = screen.getByText('No products were changed').closest('div.p-3')
        expect(panel?.className).not.toContain('bg-red-100')
        expect(panel?.className).toContain('bg-blue-50')
        // …never the action-specific success-like headline as primary summary.
        expect(screen.queryByText('Selected products are already published')).toBeNull()
        expect(screen.queryByText('Products published')).toBeNull()
        // Action-aware unchanged counts/reasons still explain the outcome.
        expect(screen.getByText('Published: 0')).toBeTruthy()
        expect(screen.getByText('Already published: 2')).toBeTruthy()
        expect(screen.getByText('Alpha Tea is already published.')).toBeTruthy()
    })

    it('renders an all-failed aggregate as an error-toned no-changes outcome', () => {
        renderResult({
            result: aggregate({
                requested: 2,
                accepted: 2,
                processed: 0,
                unchanged: 0,
                failed: 2,
                results: [
                    item({ productId: 'p1', status: 'failed', code: 'VALIDATION_FAILED' }),
                    item({ productId: 'p2', status: 'failed', code: 'NOT_FOUND' }),
                ],
            }),
        })

        expect(screen.getByText('No products were changed')).toBeTruthy()
        const panel = screen.getByText('No products were changed').closest('div.p-3')
        expect(panel?.className).toContain('bg-red-100')
        expect(panel?.className).not.toContain('bg-green-50')
    })
})

describe('BulkMutationResult — per-item mapping, statuses and names', () => {
    it('falls back safely for unknown item codes and never exposes raw diagnostics', () => {
        renderResult({
            result: aggregate({
                processed: 0,
                failed: 1,
                results: [
                    item({
                        status: 'failed',
                        code: 'FUTURE_UNKNOWN_CODE',
                        message: 'NpgsqlException: select * from System.Products -- secret',
                    }),
                ],
            }),
        })

        expect(screen.getByText('Alpha Tea could not be changed.')).toBeTruthy()
        const bodyText = document.body.textContent ?? ''
        expect(bodyText).not.toContain('FUTURE_UNKNOWN_CODE')
        expect(bodyText).not.toContain('Npgsql')
        expect(bodyText).not.toContain('select * from')
        expect(bodyText).not.toContain('secret')
    })

    it('renders an unknown unchanged code with a neutral safe fallback instead of omitting the item', () => {
        renderResult({
            action: 'deactivate',
            result: aggregate({
                processed: 0,
                unchanged: 1,
                failed: 0,
                results: [item({ status: 'unchanged', code: 'FUTURE_UNCHANGED_CODE' })],
            }),
        })

        expect(screen.getByText('Alpha Tea was not changed.')).toBeTruthy()
        expect(screen.getByText('Unchanged')).toBeTruthy()
        expect(document.body.textContent ?? '').not.toContain('FUTURE_UNCHANGED_CODE')
    })

    it('states Publish + PRODUCT_INACTIVE exactly and uses the safe fallback for another action', () => {
        const { unmount } = renderResult({
            action: 'publish',
            result: aggregate({
                processed: 0,
                failed: 1,
                results: [
                    item({ status: 'failed', action: 'publish', code: 'PRODUCT_INACTIVE', message: 'raw diagnostic' }),
                ],
            }),
        })

        expect(screen.getByText('Product is inactive and cannot be published.')).toBeTruthy()
        // The product name is rendered as its own prefix, never inside the reason.
        expect(screen.getByText('Alpha Tea:')).toBeTruthy()
        expect(document.body.textContent ?? '').not.toContain('raw diagnostic')
        unmount()

        renderResult({
            action: 'deactivate',
            result: aggregate({
                processed: 0,
                failed: 1,
                results: [
                    item({ status: 'failed', action: 'deactivate', code: 'PRODUCT_INACTIVE', message: 'raw diagnostic' }),
                ],
            }),
        })

        // No misleading publish wording outside Publish.
        expect(screen.queryByText('Product is inactive and cannot be published.')).toBeNull()
        expect(screen.getByText('Alpha Tea could not be changed.')).toBeTruthy()
    })

    it('gives failed and unchanged rows explicit visible Status and safe Reason labels', () => {
        renderResult({
            action: 'delete',
            result: aggregate({
                requested: 2,
                accepted: 2,
                processed: 0,
                unchanged: 1,
                failed: 1,
                results: [
                    item({ productId: 'p2', status: 'unchanged', action: 'delete', code: 'REQUEST_CANCELLED', newVersion: null }),
                    item({ productId: 'p1', status: 'failed', action: 'delete', code: 'DEPENDENCY_BLOCKED', message: 'FK leak' }),
                ],
            }),
        })

        const region = resultPanel()
        const rows = within(region).getAllByRole('listitem')
        expect(rows).toHaveLength(2)

        const unchangedRow = rows.find((row) => row.textContent?.includes('was not processed'))!
        const failedRow = rows.find((row) => row.textContent?.includes('cannot be deleted'))!
        expect(unchangedRow.textContent).toContain('Unchanged')
        expect(failedRow.textContent).toContain('Failed')

        const bodyText = document.body.textContent ?? ''
        expect(bodyText).not.toContain('FK leak')
    })

    it('keeps resolving names through the provided stable snapshot, independent of result payloads', () => {
        const snapshot: Record<string, string> = { p1: 'Alpha Tea' }
        const { rerender } = render(
            <BulkMutationResult
                action="delete"
                result={aggregate({
                    requested: 2,
                    accepted: 2,
                    processed: 0,
                    unchanged: 0,
                    failed: 2,
                    results: [
                        item({ productId: 'p1', status: 'failed', action: 'delete', code: 'DEPENDENCY_BLOCKED' }),
                        item({ productId: 'pX-gone', status: 'failed', action: 'delete', code: 'NOT_FOUND' }),
                    ],
                })}
                resolveProductName={(productId: unknown) => snapshot[String(productId)] ?? 'Product'}
            />,
        )

        expect(screen.getByText('Alpha Tea cannot be deleted because it has blocking dependencies.')).toBeTruthy()
        // Unknown/missing snapshot falls back to the generic product word,
        // never "undefined" or raw ids leaking as names.
        expect(screen.getByText('Product is no longer available.')).toBeTruthy()
        expect(document.body.textContent ?? '').not.toContain('undefined')
        expect(document.body.textContent ?? '').not.toContain('pX-gone')

        // A later rerender (e.g. rows refetched) cannot rewrite history.
        rerender(
            <BulkMutationResult
                action="delete"
                result={aggregate({
                    requested: 2,
                    accepted: 2,
                    processed: 0,
                    unchanged: 0,
                    failed: 2,
                    results: [
                        item({ productId: 'p1', status: 'failed', action: 'delete', code: 'DEPENDENCY_BLOCKED' }),
                        item({ productId: 'pX-gone', status: 'failed', action: 'delete', code: 'NOT_FOUND' }),
                    ],
                })}
                resolveProductName={(productId: unknown) => snapshot[String(productId)] ?? 'Product'}
            />,
        )
        expect(screen.getByText('Alpha Tea cannot be deleted because it has blocking dependencies.')).toBeTruthy()
    })
})

describe('BulkMutationResult — request errors, uncertainty and safety', () => {
    it.each(ALL_ACTIONS)('treats local BULK_* hook codes as a definite start failure for %s', (action) => {
        renderResult({
            action,
            error: Object.assign(new Error('local validation secret'), {
                status: null,
                code: 'BULK_MISSING_ROW_VERSION',
            }),
        })

        expect(screen.getByText('The bulk operation could not be started.')).toBeTruthy()
        expect(screen.queryByText(/could not be confirmed/i)).toBeNull()
        expect(document.body.textContent ?? '').not.toContain('local validation secret')
        const panel = resultPanel().querySelector('div.p-3')
        expect(panel?.className).toContain('bg-red-100')
    })

    it.each([
        ['activate', 'INVALID_BULK_ITEMS'],
        ['deactivate', 'INVALID_IDEMPOTENCY_KEY'],
        ['publish', 'IDEMPOTENCY_KEY_GENERATION_UNAVAILABLE'],
        ['unpublish', 'INVALID_BULK_ITEMS'],
        ['delete', 'IDEMPOTENCY_KEY_GENERATION_UNAVAILABLE'],
    ])('treats the API-layer pre-request code %s/%s as definite for every action, never uncertain', (action, code) => {
        renderResult({
            action,
            error: Object.assign(new Error(`pre-dispatch refusal ${code}`), { status: null, code }),
        })

        expect(screen.getByText('The bulk operation could not be started.')).toBeTruthy()
        expect(screen.queryByText(/could not be confirmed/i)).toBeNull()
        expect(screen.queryByText(/may still be processing/i)).toBeNull()
    })

    it.each([
        ['activate', null, 'The bulk request could not be confirmed', 'The request could not be confirmed and may still be processing. Refresh the product list before retrying; the operation will not repeat automatically.'],
        ['deactivate', null, 'The bulk request could not be confirmed', 'The request could not be confirmed and may still be processing. Refresh the product list before retrying; the operation will not repeat automatically.'],
        ['publish', 408, 'The bulk request could not be confirmed', 'The request could not be confirmed and may still be processing. Refresh the product list before retrying; the operation will not repeat automatically.'],
        ['unpublish', 503, 'The bulk request could not be confirmed', 'The request could not be confirmed and may still be processing. Refresh the product list before retrying; the operation will not repeat automatically.'],
        ['delete', null, 'Deletion could not be confirmed', 'The delete request could not be confirmed and may still be processing. Refresh the product list before retrying; the deletion will not repeat automatically.'],
        ['delete', 500, 'Deletion could not be confirmed', 'The delete request could not be confirmed and may still be processing. Refresh the product list before retrying; the deletion will not repeat automatically.'],
    ])('keeps %s with status %s transport-ambiguous and never definite', (action, status, expectedTitle, expectedMessage) => {
        renderResult({
            action,
            error: Object.assign(new Error('transport detail'), { status, code: 'NETWORK_ERROR' }),
        })

        expect(screen.getByText(expectedTitle)).toBeTruthy()
        expect(screen.getByText(expectedMessage)).toBeTruthy()
        expect(document.body.textContent ?? '').not.toContain('transport detail')

        const bodyText = document.body.textContent ?? ''
        expect(bodyText).not.toMatch(/products (activated|deleted)\b/)
        expect(bodyText).not.toMatch(/\bcould not be completed\b/)

        const panel = resultPanel().querySelector('div.p-3')
        expect(panel?.className).toContain('bg-amber-50')
        expect(panel?.className).not.toContain('bg-green-50')
        expect(panel?.className).not.toContain('bg-red-100')
    })

    it.each([
        [400, 'This bulk request was not valid. Please review the selection and try again.'],
        [403, 'You do not have permission to perform this bulk action.'],
        [409, 'This bulk request conflicts with another change. Please review the products and try again.'],
    ])('keeps definitive safe request errors (%i) distinct from transport uncertainty', (status, expectedMessage) => {
        renderResult({
            error: Object.assign(new Error('definitive raw detail'), { status, code: null }),
        })

        expect(screen.getByText(expectedMessage)).toBeTruthy()
        expect(screen.getByText('The bulk request could not be completed')).toBeTruthy()
        expect(screen.queryByText(/could not be confirmed/i)).toBeNull()
        expect(document.body.textContent ?? '').not.toContain('definitive raw detail')
    })

    it.each([
        ['activate', 'This bulk request conflicts with an earlier request. Refresh the product list before trying again.'],
        ['unpublish', 'A similar bulk request is already in progress. Refresh the product list before trying again.'],
        ['delete', 'This deletion request conflicts with an earlier request. Refresh the product list before trying again.'],
    ])('maps request-level idempotency refusals to safe wording without exposing keys (%s)', (action, expectedMessage) => {
        renderResult({
            action,
            error: Object.assign(new Error('claim=42 lease-token=secret-key'), {
                status: 409,
                code: action === 'unpublish' ? 'IDEMPOTENCY_IN_PROGRESS' : 'IDEMPOTENCY_CONFLICT',
            }),
        })

        expect(screen.getByText(expectedMessage)).toBeTruthy()
        expect(screen.getByText('The bulk request needs attention')).toBeTruthy()
        expect(screen.queryByText('No products were changed')).toBeNull()
        const bodyText = document.body.textContent ?? ''
        expect(bodyText).not.toMatch(/claim=|lease-token|secret-key|Idempotency-Key/i)
        // No retry affordance exists anywhere in the passive presentation.
        expect(resultPanel().querySelectorAll('button')).toHaveLength(1)
    })

    it('keeps a successful outcome plus refresh failure as success plus a separate warning', () => {
        renderResult({
            action: 'publish',
            refreshError: new Error('refresh exploded'),
            result: aggregate(),
        })

        expect(screen.getByText('Products published')).toBeTruthy()
        expect(screen.getByText('Publishing completed, but the product list could not be refreshed.')).toBeTruthy()
        const panel = screen.getByText('Products published').closest('div.p-3')
        expect(panel?.className).toContain('bg-green-50')
        expect(screen.queryByText(/could not be completed/i)).toBeNull()
    })

    it('keeps an uncertain outcome plus refresh failure as uncertainty plus a separate warning', () => {
        renderResult({
            action: 'delete',
            error: Object.assign(new Error('network down'), { status: null }),
            refreshError: new Error('refresh exploded'),
        })

        expect(screen.getByText('Deletion could not be confirmed')).toBeTruthy()
        expect(screen.getByText('The request outcome is uncertain, and the product list could not be refreshed.')).toBeTruthy()
        expect(screen.queryByText(/deletion completed/i)).toBeNull()
    })

    it('keeps a definitive request failure plus refresh failure definitive with a separate neutral refresh warning', () => {
        renderResult({
            error: Object.assign(new Error('forbidden detail'), { status: 403 }),
            refreshError: new Error('refresh exploded'),
        })

        expect(screen.getByText('The bulk request could not be completed')).toBeTruthy()
        expect(screen.getByText('You do not have permission to perform this bulk action.')).toBeTruthy()
        expect(screen.getByText('The product list could not be refreshed.')).toBeTruthy()
        expect(screen.queryByText(/request outcome is uncertain/i)).toBeNull()
    })
})

describe('BulkMutationResult — accessibility, processing lifecycle and dismissal', () => {
    it('renders nothing when idle', () => {
        const { container } = renderResult({})
        expect(container.textContent).toBe('')
    })

    it('announces processing inside the single polite live region without a dismiss control', () => {
        renderResult({ isMutating: true, processingCount: 3, action: 'publish' })

        expect(screen.getByText('Publishing 3 selected product(s)…')).toBeTruthy()
        const live = resultLiveRegion()
        expect(live.getAttribute('aria-live')).toBe('polite')
        expect(live.getAttribute('aria-atomic')).toBe('true')
        expect(live.getAttribute('aria-label')).toBe('Bulk operation result')
        expect(document.querySelectorAll('[role="status"]')).toHaveLength(1)
        expect(resultPanel().querySelectorAll('button')).toHaveLength(0)
    })

    it('exposes one coherent live region with a semantic heading and a keyboard-operable dismiss', async () => {
        const onDismiss = vi.fn()
        const user = userEvent.setup()
        renderResult({ result: aggregate(), onDismiss })

        const regions = document.querySelectorAll('[role="status"]')
        expect(regions.length).toBeGreaterThanOrEqual(1)
        expect(screen.getAllByRole('status').length).toBe(1)

        expect(screen.getByRole('heading', { level: 3, name: 'Products activated' })).toBeTruthy()
        const resultRegion = screen.getByRole('region', { name: 'Products activated' })
        const dismissButton = screen.getByRole('button', { name: 'Dismiss' })
        expect(resultRegion.contains(dismissButton)).toBe(true)

        await user.click(screen.getByRole('button', { name: 'Dismiss' }))
        expect(onDismiss).toHaveBeenCalledTimes(1)

        const dismiss = screen.getByRole('button', { name: 'Dismiss' })
        dismiss.focus()
        await user.keyboard('{Enter}')
        expect(onDismiss).toHaveBeenCalledTimes(2)
    })

    it('keeps the live announcement concise: the per-item list and dismiss stay outside the polite atomic region', () => {
        renderResult({
            refreshError: new Error('refresh exploded'),
            result: aggregate({
                requested: 2,
                accepted: 2,
                processed: 1,
                failed: 1,
                results: [
                    item({ productId: 'p1', status: 'processed' }),
                    item({ productId: 'p2', status: 'failed', code: 'CONCURRENCY_CONFLICT' }),
                ],
            }),
        })

        const live = resultLiveRegion()

        // Concise summary is announced atomically: heading, counts, refresh warning.
        expect(within(live).getByText('Activate completed, but some products were not changed')).toBeTruthy()
        expect(within(live).getByText('Selected: 2')).toBeTruthy()
        expect(within(live).getByText('Failed: 1')).toBeTruthy()
        expect(within(live).getByText('The operation completed, but the products list could not be refreshed.'))
            .toBeTruthy()

        // The unbounded per-item list and the dismiss control are visible but
        // never part of the live announcement.
        const itemList = within(resultPanel()).getByRole('list')
        const resultRegion = screen.getByRole('region', {
            name: 'Activate completed, but some products were not changed',
        })
        expect(resultRegion.contains(itemList)).toBe(true)
        expect(itemList.contains(live)).toBe(false)
        expect(live.querySelector('ul')).toBeNull()
        expect(within(resultPanel()).getAllByRole('listitem')).toHaveLength(1)
        expect(live.contains(screen.getByRole('button', { name: 'Dismiss' }))).toBe(false)
    })

    it('renders long names/messages with wrapping and logical (RTL-safe) spacing classes', () => {
        renderResult({
            result: aggregate({
                requested: 2,
                accepted: 2,
                processed: 0,
                unchanged: 1,
                failed: 1,
                results: [
                    item({ productId: 'p2', status: 'unchanged', code: 'ALREADY_ACTIVE' }),
                    item({ productId: 'p1', status: 'failed', code: 'CONCURRENCY_CONFLICT' }),
                ],
            }),
        })

        const source = readFileSync(join(dirname(fileURLToPath(import.meta.url)), 'BulkMutationResult.jsx'), 'utf8')
        expect(source).toMatch(/\bps-\d/) // logical padding-start
        expect(source).toMatch(/\bms-\d/) // logical margin-start
        expect(source).toMatch(/break-words/)
        expect(source).not.toMatch(/[\s"'`](ml|mr|pl|pr)-\d/) // no physical directional spacing
        expect(source).not.toMatch(/text-(left|right)/)
    })
})

describe('BulkMutationResult — presentation model unit behavior', () => {
    it('resolves action presentations with a safe activate fallback', () => {
        expect(resolveBulkActionPresentation('publish').successTitleKey).toBe('products.bulkSuccessTitlePublish')
        expect(resolveBulkActionPresentation('unknown-action').processingKey).toBe('products.bulkProcessingActivate')
    })

    it('keeps machine-code reasons action-aware with a null safe fallback', () => {
        expect(resolveBulkItemReasonKey({ action: 'publish', code: 'PRODUCT_INACTIVE' }))
            .toBe('products.bulkItemInactiveCannotPublish')
        expect(resolveBulkItemReasonKey({ action: 'activate', code: 'PRODUCT_INACTIVE' })).toBeNull()
        expect(resolveBulkItemReasonKey({ action: 'delete', code: 'DEPENDENCY_BLOCKED' }))
            .toBe('products.bulkItemDependencyBlocked')
        expect(resolveBulkItemReasonKey({ action: 'delete', code: 'DEPENDENCY_BLOCKED' }))
            .not.toBe(resolveBulkItemReasonKey({ action: 'activate', code: 'DEPENDENCY_BLOCKED' }))
    })

    it('returns a processing model while mutating regardless of stale terminal state', () => {
        const model = buildBulkMutationPresentation({
            action: 'delete',
            isMutating: true,
            processingCount: 2,
            result: aggregate(),
            t: mockT,
        })
        expect(model).toMatchObject({ kind: 'processing' })
        expect(model).toMatchObject({ processingText: 'Deleting 2 selected product(s)…' })
    })

    it('returns null when idle with neither result nor error', () => {
        expect(buildBulkMutationPresentation({ action: 'activate', t: mockT })).toBeNull()
    })
})

describe('BulkMutationResult — en/ar locale parity for shared bulk keys', () => {
    const currentDir = dirname(fileURLToPath(import.meta.url))

    function productsBlock(locale: 'en' | 'ar') {
        return JSON.parse(readFileSync(join(currentDir, '..', '..', 'i18n', 'locales', `${locale}.json`), 'utf8'))
    }

    it('keeps every bulk key present in both locales with exact placeholder parity', () => {
        const en = productsBlock('en')
        const ar = productsBlock('ar')
        const bulkKeys = Object.keys(en.products).filter((key) => key.startsWith('bulk'))

        expect(bulkKeys.length).toBeGreaterThan(0)
        expect(Object.keys(ar.products).filter((key) => key.startsWith('bulk')).sort()).toEqual(bulkKeys.sort())

        for (const key of bulkKeys) {
            expect(en.products[key], `missing en products.${key}`).toBeTruthy()
            expect(ar.products[key], `missing ar products.${key}`).toBeTruthy()
            const enPlaceholders = String(en.products[key]).match(/{{[^}]+}}/g) || []
            const arPlaceholders = String(ar.products[key]).match(/{{[^}]+}}/g) || []
            expect(arPlaceholders.sort(), `placeholder mismatch for products.${key}`).toEqual(enPlaceholders.sort())
        }
    })

    it('contains the Phase 6G.6 shared keys required by the presentation model', () => {
        const en = productsBlock('en')
        const ar = productsBlock('ar')

        for (const key of [
            'bulkCountAccepted',
            'bulkItemStatusFailed',
            'bulkItemStatusUnchanged',
            'bulkResultRegionAria',
            'bulkRequestUncertainTitle',
            'bulkRequestUncertainMessage',
            'bulkNoChangesTitle',
            'bulkRequestIdempotencyConflictMessage',
            'bulkRequestIdempotencyInProgressMessage',
            'bulkItemUnknownUnchangedReason',
            'bulkRequestRefreshWarning',
            'bulkRequestFailedTitle',
            'bulkRequestAttentionTitle',
        ]) {
            expect(en.products[key], `en products.${key} missing`).toBeTruthy()
            expect(ar.products[key], `ar products.${key} missing`).toBeTruthy()
        }

        expect(en.products.bulkCountAccepted).toBe('Accepted: {{count}}')
        expect(en.products.bulkNoChangesTitle).toBe('No products were changed')
        expect(en.products.bulkRequestUncertainTitle).toMatch(/could not be confirmed/i)
        expect(en.products.bulkRequestUncertainMessage).toMatch(/may still be processing/i)
        expect(en.products.bulkRequestUncertainMessage).toMatch(/refresh the product list before retrying/i)
    })
})
