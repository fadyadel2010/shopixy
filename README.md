# Shopixy

Shopixy is a multi-tenant commerce platform built around a .NET 9 backend and
several TypeScript/React single-page applications. The solution combines a
point-of-sale and admin backend, tenant storefronts, a customer-facing commerce
hub, an affiliate program, and a WhatsApp integration microservice.

This is a private, internal repository. It is intended for the engineering team
working on the platform.

## Overview

The repository contains one .NET solution (`PointOfSale.sln`) and multiple
front-end applications and supporting assets. The backend is organized following
a Clean Architecture layering (Core, Infrastructure, API) and exposes a large
HTTP API (over 150 controllers) that serves several distinct client
applications:

- A POS/admin web application (`PointOfSale.Web`).
- A tenant storefront application (`PointOfSale.Storefront`).
- A customer-facing commerce hub (`PointOfSale.CommerceHub`).
- A WhatsApp integration microservice (`PointOfSale.WhatsApp.Baileys`).

The platform is multi-tenant. Tenant resolution in the API is host-based: the
API expects the storefront host to embed a tenant slug in the hostname (see
`Storefront:UrlTemplate` in configuration and the staging topology notes in
`deploy/staging/README.md`).

The backend integrates a broad set of runtime dependencies: PostgreSQL as the
authoritative relational store, MongoDB and Redis as derivative read/cache
stores, Elasticsearch for product search, RabbitMQ (via MassTransit) for
messaging, and Hangfire for background jobs. Payment integrations
(Stripe, Paymob, and a manual-payment flow) are present and feature-gated.

## Architecture

### Backend

ASP.NET Core (.NET 9) Web API, structured in three projects:

- `PointOfSale.Core` — domain entities, enums, interfaces, DTOs, and
  business abstractions. No external infrastructure dependencies beyond
  supporting libraries.
- `PointOfSale.Infrastructure` — data access (Entity Framework Core /
  Npgsql), MongoDB, Redis, Elasticsearch (NEST), messaging consumers,
  payment/fulfillment/accounting/treasury services, WhatsApp integration,
  and background jobs.
- `PointOfSale.API` — controllers, authentication, SignalR hubs, middleware,
  filters, health checks, Hangfire dashboard wiring, and OpenTelemetry
  instrumentation. This is the startup/host project.

Cross-cutting backend capabilities present in the codebase include SignalR
real-time hubs (with an optional Redis backplane for multi-instance
deployments), OpenTelemetry metrics/tracing (OTLP and Prometheus exporters),
JWT authentication with multiple distinct token audiences (POS, SuperAdmin,
Storefront, Customer/CommerceHub, Affiliate), and Hangfire background
processing backed by PostgreSQL.

### Frontend

Three independent Vite + React 18 + TypeScript applications:

- `PointOfSale.Web` — POS and administration SPA. Uses React Router,
  Zustand, Radix UI, Tailwind CSS, TanStack Query, Recharts, and SignalR
  client for real-time updates. Includes Vitest-based unit and architecture
  tests.
- `PointOfSale.Storefront` — tenant storefront SPA with Stripe.js
  integration and i18n.
- `PointOfSale.CommerceHub` — customer-facing commerce hub SPA.

### Supporting services

- `PointOfSale.WhatsApp.Baileys` — a Node.js + TypeScript microservice built
  on `@whiskeysockets/baileys`, exposing an Express HTTP API, using Redis for
  state and `prom-client` for metrics. The .NET API communicates with it over
  HTTP (see the `BaileysService` configuration section).

### Databases

- PostgreSQL — authoritative relational database (EF Core / Npgsql), also
  used as the Hangfire job store.
- MongoDB — derivative read models / product read database.
- Redis — caching, distributed state, and SignalR backplane.

### Messaging

- RabbitMQ via MassTransit for event-driven messaging and consumers.

### Search

- Elasticsearch (NEST client) for product and CommerceHub product indexes.

## Repository Structure

```
PointOfSale/
├── PointOfSale.sln                 # Visual Studio solution (Core, Infrastructure, API, Tests)
├── global.json                     # Pins the .NET SDK (9.0.308)
├── PointOfSale.Core/               # Domain layer: entities, enums, interfaces, DTOs
├── PointOfSale.Infrastructure/     # Data access, external services, jobs, integrations
├── PointOfSale.API/                # ASP.NET Core host: controllers, hubs, middleware, config
├── PointOfSale.Tests/              # xUnit test project
├── PointOfSale.Web/                # POS/admin SPA (Vite + React + TS)
├── PointOfSale.Storefront/         # Tenant storefront SPA (Vite + React + TS)
├── PointOfSale.CommerceHub/        # Customer commerce hub SPA (Vite + React + TS)
├── PointOfSale.WhatsApp.Baileys/   # WhatsApp integration microservice (Node + TS)
├── DB/                             # SQL migration scripts and query assets
│   ├── migrations/                 # Raw SQL migrations (feature/hotfix scripts)
│   └── es_query.json               # Elasticsearch query asset
├── deploy/                         # Deployment assets
│   ├── staging/                    # Staging topology, Caddyfile, env example
│   └── n8n-workflows/              # n8n workflow definitions (e.g. abandoned cart)
├── docs/                           # Architecture, audits, certifications, runbooks
├── .config/                        # Tooling configuration
└── .vscode/                        # Shared editor configuration
```

Only `PointOfSale.Core`, `PointOfSale.Infrastructure`, `PointOfSale.API`, and
`PointOfSale.Tests` are members of `PointOfSale.sln`. The React and Node
applications are built independently with their own toolchains.

## Technology Stack

| Technology | Usage |
| --- | --- |
| .NET 9 (ASP.NET Core) | Backend API, domain, and infrastructure (`net9.0`) |
| C# | Backend implementation language |
| Entity Framework Core 9 (Npgsql) | ORM and PostgreSQL data access |
| PostgreSQL | Authoritative relational database and Hangfire store |
| MongoDB (MongoDB.Driver) | Derivative read models / product read store |
| Redis (StackExchange.Redis) | Caching, distributed state, SignalR backplane |
| RabbitMQ (MassTransit) | Event-driven messaging and consumers |
| Elasticsearch (NEST) | Product / CommerceHub product search indexes |
| Hangfire | Background job processing (PostgreSQL-backed) |
| SignalR | Real-time server-to-client communication |
| OpenTelemetry | Metrics and tracing (OTLP + Prometheus exporters) |
| Stripe.net | Server-side payment integration |
| AWS SDK (S3) | Object storage (Cloudflare R2 via S3-compatible API) |
| Microsoft.Playwright | Browser automation in infrastructure |
| React 18 | Front-end UI framework (Web, Storefront, CommerceHub) |
| TypeScript | Front-end language across all SPAs and the WhatsApp service |
| Vite | Front-end build tooling / dev server |
| Tailwind CSS | Front-end styling |
| Node.js (>= 20) | WhatsApp Baileys microservice runtime |
| Baileys / Express | WhatsApp microservice (`@whiskeysockets/baileys`, Express) |
| SQL | Raw migration scripts under `DB/migrations` |
| Caddy | Reverse proxy for the staging topology |

## Projects

| Name | Purpose | Technology |
| --- | --- | --- |
| PointOfSale.Core | Domain entities, enums, interfaces, DTOs, business abstractions | .NET 9 class library |
| PointOfSale.Infrastructure | Data access, external services, messaging, jobs, integrations | .NET 9, EF Core, MongoDB, Redis, NEST, MassTransit |
| PointOfSale.API | ASP.NET Core host: controllers, hubs, auth, middleware, jobs | .NET 9 Web API, SignalR, Hangfire, OpenTelemetry |
| PointOfSale.Tests | Automated tests for backend layers | xUnit, Moq, FsCheck, EF Core InMemory/Sqlite |
| PointOfSale.Web | POS and administration single-page application | Vite, React 18, TypeScript, Tailwind, Zustand |
| PointOfSale.Storefront | Tenant-facing storefront application | Vite, React 18, TypeScript, Stripe.js |
| PointOfSale.CommerceHub | Customer-facing commerce hub application | Vite, React 18, TypeScript, TanStack Query |
| PointOfSale.WhatsApp.Baileys | WhatsApp integration microservice | Node.js, TypeScript, Baileys, Express, Redis |

## Prerequisites

Required to build and run the full platform:

- .NET SDK 9.0.308 (pinned in `global.json`, `rollForward: latestPatch`).
- Node.js >= 20 (required by the WhatsApp microservice `engines` field; the
  React apps use the same toolchain).
- PostgreSQL (authoritative database and Hangfire store).
- Redis (caching, distributed state, SignalR backplane).
- MongoDB (read models / product read store).
- RabbitMQ (MassTransit messaging).
- Elasticsearch (product search indexes).

The EF Core CLI (`dotnet-ef`) is required only if you manage EF migrations.
Playwright browser binaries are required if you exercise the Playwright-based
infrastructure code paths.

## Getting Started

The commands below assume the repository root as the working directory.

### 1. Clone

```bash
git clone <repository-url>
cd PointOfSale
```

### 2. Restore backend dependencies

```bash
dotnet restore PointOfSale.sln
```

### 3. Build the backend

```bash
dotnet build PointOfSale.sln
```

### 4. Install front-end dependencies

Each SPA and the microservice manages its own dependencies:

```bash
cd PointOfSale.Web && npm install && cd ..
cd PointOfSale.Storefront && npm install && cd ..
cd PointOfSale.CommerceHub && npm install && cd ..
cd PointOfSale.WhatsApp.Baileys && npm install && cd ..
```

### 5. Configure environment / secrets

Runtime secrets and connection endpoints are intentionally not committed. The
committed `PointOfSale.API/appsettings.json` ships with empty secret fields.
Provide values through environment variables, .NET user secrets, or gitignored
`*.local.json` files. See the Configuration section below and
`deploy/staging/pointofsale.api.env.example` for the canonical variable list.

### 6. Prepare the database

Provide a PostgreSQL connection string via
`ConnectionStrings__DefaultConnection`, then apply schema. EF Core migrations
live under `PointOfSale.Infrastructure/Migrations`; additional raw SQL scripts
live under `DB/migrations`.

```bash
dotnet ef database update --project PointOfSale.Infrastructure --startup-project PointOfSale.API
```

### 7. Run the backend

```bash
dotnet run --project PointOfSale.API
```

The API listens on `http://localhost:5010` and `https://localhost:7263` by
default (see `PointOfSale.API/Properties/launchSettings.json`).

### 8. Run the front-end applications

```bash
cd PointOfSale.Web && npm run dev          # http://localhost:5173
cd PointOfSale.Storefront && npm run dev    # http://localhost:5174
cd PointOfSale.CommerceHub && npm run dev   # http://localhost:5175
```

### 9. Run the WhatsApp microservice (optional)

```bash
cd PointOfSale.WhatsApp.Baileys
npm run dev     # ts-node src/index.ts
# or
npm run build && npm start
```

## Configuration

Configuration is layered via ASP.NET Core `appsettings` files, environment
variables, and user secrets.

- `appsettings.json` — committed base configuration with non-secret defaults.
  Secret fields (connection strings, JWT secrets, payment keys, SMTP,
  R2/object storage, RabbitMQ/Mongo/Redis/Elasticsearch endpoints) are present
  but empty by design.
- `appsettings.Development.json`, `appsettings.Staging.json`,
  `appsettings.Production.json`, `appsettings.Test.json` — environment-specific
  overrides.
- `appsettings.*.local.json` — machine-local, gitignored overrides for secrets
  and endpoints.
- User secrets — the API and Infrastructure projects share a `UserSecretsId`
  (`bbdf2e74-8790-4753-8874-f20fcfbdbbd5`); use `dotnet user-secrets` for local
  secret storage.
- Environment variables — use the double-underscore convention
  (for example `ConnectionStrings__DefaultConnection`, `JwtSettings__Secret`).
  `deploy/staging/pointofsale.api.env.example` lists the deployment variables.

Notable configuration sections include multiple JWT audiences
(`JwtSettings`, `SuperAdminJwt`, `StorefrontJwt`, `CustomerJwt`,
`AffiliateJwt`), `CommerceHub` (payments, payouts, refunds, emergency stops),
`RabbitMQ`, `MongoDB`, `Redis`, `Elasticsearch`, `Hangfire`, `Cors`, and
`BaileysService`.

Do not commit real credentials. Rotate any secret that is accidentally
exposed before deploying.

## Build

Backend:

```bash
dotnet build PointOfSale.sln
```

Front-end applications:

```bash
cd PointOfSale.Web && npm run build
cd PointOfSale.Storefront && npm run build      # runs tsc then vite build
cd PointOfSale.CommerceHub && npm run build     # runs tsc then vite build
```

WhatsApp microservice:

```bash
cd PointOfSale.WhatsApp.Baileys && npm run build
```

## Testing

Backend (xUnit):

```bash
dotnet test PointOfSale.sln
```

POS/admin SPA (Vitest):

```bash
cd PointOfSale.Web
npm run test          # unit tests
npm run test:arch     # architecture tests
```

WhatsApp microservice (Jest):

```bash
cd PointOfSale.WhatsApp.Baileys
npm test
npm run test:coverage
```

## Repository Standards

- Configuration: secrets are excluded from source control; `appsettings.json`
  carries only non-secret defaults, with secrets supplied via environment
  variables, user secrets, or gitignored `*.local.json` files.
- Git ignore: `.gitignore` is source-code-backed and documented rule by rule
  (see `docs/project`), covering OS artifacts, IDE metadata, .NET `bin/`/`obj/`,
  NuGet packages, Node `node_modules`/`dist`, Playwright browsers, and local
  secret files while retaining shared VS Code workspace configuration.
- Project layout: backend follows Clean Architecture (Core → Infrastructure →
  API); each front-end application is self-contained with its own toolchain.
- Coding standards: backend projects enable nullable reference types
  (`Nullable`) and implicit usings; front-end projects use TypeScript with
  ESLint (React and React Hooks plugins) where configured.

## Deployment

Deployment assets present in the repository target a reverse-proxied,
externally-provisioned-dependency topology (there are no Docker or Kubernetes
manifests in this repository):

- `deploy/staging/README.md` — staging topology: host-based tenant routing,
  isolated PostgreSQL/Redis/MongoDB/RabbitMQ/Elasticsearch/R2 dependencies,
  and secret-injection guidance.
- `deploy/staging/Caddyfile` — Caddy reverse-proxy configuration serving the
  API, POS/admin SPA (`PointOfSale.Web/dist`), and wildcard tenant storefront
  SPA (`PointOfSale.Storefront/dist`).
- `deploy/staging/pointofsale.api.env.example` — canonical environment variable
  list for API deployment.
- `deploy/n8n-workflows/` — n8n workflow definitions (for example, abandoned
  cart recovery).

The staging setup expects wildcard DNS for tenant storefronts so the backend
can resolve tenant context from the request host.

## License

Internal proprietary software. Private repository. All rights reserved.

## Notes

- This repository is intended for internal development.
- Sensitive runtime secrets are intentionally excluded from source control;
  committed configuration files contain non-secret defaults only.
