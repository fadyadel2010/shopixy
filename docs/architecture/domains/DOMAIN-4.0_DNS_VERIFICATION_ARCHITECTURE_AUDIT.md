# DOMAIN-4.0 — DNS Verification Architecture Audit

**Project:** Shopixy / PointOfSale  
**Platform:** Multi-tenant e-commerce / affiliate / POS  
**Date:** 2026-08-07  
**Status:** ARCHITECTURE AUDIT (NO IMPLEMENTATION)  
**Authority:** `.ai-context`  
**Precedent:** DOMAIN-0, DOMAIN-1, DOMAIN-1.5, DOMAIN-1.6, DOMAIN-2A, DOMAIN-2B, DOMAIN-2C, DOMAIN-2C.1, DOMAIN-2D, DOMAIN-3.*  
**Shopixy Rules:** PostgreSQL authoritative; Mongo/Redis/ES derivative. TenantId necessary-not-sufficient. Tenant isolation CRITICAL.

---

## Executive Summary

**VERDICT: NOT READY FOR DNS VERIFICATION**

The DOMAIN-2C implementation created the persistence foundation for custom domains (Domain, SslCertificate, DomainVerification, DomainAuditEntry entities), but **DNS verification is not operational**. The system can:
- Register domains in Draft/PendingVerification state
- Store verification tokens
- Track verification attempts via DomainVerification child entities
- Expose verification instructions via Admin API

**What is MISSING (blocking DNS verification):**
1. **No DNS client library** — no System.Net.Dns, no DnsClient package, no DNS lookup capability
2. **No verification execution service** — no service to poll DNS TXT/CNAME records
3. **No background job infrastructure for domains** — no Hangfire recurring job, no BackgroundService, no cron
4. **No verification retry/polling logic** — no "poll every 10s for 5 minutes" mechanism
5. **No verification state machine** — no service to transition PendingVerification → Verified
6. **No HTTP-file verification** — no endpoint to serve `/.well-known/` challenge files

The domain can be **created** and **marked** as PendingVerification, but it will **never transition to Verified** because no background worker polls DNS and no API endpoint allows manual verification triggers.

---

## Section 1: Current Domain Entity (Persistence Layer)

### 1.1 Domain Entity

**File:** `PointOfSale.Core/Entities/Domain.cs` (lines 1-258)

The Domain entity contains all verification-related fields per DOMAIN-1.5 Section 4.1:

| Field | Type | Current Value Source | Purpose |
|-------|------|---------------------|---------|
| `VerificationToken` | `string` (max 256) | Generated server-side at domain creation | Cryptographic proof for DNS TXT/CNAME/HTTP verification |
| `VerificationMethod` | `VerificationMethod?` enum (nullable) | Set at domain creation or update | DNS TXT, DNS CNAME, or HTTP file method |
| `VerifiedAtUtc` | `DateTime?` | **NULL until verification succeeds** | Timestamp when ownership verification passed |
| `LifecycleState` | `DomainLifecycleState` enum | `Draft` → `PendingVerification` → `Verified` | Current verification status |

**Source Evidence:**  
- `Domain.cs` lines 81-88: `VerificationToken`, `VerificationMethod` properties
- `Domain.cs` lines 117-120: `VerifiedAtUtc` property (nullable, null until verified)
- `Domain.cs` lines 197-205: `MarkVerified()` lifecycle method (sets `VerifiedAtUtc`, transitions to `Verified` or `Primary`)

**Verification Token Generation:**  
`DomainWriteService.cs` (lines 96-236) generates a cryptographically random token when creating a domain:
```csharp
var verificationToken = GenerateVerificationToken(); // line 150
```

The token is stored in `Domain.VerificationToken` but **no external service reads or validates it yet**.

### 1.2 DomainVerification Entity (Attempt History)

**File:** `PointOfSale.Core/Entities/DomainVerification.cs` (lines 1-86)

