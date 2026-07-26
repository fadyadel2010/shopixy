import { Outlet } from 'react-router-dom'
import { ThemeProvider } from '../theme/ThemeContext'
import { LanguageProvider } from '../i18n/LanguageContext'

/**
 * AffiliateProviders – layout wrapper for all public affiliate routes.
 * Renders ThemeProvider > LanguageProvider > <Outlet /> so child routes
 * automatically inherit both contexts without repeating the wrapper pair.
 */
export default function AffiliateProviders() {
    return (
        <ThemeProvider>
            <LanguageProvider>
                <Outlet />
            </LanguageProvider>
        </ThemeProvider>
    )
}
