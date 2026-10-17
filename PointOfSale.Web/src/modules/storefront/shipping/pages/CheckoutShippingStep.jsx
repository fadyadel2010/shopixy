/**
 * Checkout Shipping Step Component
 * Phase: Storefront Shipping Experience
 * 
 * Embeddable component for checkout page.
 * Displays shipping options for customer selection.
 * 
 * RULES:
 * ❌ No sorting logic in frontend
 * ❌ No "cheapest / fastest" logic
 * ✅ Display order exactly as returned by API
 * ✅ Disable checkout if no shipping option selected
 */

import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { Truck, AlertCircle, RefreshCw } from 'lucide-react'
import { useShippingQuotes } from '../hooks/useShippingApi'
import ShippingQuoteCard from '../components/ShippingQuoteCard'
import { resolveCountryCode } from '../utils/country'

export default function CheckoutShippingStep({
    destinationCity,
    destinationCountry,
    defaultCountryCode,
    destinationZipCode,
    orderSubtotal = 0,
    isCod = false,
    codAmount,
    onSelectShipping,
    selectedOption = null
}) {
    const { t } = useTranslation()

    const { quotes, loading, error, fetchQuotes } = useShippingQuotes()
    const [selectedQuote, setSelectedQuote] = useState(selectedOption)
    const resolvedDestinationCountry = resolveCountryCode(destinationCountry, defaultCountryCode)

    // Fetch quotes when destination changes
    useEffect(() => {
        if (destinationCity) {
            fetchQuotes({
                city: destinationCity,
                country: resolvedDestinationCountry,
                defaultCountryCode,
                zipCode: destinationZipCode,
                subtotal: orderSubtotal,
                isCod,
                codAmount
            })
        }
    }, [codAmount, defaultCountryCode, destinationCity, destinationZipCode, fetchQuotes, isCod, orderSubtotal, resolvedDestinationCountry])

    // Notify parent when selection changes
    useEffect(() => {
        if (onSelectShipping) {
            onSelectShipping(selectedQuote)
        }
    }, [selectedQuote, onSelectShipping])

    const handleSelectQuote = (quote) => {
        // Check COD compatibility
        if (isCod && !quote.supportsCod) {
            return // Can't select COD-incompatible option with COD payment
        }
        setSelectedQuote(quote)
    }

    // No destination yet
    if (!destinationCity) {
        return (
            <div className="p-6 bg-gray-50 rounded-xl text-center">
                <Truck size={32} className="mx-auto text-gray-400 mb-3" />
                <p className="text-gray-600">
                    {t('storefront.shipping.enterAddress', 'Enter your address to see shipping options')}
                </p>
            </div>
        )
    }

    // Loading
    if (loading) {
        return (
            <div className="p-6 bg-white rounded-xl">
                <div className="flex items-center gap-3 mb-4">
                    <RefreshCw size={20} className="text-blue-500 animate-spin" />
                    <span className="text-gray-600">
                        {t('storefront.shipping.loading', 'Loading shipping options...')}
                    </span>
                </div>
                <div className="space-y-3">
                    {[1, 2, 3].map(i => (
                        <div key={i} className="h-20 bg-gray-100 rounded-xl animate-pulse" />
                    ))}
                </div>
            </div>
        )
    }

    // Error
    if (error) {
        return (
            <div className="p-6 bg-red-50 rounded-xl">
                <div className="flex items-center gap-3 text-red-600 mb-4">
                    <AlertCircle size={20} />
                    <span className="font-medium">
                        {t('storefront.shipping.error', 'Failed to load shipping options')}
                    </span>
                </div>
                <p className="text-red-500 text-sm mb-4">{error}</p>
                <button
                    onClick={() => fetchQuotes({
                        city: destinationCity,
                        country: resolvedDestinationCountry,
                        defaultCountryCode,
                        zipCode: destinationZipCode,
                        subtotal: orderSubtotal,
                        isCod,
                        codAmount
                    })}
                    className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700"
                >
                    {t('common.retry', 'Try Again')}
                </button>
            </div>
        )
    }

    // No options available
    if (!quotes.length) {
        return (
            <div className="p-6 bg-yellow-50 rounded-xl">
                <div className="flex items-center gap-3 text-yellow-700 mb-2">
                    <AlertCircle size={20} />
                    <span className="font-medium">
                        {t('storefront.shipping.noOptions', 'No shipping options available')}
                    </span>
                </div>
                <p className="text-yellow-600 text-sm">
                    {t('storefront.shipping.noOptionsDesc', 'We cannot ship to this location at the moment. Please try a different address.')}
                </p>
            </div>
        )
    }

    return (
        <div className="space-y-3">
            <div className="flex items-center gap-2 mb-4">
                <Truck size={20} className="text-gray-500" />
                <h3 className="font-semibold text-gray-900">
                    {t('storefront.shipping.selectOption', 'Select Shipping Method')}
                </h3>
            </div>

            {/* COD Warning */}
            {isCod && (
                <div className="p-3 bg-amber-50 text-amber-700 rounded-lg text-sm mb-4">
                    💰 {t('storefront.shipping.codNote', 'You selected Cash on Delivery. Only COD-compatible carriers are available.')}
                </div>
            )}

            {/* Shipping Options - displayed in backend order */}
            <div className="space-y-3">
                {quotes.map((quote, index) => {
                    const isCodIncompatible = isCod && !quote.supportsCod

                    return (
                        <div key={`${quote.carrierCode}-${quote.serviceLevel}-${index}`}>
                            <ShippingQuoteCard
                                quote={quote}
                                selected={selectedQuote?.quoteId === quote.quoteId ||
                                    (selectedQuote?.carrierCode === quote.carrierCode &&
                                        selectedQuote?.serviceLevel === quote.serviceLevel)}
                                onSelect={handleSelectQuote}
                                disabled={isCodIncompatible}
                            />
                            {isCodIncompatible && (
                                <p className="text-xs text-red-500 mt-1 ml-2">
                                    {t('storefront.shipping.noCod', 'Does not support Cash on Delivery')}
                                </p>
                            )}
                        </div>
                    )
                })}
            </div>

            {/* Selected Summary */}
            {selectedQuote && (
                <div className="mt-4 p-4 bg-blue-50 rounded-xl border border-blue-200">
                    <div className="flex items-center justify-between">
                        <div>
                            <p className="text-sm text-blue-600">
                                {t('storefront.shipping.selected', 'Selected')}
                            </p>
                            <p className="font-semibold text-blue-900">
                                {selectedQuote.carrierName} - {selectedQuote.serviceName}
                            </p>
                        </div>
                        <p className="text-lg font-bold text-blue-900">
                            {selectedQuote.cost === 0
                                ? t('storefront.shipping.free', 'FREE')
                                : `${selectedQuote.cost.toLocaleString()} ${selectedQuote.currency}`
                            }
                        </p>
                    </div>
                </div>
            )}
        </div>
    )
}
