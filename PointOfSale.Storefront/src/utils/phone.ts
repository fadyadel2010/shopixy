import {
    getCountryCallingCode,
    parsePhoneNumberFromString,
    type CountryCode,
} from 'libphonenumber-js/min';
import { normalizeCountryCode } from './country';

function asSupportedCountryCode(countryCode?: string | null): CountryCode | null {
    const normalizedCountryCode = normalizeCountryCode(countryCode);
    if (!normalizedCountryCode) return null;

    try {
        getCountryCallingCode(normalizedCountryCode as CountryCode);
        return normalizedCountryCode as CountryCode;
    } catch {
        return null;
    }
}

export function getCallingCodeForCountry(countryCode?: string | null): string | null {
    const supportedCountryCode = asSupportedCountryCode(countryCode);
    return supportedCountryCode ? `+${getCountryCallingCode(supportedCountryCode)}` : null;
}

export function formatPhoneForLocalInput(phone: string, countryCode?: string | null): string {
    if (!phone.trim().startsWith('+')) return phone;

    const supportedCountryCode = asSupportedCountryCode(countryCode);
    if (!supportedCountryCode) return phone;

    const parsedPhone = parsePhoneNumberFromString(phone);
    return parsedPhone?.country === supportedCountryCode
        ? parsedPhone.formatNational()
        : phone;
}
