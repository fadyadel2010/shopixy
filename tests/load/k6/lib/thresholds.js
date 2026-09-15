// M1.1 non-binding smoke thresholds. NOT production SLOs.
// Capacity thresholds are explicitly out of scope for this phase.
export const SMOKE_THRESHOLDS = {
  // Zero failed k6 checks across the read-only smoke.
  checks: ['rate==1.0'],
  // No HTTP 5xx on health/read requests.
  http_req_failed: ['rate==0'],
  // Isolation violations must be zero (custom Counter must stay at 0).
  'checks{check:i1_no_foreign_products}': ['rate==1.0'],
  'checks{check:i2_store_resolution}': ['rate==1.0'],
};
