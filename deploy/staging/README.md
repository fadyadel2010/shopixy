# Staging Topology

This staging layout mirrors production while keeping each dependency isolated from live traffic.

## Domains

- `api-staging.yourdomain.com` -> ASP.NET Core API
- `app-staging.yourdomain.com` -> POS/admin SPA
- `*.app-staging.yourdomain.com` -> tenant storefront SPA

The wildcard storefront host is required because tenant resolution in the API is host-based and expects `Storefront:UrlTemplate` to contain `{slug}` in the hostname.

## Isolated Dependencies

- PostgreSQL: `PosEcommerceDb_Staging`
- Redis: dedicated `pos-staging` instance or database
- MongoDB: dedicated `ecommerce_read_db_staging`
- RabbitMQ: dedicated staging virtual host or cluster
- Elasticsearch: staging indexes (`products_staging`, `commercehub_products_staging`)
- R2: dedicated staging bucket (`pointofsale-staging`)

## Secret Injection

Do not store staging credentials in `appsettings.*.json`.

- Inject runtime secrets from environment variables, Kubernetes secrets, Azure Key Vault, AWS Secrets Manager, or an equivalent secret store.
- Use [pointofsale.api.env.example](./pointofsale.api.env.example) as the canonical variable list for API deployment.
- Keep `appsettings.Staging.json` limited to non-secret defaults only.
- Rotate all leaked credentials before re-deploying staging or production.

## HTTPS / Reverse Proxy

Use [Caddyfile](./Caddyfile) on the staging host with:

- `api-staging.yourdomain.com` reverse-proxied to the API
- `app-staging.yourdomain.com` serving `PointOfSale.Web/dist`
- `*.app-staging.yourdomain.com` serving `PointOfSale.Storefront/dist`
- `/api`, `/hubs`, and `/health` forwarded with the original `Host` header preserved

## DNS

Create these records before validation:

- `api-staging.yourdomain.com`
- `app-staging.yourdomain.com`
- `*.app-staging.yourdomain.com`

All should resolve to the staging reverse proxy so the backend can securely resolve tenant context from the request host.

## Validation

Run [scripts/staging/Invoke-StagingValidation.ps1](../scripts/staging/Invoke-StagingValidation.ps1) against the staging-shaped environment after:

1. `dotnet build PointOfSale.sln -nologo -v minimal`
2. `npm.cmd run build` in `PointOfSale.Web`
3. `npm.cmd run build` in `PointOfSale.Storefront`

The validation script exercises smoke flows, dependency failure simulation, observability checks, load sanity, and a deployment dry run.
