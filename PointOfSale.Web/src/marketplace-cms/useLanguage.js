import { useEffect } from 'react'
import { useLocation } from 'react-router-dom'
import i18n from '../i18n/config'
import {
    applyMarketplaceDocumentLanguage,
    getCurrentLanguage,
    normalizeMarketplaceLanguage,
    setLanguage as persistMarketplaceLanguage,
} from './marketplaceLanguage'

export function useLanguage(explicitLanguage = null) {
    const location = useLocation()
    const currentLang = normalizeMarketplaceLanguage(
        explicitLanguage || getCurrentLanguage(location.pathname),
    )
    const isRTL = currentLang === 'ar'

    useEffect(() => {
        applyMarketplaceDocumentLanguage(currentLang)
        persistMarketplaceLanguage(currentLang)
        if (i18n.resolvedLanguage !== currentLang) {
            i18n.changeLanguage(currentLang).catch((error) => {
                console.error(`Failed to change language to ${currentLang}:`, error)
            })
        }
    }, [currentLang])

    return {
        currentLang,
        setLang: persistMarketplaceLanguage,
        isRTL,
    }
}

export default useLanguage
