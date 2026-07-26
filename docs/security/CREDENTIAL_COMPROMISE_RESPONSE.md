# Credential Compromise Response

## Scope

This repository previously contained committed secrets for:

- PostgreSQL
- JWT signing keys
- SMTP
- RabbitMQ
- MongoDB
- Baileys service authentication
- Commerce AI provider access
- R2 object storage
- Abandoned cart token signing

Treat every previously committed value as compromised.

## Immediate Rotation Checklist

Perform these actions outside the repository before the next production deployment:

1. Rotate PostgreSQL passwords and invalidate old users/passwords.
2. Rotate every JWT signing key and force token/session revocation.
3. Rotate SMTP credentials and revoke old app passwords.
4. Rotate RabbitMQ credentials and replace any shared admin credentials.
5. Rotate MongoDB credentials and invalidate old connection users.
6. Rotate Baileys service API keys or shared service credentials.
7. Rotate Commerce AI provider keys and revoke any leaked tokens.
8. Rotate R2 access keys and invalidate the compromised pair.
9. Rotate abandoned-cart signing secrets and invalidate old recovery links.

## Secure Configuration Sources

The application now expects sensitive values from one of these sources:

1. Environment variables
2. `dotnet user-secrets` for local development
3. Gitignored `appsettings.{Environment}.local.json` files

Committed `appsettings*.json` files must remain free of secrets.

## Local Development

The API project is configured for `dotnet user-secrets`.

Example commands:

```bash
dotnet user-secrets init --project PointOfSale.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=PosEcommerceDb;Username=postgres;Password=..." --project PointOfSale.API
dotnet user-secrets set "JwtSettings:Secret" "replace-with-32-plus-char-secret" --project PointOfSale.API
dotnet user-secrets set "SuperAdminJwt:Secret" "replace-with-different-32-plus-char-secret" --project PointOfSale.API
dotnet user-secrets set "StorefrontJwt:Secret" "replace-with-different-32-plus-char-secret" --project PointOfSale.API
dotnet user-secrets set "CustomerJwt:Secret" "replace-with-different-32-plus-char-secret" --project PointOfSale.API
dotnet user-secrets set "AffiliateJwt:Secret" "replace-with-different-32-plus-char-secret" --project PointOfSale.API
dotnet user-secrets set "RabbitMQ:Host" "localhost" --project PointOfSale.API
dotnet user-secrets set "RabbitMQ:Username" "guest" --project PointOfSale.API
dotnet user-secrets set "RabbitMQ:Password" "guest" --project PointOfSale.API
dotnet user-secrets set "MongoDB:ConnectionString" "mongodb://localhost:27017" --project PointOfSale.API
```

## Deployment

- Use environment variable injection for every secret and connection endpoint.
- Use [staging/pointofsale.api.env.example](../../staging/pointofsale.api.env.example) as the baseline variable map.
- Keep `appsettings.Production.json` and `appsettings.Staging.json` free of credentials.
- Do not reintroduce fallback secrets in code, config, scripts, or CI variables.

## CI Secret Scanning

GitLeaks scanning is enforced in [`.github/workflows/secret-scan.yml`](../../.github/workflows/secret-scan.yml).

- Pull requests and pushes to `main`/`master` are scanned.
- The workflow fails on any detected secret.
- Keep `fetch-depth: 0` so historical commits in the checked-out range are scanned correctly.

## Git History Purge

Removing the current working tree secrets is not enough. You must also rewrite repository history.

Preferred approaches:

1. `git filter-repo`
2. BFG Repo-Cleaner

Example `git filter-repo` workflow:

```bash
git filter-repo --path PointOfSale.API/appsettings.json --invert-paths
git filter-repo --path PointOfSale.WhatsApp.Baileys/auth_states --invert-paths
```

For targeted value replacement, use a replacements file and then force-push rewritten branches. Coordinate this carefully with every collaborator before execution.

## Runtime Enforcement

Startup now fails closed when required secrets are missing, blank, placeholder-based, or partially configured. This is intentional and should remain in place.
