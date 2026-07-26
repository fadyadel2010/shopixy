/**
 * Storefront Shipping API Hooks
 * Phase: Storefront Shipping Experience
 * 
 * PURE CONSUMER - NO BUSINESS LOGIC
 * All shipping logic comes from backend.
 * 
 * APIs:
 * - POST /api/storefront/shipping/quotes - Get shipping quotes
 * - GET /api/storefront/orders/{orderId}/shipment - Get authenticated customer shipment details
 * - GET /api/storefront/orders/track-guest - Track guest order
 */

import { useState, useEffect, useCallback } from 'react'
import api from '../../../../services/api'
import { resolveCountryCode } from '../utils/country'
import { requireBackendShipmentIdentity, requireBackendTrackedOrder } from '../shipmentIdentityAuthority'

const SHIPPING_CURRENCY_REGEX = /^[A-Z]{3}$/

function requireShippingQuoteString(value, field) {
    if (typeof value !== 'string') {
        throw new Error(`Shipping quote response missing ${field}.`)
    }

    const normalized = value.trim()
    if (!normalized) {
        throw new Error(`Shipping quote response missing ${field}.`)
    }

    return normalized
}

function requireShippingQuoteAmount(value, field) {
    if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) {
        throw new Error(`Shipping quote response contained invalid ${field}.`)
    }

    return value
}

function requireShippingQuoteDays(value, field) {
    if (typeof value !== 'number' || !Number.isInteger(value) || value < 0 || value > 365) {
        throw new Error(`Shipping quote response contained invalid ${field}.`)
    }

    return value
}

function optionalShippingQuoteString(value) {
    if (value === undefined || value === null) {
        return null
    }

    if (typeof value !== 'string') {
        throw new Error('Shipping quote response contained malformed quote identity evidence.')
    }

    const normalized = value.trim()
    return normalized || null
}

function requireBackendShippingQuoteResponse(payload) {
    if (!payload || payload.success !== true || !Array.isArray(payload.options)) {
        throw new Error('Shipping quote response was malformed.')
    }

    const seen = new Set()
    return payload.options.map((quote) => {
        if (!quote || typeof quote !== 'object') {
            throw new Error('Shipping quote response contained malformed option evidence.')
        }

        const carrierCode = requireShippingQuoteString(quote.carrierCode, 'carrierCode')
        const carrierName = requireShippingQuoteString(quote.carrierName, 'carrierName')
        const serviceLevel = requireShippingQuoteString(quote.serviceLevel, 'serviceLevel')
        const serviceName = requireShippingQuoteString(quote.serviceName, 'serviceName')
        const currency = requireShippingQuoteString(quote.currency, 'currency').toUpperCase()
        if (!SHIPPING_CURRENCY_REGEX.test(currency)) {
            throw new Error('Shipping quote response contained invalid currency evidence.')
        }

        const cost = requireShippingQuoteAmount(quote.cost, 'cost')
        const originalCost = requireShippingQuoteAmount(quote.originalCost, 'originalCost')
        const estimatedDelivery = requireShippingQuoteString(quote.estimatedDelivery, 'estimatedDelivery')
        const estimatedDeliveryDaysMin = requireShippingQuoteDays(quote.estimatedDeliveryDaysMin, 'estimatedDeliveryDaysMin')
        const estimatedDeliveryDaysMax = requireShippingQuoteDays(quote.estimatedDeliveryDaysMax, 'estimatedDeliveryDaysMax')
        if (estimatedDeliveryDaysMax < estimatedDeliveryDaysMin) {
            throw new Error('Shipping quote response contained contradictory delivery window evidence.')
        }

        const quoteId = optionalShippingQuoteString(quote.quoteId)
        const identity = quoteId || `${carrierCode}:${serviceLevel}`
        if (seen.has(identity)) {
            throw new Error('Shipping quote response contained duplicate quote evidence.')
        }
        seen.add(identity)

        return {
            ...quote,
            carrierCode,
            carrierName,
            serviceLevel,
            serviceName,
            cost,
            originalCost,
            currency,
            estimatedDelivery,
            estimatedDeliveryDaysMin,
            estimatedDeliveryDaysMax,
            supportsCod: quote.supportsCod === true,
            isFreeShipping: quote.isFreeShipping === true,
            quoteId,
        }
    })
}

/**
 * Hook for fetching shipping quotes during checkout
 */
export function useShippingQuotes() {
    const [quotes, setQuotes] = useState([])
    const [loading, setLoading] = useState(false)
    const [error, setError] = useState(null)

    const fetchQuotes = useCallback(async (request) => {
        setLoading(true)
        setError(null)

        try {
            const destinationCountry = resolveCountryCode(request.country, request.defaultCountryCode)
            const response = await api.post('/storefront/shipping/quotes', {
                destinationCity: request.city,
                destinationCountry,
                destinationZipCode: request.zipCode,
                weightKg: request.weight || 1.0,
                packageCount: request.packageCount || 1,
                isCashOnDelivery: request.isCod || false,
                codAmount: request.codAmount,
                orderSubtotal: request.subtotal || 0
            }, {
                headers: {
                    'Cache-Control': 'no-store',
                    Pragma: 'no-cache',
                },
            })

            if (response.data.success) {
                const acceptedQuotes = requireBackendShippingQuoteResponse(response.data)
                // Display order exactly as returned by API - NO sorting logic
                setQuotes(acceptedQuotes)
                return acceptedQuotes
            } else {
                setError(response.data.errorMessage || 'Failed to get quotes')
                return []
            }
        } catch (err) {
            console.error('Failed to fetch shipping quotes:', err)
            const errorMessage = err.response?.data?.error || 'Failed to load shipping options'
            setError(errorMessage)
            return []
        } finally {
            setLoading(false)
        }
    }, [])

    return {
        quotes,
        loading,
        error,
        fetchQuotes,
        clearError: () => setError(null)
    }
}

/**
 * Hook for fetching shipment tracking by order ID
 */
export function useShipmentTracking(orderId) {
    const [shipment, setShipment] = useState(null)
    const [loading, setLoading] = useState(!!orderId) // Only start loading if we have an ID
    const [error, setError] = useState(null)

    const fetchShipment = useCallback(async () => {
        // Guard: Don't fetch if no valid orderId
        if (!orderId || orderId === 'undefined') {
            setLoading(false)
            return
        }

        setLoading(true)
        setError(null)

        try {
            const response = await api.get(`/storefront/orders/${orderId}/shipment`)
            setShipment(requireBackendShipmentIdentity(response.data, orderId))
        } catch (err) {
            console.error('Failed to fetch shipment:', err)
            if (err.response?.status === 404) {
                // No shipment yet - this is OK
                setShipment(null)
            } else {
                setError(err.response?.data?.message || 'Failed to load tracking')
            }
        } finally {
            setLoading(false)
        }
    }, [orderId])

    useEffect(() => {
        if (orderId && orderId !== 'undefined') {
            fetchShipment()
        } else {
            setLoading(false)
        }
    }, [orderId, fetchShipment])

    return {
        shipment,
        loading,
        error,
        refetch: fetchShipment
    }
}

/**
 * Hook for guest order tracking
 */
export function useGuestTracking() {
    const [order, setOrder] = useState(null)
    const [loading, setLoading] = useState(false)
    const [error, setError] = useState(null)

    const trackOrder = async (email, orderNumber) => {
        setLoading(true)
        setError(null)
        setOrder(null)

        try {
            const response = await api.get('/storefront/orders/track-guest', {
                params: { email, orderNumber }
            })
            const trackedOrder = requireBackendTrackedOrder(response.data)
            setOrder(trackedOrder)
            return trackedOrder
        } catch (err) {
            console.error('Failed to track order:', err)
            if (err.response?.status === 404) {
                setError('Order not found. Please check your email and order number.')
            } else {
                setError(err.response?.data?.message || 'Failed to track order')
            }
            return null
        } finally {
            setLoading(false)
        }
    }

    return {
        order,
        loading,
        error,
        trackOrder,
        clearError: () => setError(null)
    }
}

/**
 * Shipment status configuration for display
 * Colors and icons - NO lifecycle logic
 */
export const SHIPMENT_STATUS_DISPLAY = {
    Pending: {
        color: 'gray',
        bgColor: 'bg-gray-100',
        textColor: 'text-gray-700',
        icon: '⏳',
        order: 0
    },
    ReadyForPickup: {
        color: 'blue',
        bgColor: 'bg-blue-100',
        textColor: 'text-blue-700',
        icon: '📦',
        order: 1
    },
    PickedUp: {
        color: 'indigo',
        bgColor: 'bg-indigo-100',
        textColor: 'text-indigo-700',
        icon: '🚛',
        order: 2
    },
    InTransit: {
        color: 'sky',
        bgColor: 'bg-sky-100',
        textColor: 'text-sky-700',
        icon: '🚚',
        order: 3
    },
    OutForDelivery: {
        color: 'amber',
        bgColor: 'bg-amber-100',
        textColor: 'text-amber-700',
        icon: '🏃',
        order: 4
    },
    Delivered: {
        color: 'green',
        bgColor: 'bg-green-100',
        textColor: 'text-green-700',
        icon: '✅',
        order: 5,
        isTerminal: true
    },
    Failed: {
        color: 'red',
        bgColor: 'bg-red-100',
        textColor: 'text-red-700',
        icon: '❌',
        order: 100,
        isTerminal: true
    },
    Cancelled: {
        color: 'rose',
        bgColor: 'bg-rose-100',
        textColor: 'text-rose-700',
        icon: '🚫',
        order: 100,
        isTerminal: true
    },
    Returned: {
        color: 'orange',
        bgColor: 'bg-orange-100',
        textColor: 'text-orange-700',
        icon: '↩️',
        order: 100,
        isTerminal: true
    }
}

export function getStatusDisplay(status) {
    return SHIPMENT_STATUS_DISPLAY[status] || SHIPMENT_STATUS_DISPLAY.Pending
}
