export function readTranslation(messages: unknown, key: string): string {
    let value: unknown = messages
    for (const part of key.split('.')) {
        value = typeof value === 'object' && value !== null && part in value
            ? Reflect.get(value, part) : undefined
    }
    return typeof value === 'string' && value ? value : key
}
