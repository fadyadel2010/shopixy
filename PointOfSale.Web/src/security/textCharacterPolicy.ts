// UTF-16 code-unit checks intentionally preserve the former ASCII regex rules,
// including lone surrogates. Do not broaden these to Unicode control categories.
export function containsAsciiControl(value: string): boolean {
    for (let index = 0; index < value.length; index += 1) {
        const code = value.charCodeAt(index)
        if (code <= 31 || code === 127) return true
    }
    return false
}

export function containsNonAscii(value: string): boolean {
    for (let index = 0; index < value.length; index += 1) {
        if (value.charCodeAt(index) > 127) return true
    }
    return false
}
