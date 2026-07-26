export const buildIdempotencyHeaders = (idempotencyKey, paymentAttemptId, posCartId) => ({
    'Idempotency-Key': idempotencyKey,
    'X-Request-Id': idempotencyKey,
    ...(paymentAttemptId ? { 'X-Payment-Attempt-Id': paymentAttemptId } : {}),
    ...(posCartId ? { 'X-Pos-Cart-Id': posCartId } : {}),
})

export const stableStringify = (value) => JSON.stringify(value, (_, currentValue) => {
    if (currentValue && typeof currentValue === 'object' && !Array.isArray(currentValue)) {
        return Object.keys(currentValue)
            .sort()
            .reduce((sortedObject, key) => {
                sortedObject[key] = currentValue[key]
                return sortedObject
            }, {})
    }

    return currentValue
})
