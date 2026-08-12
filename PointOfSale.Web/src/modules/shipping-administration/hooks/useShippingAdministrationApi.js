import { useCallback, useEffect, useState } from 'react'
import {
    fetchCarrierAccounts,
    fetchShippingAdministrationAudit,
    fetchSupportedCarriers,
    fetchWarehouses,
} from '../shippingAdministrationApi'

export function useShippingAdministrationApi() {
    const [supportedCarriers, setSupportedCarriers] = useState([])
    const [carrierAccounts, setCarrierAccounts] = useState([])
    const [warehouses, setWarehouses] = useState([])
    const [audit, setAudit] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState('')

    const refresh = useCallback(async () => {
        setLoading(true)
        setError('')

        try {
            const [supportedCarrierData, carrierAccountData, warehouseData, auditData] = await Promise.all([
                fetchSupportedCarriers(),
                fetchCarrierAccounts(),
                fetchWarehouses(),
                fetchShippingAdministrationAudit(),
            ])

            setSupportedCarriers(supportedCarrierData)
            setCarrierAccounts(carrierAccountData)
            setWarehouses(warehouseData)
            setAudit(auditData)
        } catch (err) {
            setError(err.message || 'Failed to load shipping administration.')
        } finally {
            setLoading(false)
        }
    }, [])

    useEffect(() => {
        refresh()
    }, [refresh])

    return {
        supportedCarriers,
        carrierAccounts,
        warehouses,
        audit,
        loading,
        error,
        refresh,
    }
}
