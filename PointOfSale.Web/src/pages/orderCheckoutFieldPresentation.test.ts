import { describe, expect, it } from 'vitest'
import { presentOrderCheckoutFields } from './orderCheckoutFieldPresentation'

describe('order checkout field presentation', () => {
    it('uses snapshotted option labels and never exposes attachment object keys', () => {
        const result = presentOrderCheckoutFields([
            {
                fieldId: 'size',
                fieldKind: 'CUSTOM',
                fieldType: 'SELECT',
                label: 'Size',
                displayOrder: 1,
                configurationJson: JSON.stringify({ options: [{ value: 'l', label: 'Large' }] }),
                valueJson: JSON.stringify('l'),
            },
            {
                fieldId: 'proof',
                fieldKind: 'CUSTOM',
                fieldType: 'FILE',
                label: 'Proof',
                displayOrder: 2,
                configurationJson: '{}',
                valueJson: JSON.stringify([{ attachmentId: 'a', fileName: 'proof.pdf', sizeBytes: 20, objectKey: 'private/key' }]),
            },
        ])

        expect(result[0].displayValue).toBe('Large')
        expect(result[1].displayValue).toBe('proof.pdf')
        expect(JSON.stringify(result)).not.toContain('private/key')
    })

    it('omits fields that were not visible when the order was submitted', () => {
        expect(presentOrderCheckoutFields([
            {
                fieldId: 'hidden-field',
                fieldKind: 'CUSTOM',
                fieldType: 'TEXT',
                label: 'Hidden answer',
                isVisible: false,
                valueJson: 'null',
                configurationJson: '{}',
            },
        ])).toEqual([])
    })

    it('retains safe preview metadata and infers legacy image media type', () => {
        const [field] = presentOrderCheckoutFields([{
            fieldId: 'photo',
            fieldKind: 'CUSTOM',
            fieldType: 'IMAGE',
            label: 'Photo',
            valueJson: JSON.stringify([{
                attachmentId: 'image-1',
                fileName: 'photo.webp',
                contentType: 'image/webp',
                sizeBytes: 128,
                finalETag: 'private-etag',
                finalChecksumSha256: 'private-checksum',
            }]),
        }])

        expect(field.attachments).toEqual([{
            attachmentId: 'image-1',
            fileName: 'photo.webp',
            contentType: 'image/webp',
            sizeBytes: 128,
            mediaType: 'image',
        }])
        expect(JSON.stringify(field)).not.toContain('private-etag')
        expect(JSON.stringify(field)).not.toContain('private-checksum')
    })

    it('prefers typed API presentation fields over raw snapshot json', () => {
        const result = presentOrderCheckoutFields([
            {
                fieldId: 'proof',
                fieldKind: 'CUSTOM',
                fieldType: 'FILE',
                label: 'Proof',
                displayOrder: 1,
                submittedValue: {
                    kind: 'ATTACHMENTS',
                    displayValue: 'typed-proof.pdf',
                    attachments: [
                        {
                            attachmentId: 'attachment-1',
                            fileName: 'typed-proof.pdf',
                            contentType: 'application/pdf',
                            sizeBytes: 42,
                            objectKey: 'private/object/key',
                        },
                    ],
                },
                valueJson: JSON.stringify([{ attachmentId: 'legacy', fileName: 'legacy.pdf' }]),
            },
        ])

        expect(result[0].displayValue).toBe('typed-proof.pdf')
        expect(result[0].attachments[0]).toEqual({
            attachmentId: 'attachment-1',
            fileName: 'typed-proof.pdf',
            contentType: 'application/pdf',
            sizeBytes: 42,
            mediaType: 'file',
        })
        expect(JSON.stringify(result)).not.toContain('private/object/key')
        expect(JSON.stringify(result)).not.toContain('legacy.pdf')
    })
})
