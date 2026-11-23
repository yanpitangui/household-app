# HouseholdApp

A shared space for running a household: split expenses, keep shopping lists, collect recipes and divide chores. Server-rendered ASP.NET Core with Razor Pages and HTMX, installable as a PWA, available in English and Brazilian Portuguese.

## Features

- **Households** — create a household, invite members with single-use links, and manage roles (Owner, Admin, Member).
- **Expenses** — record shared expenses with flexible funding and splits, organize them into groups, track pairwise balances, and settle up. Supports installment plans, recurring expenses and importing from a Splitwise CSV export. Amounts are stored as integer cents and every balance is derived from an event log.
- **Lists** — shopping lists with quantities, units and categories, backed by a product catalog.
- **Recipes** and **Tasks** — shared recipes and household chores, including recurring tasks.
- **Live updates** — pages refresh over Server-Sent Events when another member changes something.
- **Push notifications** — Web Push for expense activity.

## Tech stack

| Concern | Choice |
|---|---|
| Runtime | .NET 11, ASP.NET Core |
| UI | Razor Pages + HTMX (no JS build step) |
| Database | PostgreSQL, one schema per module |
| Persistence | Marten (event store) for Expenses, Dapper for everything else |
| Migrations | DbUp scripts, applied by the web app at startup |
| Scheduling | Quartz.NET, clustered on PostgreSQL |
| Cache | Redis |
| Authorization | [Valtuutus](https://github.com/valtuutus/valtuutus) (relationship-based access control) |
| Authentication | OpenID Connect (Google in production) |
| Observability | OpenTelemetry |
| Local orchestration | .NET Aspire |

## Project layout

```
src/
  HouseholdApp.AppHost/          Aspire host: starts Postgres, Redis and the web app
  HouseholdApp.Application/      Business logic, one folder per module
    Modules/{Module}/
      Domain/                    Aggregates, value objects, domain events
      Application/               Command/query services and port interfaces
      Infrastructure/            Repositories and port implementations
    Shared/                      Cross-cutting building blocks (events, persistence, auth, scheduler)
  HouseholdApp.Migrations/       DbUp SQL scripts
  HouseholdApp.ServiceDefaults/  OpenTelemetry, health checks, service discovery
  HouseholdApp.Web/              Razor Pages, endpoints, static assets, localization resources
tests/
  HouseholdApp.UnitTests/
  HouseholdApp.IntegrationTests/ Runs against PostgreSQL in Testcontainers
  HouseholdApp.E2ETests/         Playwright against the full app with a fake OIDC server
```

## Getting started

### Prerequisites

- .NET 11 SDK (preview)
- Docker (Aspire and Testcontainers both use it)
- A Google OAuth client for sign-in

### Run locally

The Aspire host prompts for these parameters on first run, or you can set them as user secrets on `src/HouseholdApp.AppHost`:

| Parameter | Purpose |
|---|---|
| `postgres-password` | Password for the local PostgreSQL container |
| `google-client-id` | Google OAuth client ID |
| `google-client-secret` | Google OAuth client secret |
| `admin-email` | Email allowed into the Quartz dashboard at `/quartz` |

```bash
dotnet run --project src/HouseholdApp.AppHost
```

The app listens on `http://localhost:9000`, and the Aspire dashboard shows logs, traces and metrics for every resource. Database migrations run automatically when the web app starts.

To enable push notifications, set `Push__VapidPublicKey` and `Push__VapidPrivateKey` on the web project.

### Build and test

```bash
dotnet build HouseholdApp.slnx

dotnet test tests/HouseholdApp.UnitTests/HouseholdApp.UnitTests.csproj
dotnet test tests/HouseholdApp.IntegrationTests/HouseholdApp.IntegrationTests.csproj
dotnet test tests/HouseholdApp.E2ETests/HouseholdApp.E2ETests.csproj
```

Tests use [TUnit](https://tunit.dev) on Microsoft.Testing.Platform.

## Integrations API

Other apps and automations can add list items and record expenses with an **access token**
(Household settings → Access tokens). A token belongs to one member and one household, can only do what its
permissions allow, expires (30/90/365 days), and can be revoked at any time. It is shown once — store it safely.

```
POST /api/integrations/list-items
POST /api/integrations/expenses
Authorization: Bearer hha_…
Content-Type: application/json
```

Send a sentence and let the server read it, or send fields directly (fields win over the sentence):

```json
{ "text": "milk, eggs and 2 kg rice to groceries" }
{ "list": "groceries", "items": [{ "name": "rice", "quantity": "2", "unit": "kg" }] }
{ "text": "50 reais mercado em despesas variáveis pago por Ana" }
{ "text": "pizza", "amount": "50", "group": "Trip" }
```

Call the API over `https://` — outside development, plain `http://` requests get a `400`.

Every response is JSON with a `message` ready to show or speak:

| Status | Meaning |
|---|---|
| `201` | Saved. `parsed` shows how the sentence was read, `unparsed` lists anything ignored. |
| `422` | Nothing was saved. Either something is missing or unclear — `issues` lists each `field` with a `reason` (`missing`, `ambiguous`, `notFound`) and `candidates` to choose from; send the request again with the sentence plus those fields — or a household rule refused it (only `message`). |
| `400` | Malformed body, both or neither of `text`/`items`, or input over the size limits (text 1000 chars, 50 items, 64 KB body; `413` for an oversized body). |
| `401` | Missing, invalid, expired or revoked token. |
| `403` | The token lacks the permission for this endpoint. |
| `404` | The token's owner is no longer in the household. |
| `429` | More than 30 requests per minute for this token. |

Expenses are split equally among current members; the payer is you unless the sentence says otherwise
("pago por Ana", "paid by Leo").

### Example: iOS Shortcut with Siri

1. **Dictate Text** → **Get Contents of URL** (`POST`, header `Authorization: Bearer <token>`, JSON body
   `text` = Dictated Text).
2. **If** the status is 422: for each item in `issues` — `amount` → **Ask for Input** (Number); a field with
   `candidates` → **Choose from List**; otherwise **Ask for Input** (Text). Send the request again with `text` plus
   those fields.
3. **Speak Text** → `message`.

Name the Shortcut (e.g. "Add to groceries") and say "Hey Siri, add to groceries". When sharing it, use
**Import Questions** for the token so each person pastes their own.

### Deploy notes

Production must set `Integrations__TokenKey` (base64, at least 32 bytes) and `Redaction__Hmac__Key` (base64, at least 44 characters) together with `Redaction__Hmac__KeyId`; the app refuses to start without them. Rotating `Integrations__TokenKey` invalidates every existing access token.

## Deployment

CI builds a container image from the `Dockerfile` and pushes it to GitHub Container Registry. The image needs:

- `ConnectionStrings__householdapp` — PostgreSQL connection string
- `ConnectionStrings__redis` — Redis connection string
- `Oidc__Authority`, `Oidc__ClientId`, `Oidc__ClientSecret`
- `Push__VapidPublicKey`, `Push__VapidPrivateKey` (optional)
- `Integrations__TokenKey`, `Redaction__Hmac__Key`, `Redaction__Hmac__KeyId` (see [Integrations API](#integrations-api))
- `Admins__0` (and further indexes) — emails allowed into the Quartz dashboard
