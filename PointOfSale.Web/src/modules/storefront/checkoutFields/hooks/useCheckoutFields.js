import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { getSafeApiErrorMessage, isRequestCanceled } from '../../../../services/api'
import checkoutFieldsService from '../services/checkoutFieldsService'

const sortFields = (fields = []) => [...fields].sort((a, b) => {
    const left = Number(a.displayOrder ?? 0)
    const right = Number(b.displayOrder ?? 0)
    return left - right
})

export function hasSystemFields(fields = []) {
    return fields.some((field) => String(field.fieldKind || '').toUpperCase() === 'SYSTEM')
}

export function useCheckoutFields() {
    const { t } = useTranslation()
    const [fields, setFields] = useState([])
    const [state, setState] = useState('loading')
    const [error, setError] = useState(null)
    const [mutationError, setMutationError] = useState(null)
    const [isMutating, setIsMutating] = useState(false)
    const [isOrdering, setIsOrdering] = useState(false)
    const initializedRef = useRef(false)
    const mountedRef = useRef(false)
    const loadSequenceRef = useRef(0)
    const orderingRef = useRef(false)
    const mutatingRef = useRef(false)

    useEffect(() => {
        mountedRef.current = true
        return () => {
            mountedRef.current = false
            loadSequenceRef.current += 1
        }
    }, [])

    const loadFields = useCallback(async ({ allowInitialize = true, showLoading = true } = {}) => {
        const sequence = ++loadSequenceRef.current
        if (showLoading) {
            setState('loading')
        }
        setError(null)

        try {
            const response = await checkoutFieldsService.getFields()
            if (!mountedRef.current || sequence !== loadSequenceRef.current) return null
            if (!Array.isArray(response?.fields)) throw new Error('Invalid checkout field response.')
            const nextFields = sortFields(response.fields)

            if (allowInitialize && !initializedRef.current && !hasSystemFields(nextFields)) {
                initializedRef.current = true
                const initialized = await checkoutFieldsService.initializeSystemFields()
                if (!Array.isArray(initialized?.fields)) throw new Error('Invalid checkout field initialization response.')
                if (mountedRef.current && sequence === loadSequenceRef.current) {
                    setFields(sortFields(initialized?.fields || []))
                    setState('success')
                }
                return sortFields(initialized?.fields || [])
            }

            if (mountedRef.current && sequence === loadSequenceRef.current) {
                setFields(nextFields)
                setState('success')
            }
            return nextFields
        } catch (err) {
            if (isRequestCanceled(err) || !mountedRef.current || sequence !== loadSequenceRef.current) return null
            initializedRef.current = false
            setError(getSafeApiErrorMessage(err, 'Unable to load checkout fields.'))
            setState(err?.response?.status === 403 ? 'forbidden' : 'error')
            return null
        }
    }, [])

    useEffect(() => {
        loadFields()
    }, [loadFields])

    const executeMutation = useCallback(async (action, fallbackMessage) => {
        if (mutatingRef.current || orderingRef.current) throw new Error('A checkout field operation is already in progress.')
        mutatingRef.current = true
        loadSequenceRef.current += 1
        setIsMutating(true)
        setMutationError(null)
        try {
            const result = await action()
            const readback = await loadFields({ showLoading: false, allowInitialize: false })
            const readbackFailed = readback === null
            if (readbackFailed && mountedRef.current) {
                setMutationError(t('checkoutFields.savedReadbackFailed', 'The field was saved, but the list could not be reloaded. Retry loading before making another change.'))
            }
            return { result, readbackFailed }
        } catch (err) {
            const message = getSafeApiErrorMessage(err, fallbackMessage)
            if (mountedRef.current) setMutationError(message)
            throw err
        } finally {
            mutatingRef.current = false
            if (mountedRef.current) setIsMutating(false)
        }
    }, [loadFields, t])

    const createField = useCallback((request) => executeMutation(
        () => checkoutFieldsService.createField(request),
        'Unable to create checkout field.',
    ), [executeMutation])

    const updateField = useCallback((fieldId, request) => executeMutation(
        () => checkoutFieldsService.updateField(fieldId, request),
        'Unable to update checkout field.',
    ), [executeMutation])

    const deleteField = useCallback((fieldId) => executeMutation(
        () => checkoutFieldsService.deleteField(fieldId),
        'Unable to delete checkout field.',
    ), [executeMutation])

    const reorderFields = useCallback(async (orderedFields) => {
        if (orderingRef.current || mutatingRef.current) return null

        const previousFields = fields
        const nextFields = sortFields(orderedFields.map((field, index) => ({
            ...field,
            displayOrder: (index + 1) * 10,
        })))

        setFields(nextFields)
        orderingRef.current = true
        loadSequenceRef.current += 1
        setIsOrdering(true)
        setMutationError(null)

        try {
            const response = await checkoutFieldsService.reorderFields(nextFields.map((field) => ({
                id: field.id,
                displayOrder: field.displayOrder,
            })))
            if (!Array.isArray(response?.fields)) throw new Error('Invalid checkout field order response. Reload to confirm the saved order.')
            const canonical = sortFields(response.fields)
            if (mountedRef.current) setFields(canonical)
            return canonical
        } catch (err) {
            if (mountedRef.current) {
                setFields(previousFields)
                setMutationError(getSafeApiErrorMessage(err, 'Unable to reorder checkout fields.'))
            }
            throw err
        } finally {
            orderingRef.current = false
            if (mountedRef.current) setIsOrdering(false)
        }
    }, [fields])

    return {
        fields,
        state,
        error,
        mutationError,
        isLoading: state === 'loading',
        isError: state === 'error',
        isForbidden: state === 'forbidden',
        isEmpty: state === 'success' && fields.length === 0,
        isMutating,
        isOrdering,
        refetch: loadFields,
        createField,
        updateField,
        deleteField,
        reorderFields,
        clearMutationError: () => setMutationError(null),
    }
}

export default useCheckoutFields
