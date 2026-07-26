# Auth Infrastructure Rate Limiting Requirement

Auth endpoints must be protected by infrastructure-level rate limiting.

Scope:
- `POST /api/auth/login`
- `POST /api/auth/refresh`
- Equivalent public auth routes exposed by the platform, including storefront and super-admin auth surfaces

Repository verification:
- Application-level ASP.NET rate-limit policies already exist in code for:
  - Public login and refresh paths (`PublicLoginRateLimit`, `PublicRefreshRateLimit`)
  - Super-admin auth route (`SuperAdminSecurityConstants.RateLimitPolicyName` registered in PointOfSale.API/Program.cs)
- Infrastructure enforcement is not represented in this repository, so nginx, API gateway, load balancer, or Cloudflare configuration must be verified separately during deployment review.

Production requirement:
- If infrastructure-level rate limiting is not already configured, enforce at least:
- Login: max `5` attempts per minute per client IP
- Refresh: max `10` attempts per minute per client IP

Purpose:
- Reduce brute-force exposure before requests reach the application
- Preserve capacity during credential-stuffing or token-abuse bursts
- Keep protection aligned across multi-instance deployments and upstream caches
