import { describe, expect, it } from 'vitest'
import {
    PUBLISH_STATUS_ARCHIVED,
    PUBLISH_STATUS_DRAFT,
    PUBLISH_STATUS_PUBLISHED,
    normalizePublishStatus,
} from './productStatus'

describe('normalizePublishStatus', () => {
    it('recognizes the canonical string enum values emitted by JsonStringEnumConverter', () => {
        expect(normalizePublishStatus('Draft')).toBe(PUBLISH_STATUS_DRAFT)
        expect(normalizePublishStatus('Published')).toBe(PUBLISH_STATUS_PUBLISHED)
        expect(normalizePublishStatus('Archived')).toBe(PUBLISH_STATUS_ARCHIVED)
    })

    it('is case-insensitive and tolerant of surrounding whitespace', () => {
        expect(normalizePublishStatus('draft')).toBe(PUBLISH_STATUS_DRAFT)
        expect(normalizePublishStatus('  PUBLISHED  ')).toBe(PUBLISH_STATUS_PUBLISHED)
        expect(normalizePublishStatus('archived')).toBe(PUBLISH_STATUS_ARCHIVED)
    })

    it('supports numeric representations for legacy compatibility', () => {
        expect(normalizePublishStatus(0)).toBe(PUBLISH_STATUS_DRAFT)
        expect(normalizePublishStatus(1)).toBe(PUBLISH_STATUS_PUBLISHED)
        expect(normalizePublishStatus(2)).toBe(PUBLISH_STATUS_ARCHIVED)
    })

    it('supports numeric string representations', () => {
        expect(normalizePublishStatus('0')).toBe(PUBLISH_STATUS_DRAFT)
        expect(normalizePublishStatus('1')).toBe(PUBLISH_STATUS_PUBLISHED)
        expect(normalizePublishStatus('2')).toBe(PUBLISH_STATUS_ARCHIVED)
    })

    it('fails closed to Draft for unknown values — unknown must never become Published', () => {
        expect(normalizePublishStatus('SomethingElse')).toBe(PUBLISH_STATUS_DRAFT)
        expect(normalizePublishStatus('SomethingElse')).not.toBe(PUBLISH_STATUS_PUBLISHED)
        expect(normalizePublishStatus(7)).toBe(PUBLISH_STATUS_DRAFT)
        expect(normalizePublishStatus('3')).toBe(PUBLISH_STATUS_DRAFT)
    })

    it('fails closed to Draft for missing or malformed input', () => {
        expect(normalizePublishStatus(null)).toBe(PUBLISH_STATUS_DRAFT)
        expect(normalizePublishStatus(undefined)).toBe(PUBLISH_STATUS_DRAFT)
        expect(normalizePublishStatus('')).toBe(PUBLISH_STATUS_DRAFT)
        expect(normalizePublishStatus({})).toBe(PUBLISH_STATUS_DRAFT)
        expect(normalizePublishStatus(Number.NaN)).toBe(PUBLISH_STATUS_DRAFT)
    })
})
