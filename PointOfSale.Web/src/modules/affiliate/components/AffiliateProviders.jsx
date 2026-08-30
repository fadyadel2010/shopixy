import { useEffect } from 'react'
import { Outlet, useNavigate } from 'react-router-dom'
import { setAffiliateLoginNavigator } from '../../../services/affiliateAuthApi'
import { ThemeProvider } from '../theme/ThemeContext'
import { LanguageProvider } from '../i18n/LanguageContext'

/**
 * AffiliateProviders – layout wrapper for all public affiliate routes.
 * Renders ThemeProvider > LanguageProvider > <Outlet /> so child routes
 * automatically inherit both contexts without repeating the wrapper pair.
 */
export default function AffiliateProviders() {
    const navigate = useNavigate()

    useEffect(() => {
        setAffiliateLoginNavigator((path) => navigate(path, { replace: true }))

        return () => {
            setAffiliateLoginNavigator((path) => {
                window.location.href = path
            })
        }
    }, [navigate])

    return (
        <ThemeProvider>
            <LanguageProvider>
                <Outlet />
            </LanguageProvider>
        </ThemeProvider>
    )
}
