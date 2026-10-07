![CI](https://github.com/matas108/ExpenseTracker/actions/workflows/ci.yml/badge.svg)

# Expense Tracker API

A small personal-finance REST API: accounts, categories and transactions, CSV import from bank
exports, and income/expense summaries. Built with ASP.NET Core and EF Core.

**Live demo:** https://expensetracker-c99f88.azurewebsites.net (opens Swagger UI)

> Hosted on free tiers. The first request after a quiet period can take up to a minute while the
> app and the serverless database wake up.

## Features

- **Auth:** register / login with ASP.NET Core Identity, JWT bearer tokens, lockout after 5 failed logins, and a rate limit of 10 login/register requests per minute per IP (`429` beyond that)
- **Accounts, categories, transactions:** full CRUD, every query scoped to the signed-in user
- **Account balance:** opening balance plus the sum of the account's transactions
- **Transaction list:** filter by account, category and date range, with paging
- **CSV import:** partial imports with a per-row error report and duplicate detection, so re-importing the same file is safe
- **Summary:** total income, expense and net for a date range, broken down by category and by month
- **Swagger UI:** with a built-in "Authorize" button for the JWT

## Tech stack

.NET 10 · ASP.NET Core Web API · EF Core 10 · ASP.NET Core Identity · JWT · SQLite (local) ·
Azure SQL (deployed) · Swashbuckle · CsvHelper · Azure App Service

## Run locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Nothing else: the app uses
SQLite and creates `expensetracker.db` on first start.

```bash
git clone https://github.com/matas108/ExpenseTracker.git
cd ExpenseTracker
dotnet run --project src/ExpenseTracker.Api
```

Then open http://localhost:5100/swagger.

### Try it in Swagger

1. `POST /api/auth/register` with an email and a password (min. 6 chars, with upper, lower, digit and symbol). Copy the `token`.
2. Click **Authorize** and paste the token.
3. `POST /api/accounts`: `{ "name": "Checking", "type": "Bank", "currency": "EUR" }`
4. `POST /api/categories` for `Salary` (Income), `Rent` and `Groceries` (Expense).
5. `POST /api/transactions/import`: upload [`samples/transactions.csv`](samples/transactions.csv) with `accountId` = 1.
6. `GET /api/transactions`, `GET /api/accounts/1/balance` and `GET /api/summary`.

## Tests

```bash
dotnet test
```

72 xUnit tests in `tests/ExpenseTracker.Api.Tests`, run by GitHub Actions on every push to `main` and every pull request:

- **Integration tests** start the real API in memory with `WebApplicationFactory`, with a throwaway SQLite database per test class. They cover auth, lockout and rate limiting, per-user data isolation, CRUD rules (409s, validation, uncategorising on delete), account balances, filtering and paging, CSV import (dedup, European formats, per-row errors) and summary totals checked against hand-calculated values.
- **Unit tests** cover the CSV amount parser.

## CSV import format

| Column | Required | Notes |
|---|---|---|
| `Date` | yes | `yyyy-MM-dd`, `dd.MM.yyyy`, `dd/MM/yyyy` or `yyyy.MM.dd`. US month-first dates are rejected as ambiguous. |
| `Amount` | yes | Negative = expense, positive = income. `-42.35`, `-42,35`, `1,234.56` and `1.234,56` all work. Max 2 decimals. |
| `Description` | yes | May be empty. |
| `Category` | no | Matched to an existing category by name. Unknown names are reported as row errors. |
| `Account` | no | Matched by name. If there's no Account column, the `accountId` form field is used for every row. |
| `ExternalId` | no | e.g. the bank's transaction ID, used for duplicate detection. |

- **Columns:** header names are case-insensitive, and commas and semicolons both work as separators.
- **Duplicates:** rows without an `ExternalId` are checked on account + date + amount + description. Genuinely identical rows within one file (two coffees on the same day) are both kept.
- **Limits:** 5 MB and 10,000 rows per file.

## Configuration

| Setting | Default | Purpose |
|---|---|---|
| `Database__Provider` | `Sqlite` | `Sqlite` or `SqlServer` |
| `ConnectionStrings__Default` | `Data Source=expensetracker.db` | Database connection |
| `Jwt__Key` | dev-only key in `appsettings.Development.json` | **Required** outside Development, at least 32 bytes. The app refuses to start without it. |
| `Jwt__ExpiryMinutes` | `60` | Token lifetime |
| `RateLimiting__AuthPermitLimit` | `10` | Login/register requests allowed per client IP per minute |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | not set | Set to `true` behind a reverse proxy (the deploy script does this on Azure) so the rate limiter sees each client's real IP |

## Database migrations

Each provider has its own migration set, because SQLite and SQL Server generate different SQL:

- `src/ExpenseTracker.Api/Data/Migrations/Sqlite` for `SqliteAppDbContext`
- `src/ExpenseTracker.Api/Data/Migrations/SqlServer` for `SqlServerAppDbContext`

After a model change, add the migration to **both**:

```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/ExpenseTracker.Api --context SqliteAppDbContext -o Data/Migrations/Sqlite
dotnet ef migrations add <Name> -p src/ExpenseTracker.Api --context SqlServerAppDbContext -o Data/Migrations/SqlServer
```

Migrations are applied automatically on startup.

## Deployment (Azure)

[`scripts/deploy-azure.ps1`](scripts/deploy-azure.ps1) provisions everything on the first run and
redeploys on later runs. It needs the Azure CLI; run `az login` first:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/deploy-azure.ps1
```

`-ExecutionPolicy Bypass` lets this one run go ahead on machines that block unsigned scripts, without changing any system setting.

It creates:

- **App Service:** plan F1 (free, Linux), HTTPS only.
- **Azure SQL Database:** free serverless offer. If the monthly free allowance runs out it pauses rather than billing.
- **Passwordless database access:** the web app connects with its system-assigned managed identity, and the SQL server accepts Microsoft Entra authentication only, so no database password exists anywhere.
- **JWT signing key:** generated once, stored as an app setting and never printed.
- **Forwarded headers:** enabled, because App Service sits behind a proxy. Without it the app would see the proxy's address instead of each client's, and the rate limiter would treat all visitors as one.

## Design notes

- **Summaries are aggregated in the database.** `GroupBy` and `Sum` are translated to SQL on both providers (on SQLite through EF Core's exact `ef_sum` and `ef_compare` functions), so the database returns one row per currency, category and month instead of every transaction. That's three small queries instead of one large one.
- **Rate limiting uses a fixed one-minute window per client IP.** It's simple and predictable. A sliding window or token bucket would smooth out bursts at the window boundary.
- **Currencies are never mixed.** Multi-currency conversion is out of scope, so `/api/summary` covers one currency. If your transactions span several, it asks you to pick one with `?currency=`.
- **Deletes:**
  - Deleting an account that still has transactions returns `409`.
  - Deleting a category leaves its transactions uncategorised.
  - Transactions aren't cascade-deleted with their user, because SQL Server doesn't allow two cascade paths to the same table.

## Roadmap

- v2: bank sync via GoCardless (the `Source` and `ExternalId` fields are already in place)
