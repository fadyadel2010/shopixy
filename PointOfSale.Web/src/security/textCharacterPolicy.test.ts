import { describe, expect, it } from 'vitest'
import { containsAsciiControl, containsNonAscii } from './textCharacterPolicy'

describe('ASCII character policy, without security-regex lint exceptions', () => {
    it('preserves every UTF-16 code-unit classification and boundary', () => {
        const mismatches: number[] = []
        for (let code = 0; code <= 65535; code += 1) {
            const value = `safe${String.fromCharCode(code)}suffix`
            if (containsAsciiControl(value) !== (code <= 31 || code === 127)
                || containsNonAscii(value) !== (code > 127)) mismatches.push(code)
        }
        expect(mismatches).toEqual([])
        expect(containsAsciiControl('')).toBe(false)
        expect(containsNonAscii('')).toBe(false)
    })
})
