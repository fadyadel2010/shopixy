/**
 * Validate E.164 phone number format
 * E.164 format: +[country code][number]
 * Example: +1234567890
 */
export function validateE164Phone(phone: string): boolean {
  // E.164 format: starts with +, followed by 1-15 digits
  const e164Regex = /^\+[1-9]\d{1,14}$/;
  return e164Regex.test(phone);
}

/**
 * Format phone number for WhatsApp (remove + and add @s.whatsapp.net)
 */
export function formatPhoneForWhatsApp(phone: string): string {
  // Remove + prefix and add WhatsApp suffix
  const cleanPhone = phone.replace(/^\+/, '');
  return `${cleanPhone}@s.whatsapp.net`;
}

/**
 * Validate and format phone number
 */
export function validateAndFormatPhone(phone: string): { valid: boolean; formatted?: string; error?: string } {
  if (!phone) {
    return { valid: false, error: 'Phone number is required' };
  }

  if (!validateE164Phone(phone)) {
    return { 
      valid: false, 
      error: 'Invalid phone number format. Expected E.164 format (e.g., +1234567890)' 
    };
  }

  return {
    valid: true,
    formatted: formatPhoneForWhatsApp(phone)
  };
}
