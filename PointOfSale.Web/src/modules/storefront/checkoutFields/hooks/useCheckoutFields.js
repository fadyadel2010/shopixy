import { useCallback, useEffect, useRef, useState } from 'react'

import { getSafeApiErrorMessage, isRequestCanceled } from '../../../../services/api'
import checkoutFieldsService from '../services/checkoutFieldsService'

const sortFields = (fields = []) => [...fields].sort((a, b) => {
    const left = Number(a.displayOrder ?? 0)
    const right = Number(b.displayOrder ?? 0)
    return left === right ? String(a.label || '').localeCompare(String(b.label || '')) : left - right
})

export function hasSystemFields(fields = []) {
    return fields.some((field) => String(field.fieldKind || '').toUpperCase() === 'SYSTEM')
}

export function useCheckoutFields() {
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
            const nextFields = sortFields(response?.fields || [])

            if (allowInitialize && !initializedRef.current && !hasSystemFields(nextFields)) {
                initializedRef.current = true
                const initialized = await checkoutFieldsService.initializeSystemFields()
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
            if (isRequestCanceled(err) || !mountedRef.current || sequence !== loadSequenceRef.current) return []
            setError(getSafeApiErrorMessage(err, 'Unable to load checkout fields.'))
            setState(err?.response?.status === 403 ? 'forbidden' : 'error')
            return []
        }
    }, [])

    useEffect(() => {
        loadFields()
    }, [loadFields])

    const executeMutation = useCallback(async (action, fallbackMessage) => {
        setIsMutating(true)
        setMutationError(null)
        try {
            const result = await action()
            await loadFields({ showLoading: false, allowInitialize: false })
            return result
        } catch (err) {
            const message = getSafeApiErrorMessage(err, fallbackMessage)
            setMutationError(message)
            throw err
        } finally {
            setIsMutating(false)
        }
    }, [loadFields])

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
        if (orderingRef.current) return null

        const previousFields = fields
        const nextFields = sortFields(orderedFields.map((field, index) => ({
            ...field,
            displayOrder: (index + 1) * 10,
        })))

        setFields(nextFields)
        orderingRef.current = true
        setIsOrdering(true)
        setMutationError(null)

        try {
            const response = await checkoutFieldsService.reorderFields(nextFields.map((field) => ({
                id: field.id,
                displayOrder: field.displayOrder,
            })))
            const canonical = sortFields(response?.fields || nextFields)
            setFields(canonical)
            return canonical
        } catch (err) {
            setFields(previousFields)
            setMutationError(getSafeApiErrorMessage(err, 'Unable to reorder checkout fields.'))
            throw err
        } finally {
            orderingRef.current = false
            setIsOrdering(false)
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
