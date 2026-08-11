const UPLOAD_TYPES = new Set(['IMAGE', 'FILE', 'MULTI_IMAGE'])

function parseJson(value, fallback) {
    try {
        return JSON.parse(value)
    } catch {
        return fallback
    }
}

export function presentOrderCheckoutFields(snapshots = []) {
    return snapshots
        .filter((snapshot) => String(snapshot?.fieldKind || '').toUpperCase() === 'CUSTOM')
        .filter((snapshot) => snapshot.isVisible !== false)
        .sort((left, right) => Number(left.displayOrder || 0) - Number(right.displayOrder || 0))
        .map((snapshot) => {
            const fieldType = String(snapshot.fieldType || '').toUpperCase()
            const typedValue = snapshot.submittedValue || null
            const value = typedValue ? null : parseJson(snapshot.valueJson || 'null', null)
            let displayValue = value == null ? '' : String(value)
            let attachments = []

            if (typedValue) {
                attachments = Array.isArray(typedValue.attachments)
                    ? typedValue.attachments
                        .filter((item) => item?.attachmentId && item?.fileName)
                        .map((item) => ({
                            attachmentId: item.attachmentId,
                            fileName: item.fileName,
                            contentType: item.contentType || '',
                            sizeBytes: Number(item.sizeBytes || 0),
                        }))
                    : []
                displayValue = typedValue.displayValue || attachments.map((item) => item.fileName).join(', ')
            } else if (UPLOAD_TYPES.has(fieldType)) {
                attachments = Array.isArray(value)
                    ? value
                        .filter((item) => item?.attachmentId && item?.fileName)
                        .map((item) => ({
                            attachmentId: item.attachmentId,
                            fileName: item.fileName,
                            contentType: item.contentType || '',
                            sizeBytes: Number(item.sizeBytes || 0),
                        }))
                    : []
                displayValue = attachments.map((item) => item.fileName).join(', ')
            } else if (fieldType === 'CHECKBOX') {
                displayValue = value === true ? 'Yes' : 'No'
            } else if (fieldType === 'SELECT' || fieldType === 'RADIO') {
                const configuration = snapshot.configuration || parseJson(snapshot.configurationJson || '{}', {})
                displayValue = configuration?.options?.find((option) => option.value === value)?.label || displayValue
            }

            return {
                fieldId: snapshot.fieldId,
                label: snapshot.label || 'Custom field',
                displayValue: displayValue || 'Not provided',
                attachments,
            }
        })
}
