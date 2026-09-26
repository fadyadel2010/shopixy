export async function runWithCrossTabRefreshLock<T>(
    name: string,
    operation: () => Promise<T>,
): Promise<T> {
    if (typeof navigator !== 'undefined' && navigator.locks?.request) {
        return navigator.locks.request(name, { mode: 'exclusive' }, operation)
    }

    return operation()
}
