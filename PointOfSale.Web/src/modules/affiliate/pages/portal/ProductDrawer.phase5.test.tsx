import { act, render } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import ProductDrawer from './ProductDrawer'

vi.mock('../../../../services/affiliateApi', () => ({ default: {
  getProductIntelligence: vi.fn().mockResolvedValue(null), createPromotionLink: vi.fn(),
} }))
vi.mock('../../i18n/LanguageContext', () => ({ useLanguage: () => ({ t: (key: string) => key }) }))

describe('ProductDrawer hook order', () => {
  it('can open and close the same mounted drawer without changing hook order', async () => {
    const close = vi.fn()
    const { rerender, container } = render(<ProductDrawer productId={null} onClose={close} />)
    expect(container).toBeEmptyDOMElement()
    await act(async () => rerender(<ProductDrawer productId="synthetic-product" onClose={close} />))
    await act(async () => rerender(<ProductDrawer productId={null} onClose={close} />))
    expect(container).toBeEmptyDOMElement()
  })
})
