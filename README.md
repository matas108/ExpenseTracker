# Expense Tracker API

A small personal-finance REST API: accounts, categories and transactions, CSV import from bank
exports, and income/expense summaries. Built with ASP.NET Core and EF Core.

**Live demo:** https://expensetracker-c99f88.azurewebsites.net (opens Swagger UI)

> Hosted on free tiers. The first request after a quiet period can take up to a minute while the
> app and the serverless database wake up.

## Features

- **Auth:** register / login with ASP.NET Core Identity, JWT bearer tokens, lockout after 5 failed logins
- **Accounts, categories, transactions:** full CRUD, every query scoped to the signed-in user
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
git clone <this repo>
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
6. `GET /api/transactions` and `GET /api/summary`.

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
redeploys on later runs. It needs the Azure CLI, run `az login` first:

```powershell
./scripts/deploy-azure.ps1
```

It creates:

- **App Service:** plan F1 (free, Linux), HTTPS only.
- **Azure SQL Database:** free serverless offer. If the monthly free allowance runs out it pauses rather than billing.
- **Passwordless database access:** the web app connects with its system-assigned managed identity, and the SQL server accepts Microsoft Entra authentication only, so no database password exists anywhere.
- **JWT signing key:** generated once, stored as an app setting and never printed.

## Design notes

- **Summaries are calculated in the app, not the database.** SQLite can't `SUM` decimals exactly, and a personal ledger is small. This keeps the totals exact and the code identical on both providers.
- **Currencies are never mixed.** Multi-currency conversion is out of scope, so `/api/summary` covers one currency. If your transactions span several, it asks you to pick one with `?currency=`.
- **Deletes:**
  - Deleting an account that still has transactions returns `409`.
  - Deleting a category leaves its transactions uncategorised.
  - Transactions aren't cascade-deleted with their user, because SQL Server doesn't allow two cascade paths to the same table.

## Roadmap

- v2: bank sync via GoCardless (the `Source` and `ExternalId` fields are already in place)
