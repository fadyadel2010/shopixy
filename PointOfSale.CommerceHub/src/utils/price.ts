/**
 * Sanitizes a price snapshot value to ensure it's a valid, non-negative number
 * rounded to two decimal places.
 * 
 * This utility prevents invalid price values (NaN, Infinity, negative numbers)
 * from propagating through cart calculations and checkout flows.
 * 
 * @param value - Raw price value that may be invalid
 * @returns Sanitized price: finite, non-negative, rounded to 2 decimals
 * 
 * @example
 * sanitizePriceSnapshot(10.999) // 11.00
 * sanitizePriceSnapshot(-5) // 0
 * sanitizePriceSnapshot(NaN) // 0
 * sanitizePriceSnapshot(Infinity) // 0
 */
export const sanitizePriceSnapshot = (value: number | undefined | null): number => {
  // Handle null/undefined
  if (value == null) {
    return 0;
  }

  // Parse and validate
  const parsed = Number(value);
  
  // Check if finite (excludes NaN, Infinity, -Infinity)
  if (!Number.isFinite(parsed)) {
    return 0;
  }

  // Ensure non-negative and round to 2 decimals
  return Math.max(0, Number(parsed.toFixed(2)));
};

/**
 * Legacy price normalization for fingerprint generation.
 * 
 * Preserves the exact behavior of the original Number(price.toFixed(2)) pattern
 * to maintain idempotency fingerprint compatibility with in-flight checkouts.
 * 
 * Updated to safely handle non-finite values (NaN, Infinity, null, undefined)
 * by validating before calling toFixed, preventing crashes in idempotency key generation.
 * 
 * @param value - Price value (may be invalid or non-finite)
 * @returns Price rounded to 2 decimals, or null for invalid inputs
 * 
 * @example
 * sanitizePriceSnapshotForFingerprint(10.999) // 11.00
 * sanitizePriceSnapshotForFingerprint(NaN) // null (safe fallback)
 * sanitizePriceSnapshotForFingerprint(Infinity) // null (safe fallback)
 * sanitizePriceSnapshotForFingerprint(undefined) // null (safe fallback)
 */
export const sanitizePriceSnapshotForFingerprint = (value: number | undefined | null): number | null => {
  // Handle null/undefined
  if (value == null) {
    return null;
  }

  // Coerce to number
  const parsed = Number(value);
  
  // Only apply legacy rounding for finite numbers
  if (Number.isFinite(parsed)) {
    return Number(parsed.toFixed(2));
  }

  // Safe fallback for non-finite values (NaN, Infinity, -Infinity)
  return null;
};
