import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import VariantOptionsBuilder from './VariantOptionsBuilder'

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string) => key,
    }),
}))

function renderBuilderInForm(props = {}) {
    const onSubmit = vi.fn((event) => event.preventDefault())
    const onChange = vi.fn()
    const view = render(
        <form onSubmit={onSubmit}>
            <VariantOptionsBuilder
                optionGroups={[{ id: 'group-1', name: '', values: [] }]}
                onChange={onChange}
                {...props}
            />
            <button type="submit">Save Product</button>
        </form>
    )
    return { onSubmit, onChange, ...view }
}

describe('VariantOptionsBuilder Enter-key behavior (Wave 2 Finding 9)', () => {
    it('does not submit the outer product form when Enter is pressed in Option Name', () => {
        const { onSubmit, container } = renderBuilderInForm()

        const nameInput = container.querySelector(
            'input[placeholder="e.g. Size, Color, Material"]'
        ) as HTMLInputElement
        fireEvent.change(nameInput, { target: { value: 'Color' } })

        // jsdom does not implement implicit Enter-to-submit, so assert the
        // guard directly: the handler must cancel the keypress (which is what
        // stops the outer product form submission in a real browser).
        const event = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })
        const preventDefault = vi.spyOn(event, 'preventDefault')
        nameInput.dispatchEvent(event)

        expect(preventDefault).toHaveBeenCalled()
        expect(onSubmit).not.toHaveBeenCalled()
    })

    it('advances focus to the value input when Enter is pressed in Option Name', () => {
        const { container } = renderBuilderInForm()

        const nameInput = container.querySelector(
            'input[placeholder="e.g. Size, Color, Material"]'
        ) as HTMLInputElement
        fireEvent.change(nameInput, { target: { value: 'Color' } })
        fireEvent.keyDown(nameInput, { key: 'Enter' })

        const valueInput = container.querySelector(
            'input[placeholder="Type a value and press Enter"]'
        ) as HTMLInputElement
        expect(document.activeElement).toBe(valueInput)
    })

    it('still adds the value and never submits when Enter is pressed in a Value input', () => {
        const { onSubmit, onChange, container } = renderBuilderInForm({
            optionGroups: [{ id: 'group-1', name: 'Color', values: [] }],
        })

        const valueInput = container.querySelector(
            'input[placeholder="Type a value and press Enter"]'
        ) as HTMLInputElement
        fireEvent.change(valueInput, { target: { value: 'Red' } })
        fireEvent.keyDown(valueInput, { key: 'Enter' })

        expect(onSubmit).not.toHaveBeenCalled()
        expect(onChange).toHaveBeenCalledWith([
            expect.objectContaining({ id: 'group-1', values: ['Red'] }),
        ])
    })

    it('keeps explicit Save working after the Enter-key guard', () => {
        const { onSubmit } = renderBuilderInForm()

        fireEvent.click(screen.getByRole('button', { name: 'Save Product' }))

        expect(onSubmit).toHaveBeenCalledTimes(1)
    })
})
