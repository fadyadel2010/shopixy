// datetime-local is deliberately shown in UTC, matching the API contract. Keep
// the original ISO value if untouched (including sub-second precision).
export const offerDateInput = value => value ? value.replace(/Z$/, '') : ''
export const offerDatePayload = (value, original) => {
    if (!value) return null
    if (original && value === offerDateInput(original)) return original
    return new Date(`${value}Z`).toISOString()
}
