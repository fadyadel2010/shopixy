import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ReviewModal } from './ReviewModal'

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key: string, options?: { count?: number }) =>
      options?.count === undefined ? key : `${key} ${options.count}`,
  }),
}))

afterEach(() => {
  vi.restoreAllMocks()
})

describe('ReviewModal rating workflow presentation', () => {
  it('keeps review intent open when authoritative submission fails', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn().mockRejectedValue(new Error('rejected'))
    const onClose = vi.fn()
    vi.spyOn(window, 'alert').mockImplementation(() => undefined)

    render(
      <ReviewModal
        isRTL={false}
        requestId="11111111-1111-4111-8111-111111111111"
        onSubmit={onSubmit}
        onClose={onClose}
      />,
    )

    await user.click(screen.getByRole('radio', { name: /ratingOption 5/i }))
    await user.click(screen.getByRole('button', { name: 'servicesMarketplace.review.submit' }))

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1))
    expect(onClose).not.toHaveBeenCalled()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('synchronously fences repeated submission while the first attempt is pending', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn(() => new Promise<void>(() => undefined))

    render(
      <ReviewModal
        isRTL={false}
        requestId="11111111-1111-4111-8111-111111111111"
        onSubmit={onSubmit}
        onClose={vi.fn()}
      />,
    )

    await user.click(screen.getByRole('radio', { name: /ratingOption 5/i }))
    const submit = screen.getByRole('button', { name: 'servicesMarketplace.review.submit' })
    await user.dblClick(submit)

    expect(onSubmit).toHaveBeenCalledTimes(1)
    expect(submit).toBeDisabled()
  })
})
