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

/** Preserve incomplete input, but send valid national numbers with their displayed country. */
export function normalizePhoneForSubmission(phone: string, countryCode?: string | null): string {
    const country = asSupportedCountryCode(countryCode);
    const parsed = parsePhoneNumberFromString(phone.trim(), country ?? undefined);
    return parsed?.isValid() ? parsed.number : phone;
}
