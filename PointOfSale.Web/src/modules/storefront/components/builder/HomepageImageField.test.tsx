import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { HomepageImageField } from './HomepageImageField'
import { isValidImageUrl } from './homepageImageUrl'
import en from '../../../../i18n/locales/en.json'
import ar from '../../../../i18n/locales/ar.json'
import { readTranslation } from '../../../../tests/readTranslation'

const api = vi.hoisted(() => ({ post: vi.fn(), get: vi.fn(), language: 'en' }))
vi.mock('../../../../services/api', () => ({ default: api }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({
    i18n: { language: api.language },
    t: (key: string) => readTranslation(api.language === 'ar' ? ar : en, key),
}) }))

afterEach(() => { cleanup(); vi.clearAllMocks(); api.language = 'en' })

describe('Homepage image authoring', () => {
    it('uploads device image and persists only the canonical reference after successful registration', async () => {
        const reference = '/api/storefront/homepage-media/11111111-1111-4111-8111-111111111111'
        api.post.mockResolvedValue({ data: { mediaId: reference.split('/').pop(), reference } })
        api.get.mockResolvedValue({ data: { url: 'https://private.example/short-lived-preview' } })
        const onChange = vi.fn()
        const { rerender } = render(<HomepageImageField label="Background Image" value="" onChange={onChange} />)
        fireEvent.change(screen.getByLabelText('Background Image'), { target: { files: [new File(['png'], 'hero.png', { type: 'image/png' })] } })
        await waitFor(() => expect(onChange).toHaveBeenCalledWith(reference))
        expect(api.post.mock.calls[0][0]).toBe('/admin/homepage-media')
        expect(api.post.mock.calls[0][1]).toBeInstanceOf(FormData)
        rerender(<HomepageImageField label="Background Image" value={reference} onChange={onChange} />)
        await waitFor(() => expect(screen.getByRole('img').getAttribute('src')).toContain('short-lived-preview'))
        expect(screen.queryByRole('textbox')).toBeNull()
        fireEvent.click(screen.getByRole('button', { name: 'Remove image' }))
        expect(onChange).toHaveBeenLastCalledWith('')
    })

    it('failed replace preserves the old draft asset and offers a localized actionable error', async () => {
        api.post.mockRejectedValue(new Error('storage unavailable'))
        const onChange = vi.fn()
        render(<HomepageImageField label="Background Image" value="https://example.com/old.png" onChange={onChange} />)
        fireEvent.change(screen.getByLabelText('Background Image'), { target: { files: [new File(['png'], 'new.png', { type: 'image/png' })] } })
        expect(await screen.findByRole('alert')).toHaveTextContent(en.homepageMedia.error)
        expect(onChange).not.toHaveBeenCalled()
        expect(screen.getByRole('img').getAttribute('src')).toBe('https://example.com/old.png')
    })

    it.each(['en', 'ar'])('URL compatibility, invalid URL state and device controls are localized in %s', (language) => {
        api.language = language
        const messages = language === 'ar' ? ar : en
        const { rerender } = render(<HomepageImageField label="Background Image" value="https://example.com/legacy.png" onChange={vi.fn()} />)
        expect(screen.getByRole('button', { name: messages.homepageMedia.replace })).toBeTruthy()
        expect(screen.getByRole('img').getAttribute('src')).toBe('https://example.com/legacy.png')
        rerender(<HomepageImageField label="Background Image" value="javascript:alert(1)" onChange={vi.fn()} />)
        expect(screen.getByRole('alert')).toHaveTextContent(messages.homepageMedia.invalid)
        expect(screen.queryByRole('img')).toBeNull()
    })

    it('rejects unsafe external candidates', () => {
        for (const url of ['javascript:alert(1)', '//foreign.example/image', 'https://user:password@host/image', 'https://host/a b']) expect(isValidImageUrl(url)).toBe(false)
        expect(isValidImageUrl('https://host/image.jpg')).toBe(true)
        expect(isValidImageUrl('images/legacy.jpg')).toBe(true)
    })
})
