import { useCallback, useEffect, useRef, useState } from 'react'
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
    const requestIdRef = useRef(0)

    const refresh = useCallback(async () => {
        const requestId = ++requestIdRef.current
        setLoading(true)
        setError('')

        try {
            const [supportedCarrierData, carrierAccountData, warehouseData, auditData] = await Promise.all([
                fetchSupportedCarriers(),
                fetchCarrierAccounts(),
                fetchWarehouses(),
                fetchShippingAdministrationAudit(),
            ])

            if (requestId !== requestIdRef.current) return
            setSupportedCarriers(supportedCarrierData)
            setCarrierAccounts(carrierAccountData)
            setWarehouses(warehouseData)
            setAudit(auditData)
        } catch (err) {
            if (requestId !== requestIdRef.current) return
            // A failed refresh must not leave a stale positive readiness snapshot
            // available to the management forms.
            setSupportedCarriers([])
            setCarrierAccounts([])
            setWarehouses([])
            setAudit([])
            setError(err.message || 'Failed to load shipping administration.')
        } finally {
            if (requestId === requestIdRef.current) setLoading(false)
        }
    }, [])

    useEffect(() => {
        refresh()
        return () => { requestIdRef.current += 1 }
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
