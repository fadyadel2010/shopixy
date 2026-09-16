import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AiProductGeneratorModal from './AiProductGeneratorModal'

const apiPostMock = vi.hoisted(() => vi.fn())

vi.mock('../services/api', () => ({
    default: {
        post: apiPostMock,
    },
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string, fallback?: string) => fallback || key,
    }),
}))

function fillIdea(container: HTMLElement, value = 'Wireless RGB gaming mouse') {
    const textarea = container.querySelector('textarea')!
    fireEvent.change(textarea, { target: { value } })
}

function clickGenerate() {
    fireEvent.click(screen.getByRole('button', { name: /Generate Product|Generating/i }))
}

function renderOpen(props: Record<string, unknown> = {}) {
    return render(
        <AiProductGeneratorModal
            isOpen
            onClose={vi.fn()}
            onApply={vi.fn()}
            {...props}
        />
    )
}

describe('AiProductGeneratorModal generation lock (Wave 3)', () => {
    beforeEach(() => {
        apiPostMock.mockReset()
        vi.useRealTimers()
    })

    it('shows an accessible loading status immediately and locks conflicting controls', async () => {
        let resolvePost!: (value: unknown) => void
        apiPostMock.mockImplementation(() => new Promise((resolve) => { resolvePost = resolve }))
        const { container } = renderOpen()
        fillIdea(container)

        clickGenerate()

        // Loading state is rendered synchronously with the request start.
        expect(await screen.findByRole('status')).toHaveTextContent('AI generation is in progress')
        const form = container.querySelector('form')!
        expect(form.getAttribute('aria-busy')).toBe('true')

        // Conflicting controls are locked: idea, selects, numbers, generate.
        expect(container.querySelector('textarea')).toBeDisabled()
        container.querySelectorAll('select').forEach((select) => expect(select).toBeDisabled())
        container.querySelectorAll('input[type="number"]').forEach((input) => expect(input).toBeDisabled())
        expect(screen.getByRole('button', { name: /Generating/i })).toBeDisabled()

        await act(async () => { resolvePost({ data: { name: 'AI Widget' } }) })
    })

    it('issues exactly one API call for duplicate submissions while loading', async () => {
        let resolvePost!: (value: unknown) => void
        apiPostMock.mockImplementation(() => new Promise((resolve) => { resolvePost = resolve }))
        const { container } = renderOpen()
        fillIdea(container)

        clickGenerate()
        clickGenerate()
        clickGenerate()

        expect(apiPostMock).toHaveBeenCalledTimes(1)

        await act(async () => { resolvePost({ data: { name: 'AI Widget' } }) })
    })

    it('uses a synchronous lock before React renders the loading state', async () => {
        let resolvePost!: (value: unknown) => void
        apiPostMock.mockImplementation(() => new Promise((resolve) => { resolvePost = resolve }))
        const { container } = renderOpen()
        fillIdea(container)

        const form = container.querySelector('form')!
        act(() => {
            fireEvent.submit(form)
            fireEvent.submit(form)
        })

        expect(apiPostMock).toHaveBeenCalledTimes(1)
        await act(async () => { resolvePost({ data: { name: 'AI Widget' } }) })
    })

    it('restores controls after success and applies the result exactly once', async () => {
        apiPostMock.mockResolvedValue({ data: { name: 'AI Widget', slug: 'ai-widget' } })
        const onApply = vi.fn()
        const onClose = vi.fn()
        const { container } = renderOpen({ onApply, onClose })
        fillIdea(container)

        clickGenerate()

        await waitFor(() => expect(screen.getByText('AI Widget')).toBeInTheDocument())

        // Controls are restored.
        expect(container.querySelector('textarea')).not.toBeDisabled()
        expect(screen.getByRole('button', { name: 'Generate Product' })).not.toBeDisabled()
        expect(screen.queryByRole('status')).not.toBeInTheDocument()

        // Double activation applies only once.
        const applyButton = screen.getByRole('button', { name: 'Apply to Product Form' })
        fireEvent.click(applyButton)
        fireEvent.click(applyButton)

        expect(onApply).toHaveBeenCalledTimes(1)
        expect(onClose).toHaveBeenCalledTimes(1)
    })

    it('restores controls after failure, announces the error, and allows retry', async () => {
        apiPostMock
            .mockRejectedValueOnce({ response: { data: { message: 'AI engine overloaded' } } })
            .mockResolvedValueOnce({ data: { name: 'Recovered Widget' } })
        const { container } = renderOpen()
        fillIdea(container)

        clickGenerate()

        const alert = await screen.findByRole('alert')
        expect(alert).toHaveTextContent('AI engine overloaded')
        await waitFor(() => expect(alert).toHaveFocus())

        // Failure restores every conflicting control so the user can retry.
        expect(container.querySelector('textarea')).not.toBeDisabled()
        expect(screen.getByRole('button', { name: 'Generate Product' })).not.toBeDisabled()

        clickGenerate()

        await waitFor(() => expect(apiPostMock).toHaveBeenCalledTimes(2))
        await waitFor(() => expect(screen.getByText('Recovered Widget')).toBeInTheDocument())
        expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('ignores a stale response that arrives after the modal was closed', async () => {
        let resolvePost!: (value: unknown) => void
        apiPostMock.mockImplementation(() => new Promise((resolve) => { resolvePost = resolve }))
        const { container, rerender } = renderOpen()
        fillIdea(container)
        clickGenerate()
        expect(apiPostMock).toHaveBeenCalledTimes(1)

        // Close the modal while the request is in flight, then reopen.
        rerender(<AiProductGeneratorModal isOpen={false} onClose={vi.fn()} onApply={vi.fn()} />)
        rerender(<AiProductGeneratorModal isOpen onClose={vi.fn()} onApply={vi.fn()} />)

        await act(async () => { resolvePost({ data: { name: 'Stale Widget' } }) })

        // The late response must not repopulate the reset modal.
        expect(screen.queryByText('Stale Widget')).not.toBeInTheDocument()
        expect(screen.queryByRole('status')).not.toBeInTheDocument()
    })

    it('requires a product idea before calling the API', () => {
        renderOpen()

        clickGenerate()

        expect(apiPostMock).not.toHaveBeenCalled()
    })
})
