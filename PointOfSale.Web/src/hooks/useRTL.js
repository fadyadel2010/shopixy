import { useTranslation } from 'react-i18next'
import { getDocumentDirection, normalizeDocumentLanguage } from '../i18n/documentLanguage'
import { getLocaleForLanguage } from '../utils/dateFormat'

export function useRTL() {
    const { i18n } = useTranslation()
    const language = normalizeDocumentLanguage(i18n.resolvedLanguage || i18n.language)

    return {
        language,
        isRTL: language === 'ar',
        dir: getDocumentDirection(language),
        locale: getLocaleForLanguage(language),
    }
}

export default useRTL
