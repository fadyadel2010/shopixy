export const WHATSAPP_ACCOUNT_STATUS_CONFIG = {
    Connected: {
        labelKey: 'whatsapp.accounts.status.connected',
        color: 'bg-green-100 text-green-800'
    },
    PendingQR: {
        labelKey: 'whatsapp.accounts.status.waitingForQr',
        color: 'bg-yellow-100 text-yellow-800'
    },
    Disconnected: {
        labelKey: 'whatsapp.accounts.status.disconnected',
        color: 'bg-red-100 text-red-800'
    },
    CONNECTED: {
        labelKey: 'whatsapp.accounts.status.connected',
        color: 'bg-green-100 text-green-800'
    },
    DISCONNECTED: {
        labelKey: 'whatsapp.accounts.status.disconnected',
        color: 'bg-red-100 text-red-800'
    },
    CONNECTING: {
        labelKey: 'whatsapp.accounts.status.connecting',
        color: 'bg-blue-100 text-blue-800',
        isLoading: true
    },
    QR_PENDING: {
        labelKey: 'whatsapp.accounts.status.waitingForQr',
        color: 'bg-yellow-100 text-yellow-800'
    },
    POISONED: {
        labelKey: 'whatsapp.accounts.status.sessionExpired',
        color: 'bg-orange-100 text-orange-800',
        descriptionKey: 'whatsapp.accounts.statusDescriptions.sessionExpired'
    },
    LOGGED_OUT: {
        labelKey: 'whatsapp.accounts.status.loggedOut',
        color: 'bg-red-100 text-red-800',
        descriptionKey: 'whatsapp.accounts.statusDescriptions.loggedOut'
    }
}

const WHATSAPP_MESSAGE_STATUS_KEYS = {
    Sent: 'whatsapp.logs.statuses.sent',
    Failed: 'whatsapp.logs.statuses.failed',
    Delivered: 'whatsapp.logs.statuses.delivered'
}

const WHATSAPP_TEMPLATE_TYPE_KEYS = {
    OrderConfirmation: 'whatsapp.types.orderConfirmation',
    Otp: 'whatsapp.types.otp'
}

const WHATSAPP_LANGUAGE_KEYS = {
    en: 'whatsapp.languages.english',
    ar: 'whatsapp.languages.arabic'
}

export function getWhatsAppMessageStatusKey(status) {
    return WHATSAPP_MESSAGE_STATUS_KEYS[status] || null
}

export function getWhatsAppTemplateTypeKey(type) {
    return WHATSAPP_TEMPLATE_TYPE_KEYS[type] || null
}

export function getWhatsAppLanguageKey(language) {
    return WHATSAPP_LANGUAGE_KEYS[language] || null
}
