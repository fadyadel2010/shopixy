import { describe, expect, it } from 'vitest'
import { sanitizeRichTextHtml } from './htmlSanitizer'

describe('typed rich text sanitizer hook registration', () => {
    it('retains safe rich text and secures blank links across repeated sanitization', () => {
        const source = '<h2>Heading</h2><a href="https://example.com" target="_blank" rel="author">Link</a>'
        const first = sanitizeRichTextHtml(source)
        expect(first).toContain('<h2>Heading</h2>')
        expect(first).toContain('rel="author noopener noreferrer"')
        expect(sanitizeRichTextHtml(source)).toBe(first)
    })
    it.each(['javascript:alert(1)', '//untrusted.invalid', 'data:text/html,unsafe'])('rejects unsafe href %s and event attributes', href => {
        const result = sanitizeRichTextHtml(`<a href="${href}" onclick="alert(1)">Link</a><script>alert(1)</script>`)
        expect(result).not.toContain('href=')
        expect(result).not.toContain('onclick')
        expect(result).not.toContain('<script')
    })
    it('retains safe relative links and empty-input behavior', () => {
        expect(sanitizeRichTextHtml('<a href="/catalog">Catalog</a>')).toContain('href="/catalog"')
        expect(sanitizeRichTextHtml(undefined)).toBe('')
    })
})
