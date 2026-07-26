# Shipping Webhook PII Redaction - Security Hardening

## Overview

This document describes the security improvements made to the `ShippingWebhookLog` entity to prevent indefinite storage of PII (Personally Identifiable Information) in webhook audit logs.

## Problem Statement

The original implementation stored:
- **Full webhook payloads** containing recipient addresses, phone numbers, names, and location data
- **Complete IP addresses** which are considered PII under GDPR
- **Full user agent strings** which can contain identifying information

This data was retained indefinitely without encryption or retention policies, creating compliance and security risks.

## Solution

### 1. Entity Changes

**Before:**
```csharp
public string RawPayload { get; set; }
public string? SourceIp { get; set; }
public string? UserAgent { get; set; }
```

**After:**
```csharp
public string PayloadSummary { get; set; }      // Redacted, truncated to 500 chars
public string? SourceIpPrefix { get; set; }     // Network prefix only (e.g., "203.0.113.0/24")
public string? UserAgentSummary { get; set; }   // Truncated to 100 chars, versions removed
```

### 2. Redaction Logic

Created `WebhookDataRedactor` class with three methods:

#### `RedactPayload(string rawPayload)`
- Parses JSON and redacts PII fields (address, phone, email, name, etc.)
- Falls back to regex-based redaction for non-JSON payloads
- Truncates to 500 characters maximum
- Example output: `{"event":"delivered","tracking":"ABC123","recipient":"[REDACTED]",...[truncated]}`

#### `RedactIpAddress(string? ipAddress)`
- IPv4: Converts `203.0.113.45` → `203.0.113.0/24`
- IPv6: Keeps first 4 groups, redacts rest
- Preserves network-level information for abuse detection while removing host-level PII

#### `TruncateUserAgent(string? userAgent)`
- Removes version numbers: `Mozilla/5.0 (Windows NT 10.0)` → `Mozilla/x.x (Windows NT x.x)`
- Truncates to 100 characters
- Preserves browser/OS family for debugging without detailed fingerprinting

### 3. Database Migration

**Migration:** `20260421000000_RedactShippingWebhookPII.cs`

Renames columns:
- `RawPayload` → `PayloadSummary`
- `SourceIp` → `SourceIpPrefix`
- `UserAgent` → `UserAgentSummary`

**Note:** Existing data is preserved but still contains unredacted PII. See cleanup script below.

### 4. Data Cleanup Script

**Script:** `PointOfSale.Infrastructure/Scripts/RedactExistingWebhookData.sql`

Provides three options:
1. **Delete old logs** (recommended): Remove logs older than 90 days
2. **Redact in place**: Apply redaction logic to existing records
3. **Clear PII fields**: Remove all PII from logs older than 30 days

## Recommended Retention Policy

### Short-term (0-30 days)
- Keep redacted payload summaries for debugging
- Retain network prefix for abuse detection
- Store truncated user agents for compatibility tracking

### Medium-term (30-90 days)
- Keep only metadata: EventId, PayloadHash, ProcessingStatus
- Clear PayloadSummary, SourceIpPrefix, UserAgentSummary
- Retain for compliance and audit purposes

### Long-term (90+ days)
- Delete webhook logs entirely
- Shipment and order records provide sufficient audit trail
- Reduces storage costs and compliance risk

## Implementation Checklist

- [x] Update `ShippingWebhookLog` entity with redacted fields
- [x] Create `WebhookDataRedactor` utility class
- [x] Update `CarrierShipmentService` to use redaction methods
- [x] Create database migration
- [x] Create data cleanup script
- [ ] **Run migration in production**
- [ ] **Execute cleanup script to redact existing data**
- [ ] **Set up automated job to delete logs older than 90 days**
- [ ] **Update monitoring/alerting to use new field names**
- [ ] **Review and update any admin dashboards that display webhook logs**

## Testing

### Unit Tests Required
1. Test `WebhookDataRedactor.RedactPayload()` with various JSON structures
2. Test IP address redaction for IPv4 and IPv6
3. Test user agent truncation
4. Verify no PII leaks in redacted output

### Integration Tests Required
1. Verify webhook processing still works with redacted storage
2. Confirm duplicate detection still functions (uses PayloadHash, not payload content)
3. Test failure recovery scenarios

## Security Benefits

1. **GDPR Compliance**: Minimizes PII storage duration
2. **Data Breach Mitigation**: Leaked webhook logs contain minimal sensitive data
3. **Storage Optimization**: Smaller payloads reduce database size
4. **Audit Trail Preservation**: Still maintains sufficient data for debugging and compliance

## Breaking Changes

### For Developers
- `RawPayload` property renamed to `PayloadSummary`
- `SourceIp` property renamed to `SourceIpPrefix`
- `UserAgent` property renamed to `UserAgentSummary`
- Content of these fields is now redacted/truncated

### For Operations
- Any scripts or queries referencing old column names must be updated
- Admin dashboards displaying webhook logs need field name updates
- Monitoring alerts checking payload content may need adjustment

## Future Enhancements

1. **Blob Storage Option**: For cases requiring full payload retention (e.g., legal disputes), implement encrypted blob storage with explicit retention policy
2. **Automated Cleanup Job**: Background service to delete old webhook logs
3. **Configurable Retention**: Allow per-tenant retention policy configuration
4. **Encryption at Rest**: Encrypt PayloadSummary even though it's redacted

## References

- GDPR Article 5(1)(e): Storage limitation principle
- GDPR Article 32: Security of processing
- ISO 27001: Information security management
- OWASP Top 10: A01:2021 – Broken Access Control

## Questions?

Contact the security team or review the implementation in:
- `PointOfSale.Core/Entities/ShippingWebhookLog.cs`
- `PointOfSale.Core/Security/WebhookDataRedactor.cs`
- `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs`
