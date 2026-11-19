# EMarket

An online marketplace for buyers and sellers in Bangladesh. Built to the [Software Requirements Specification v2.0](EMarket_SRS_v2.0_final.pdf): ASP.NET Core 10, Razor Pages, a REST API, EF Core and SQL Server.

Guests browse and search. Buyers fill a cart, check out (cash on delivery, bKash, Nagad, card, store credit), track orders, return items and write reviews. Sellers apply with an identity check, list products with variants, fulfil orders and ask for payouts. Admins approve sellers and products, run orders, refunds, payouts, coupons, banners, settings and reports.

## How it is built

```
Browser -> EMarket.Web (Razor Pages) -> EMarket.Api (REST /api/v1) -> EMarket.BLL -> EMarket.DAL -> SQL Server
```

| Project | Job |
|---|---|
| `src/EMarket.Web` | Razor Pages UI. Talks only to the API through a typed `HttpClient`. Keeps the tokens in an encrypted HttpOnly cookie. |
| `src/EMarket.Api` | Controllers, JWT auth, rate limit, health checks, security headers, Hangfire. |
| `src/EMarket.BLL` | All business rules: identity, catalog, cart, checkout, orders, payments, returns, ledger, admin, reports, jobs. |
| `src/EMarket.DAL` | EF Core `AppDbContext` and the data access wiring. |
| `src/EMarket.Domain` | Entities. |
| `src/EMarket.Shared` | DTOs with DataAnnotations, constants, the system messages (MSG-nn). |
| `tests/EMarket.Tests` | xUnit tests, including the layer rules (NFR-68). |
| `tools/` | End-to-end checks that run the real API and web app against a dev database. |
| `db/EMarketDb_schema.sql` | The database: 35 tables, constraints, indexes, insert-only triggers and the seed data. |

Layer rules, checked by a test: Web uses Shared only; Api uses BLL and Shared; BLL uses DAL, Domain and Shared; DAL uses Domain.

## Run it on your machine

You need the .NET 10 SDK, SQL Server Express (instance `SQLEXPRESS`), `sqlcmd`, and optionally Docker for a local mail inbox.

1. **Create the database** (once). It refuses to run if `EMarketDb` already exists.
   ```bash
   sqlcmd -S localhost\SQLEXPRESS -E -C -I -i db/EMarketDb_schema.sql
   ```
2. **Set the signing key** for the API. It must be at least 32 characters and is never kept in the repository.
   ```bash
   dotnet user-secrets init --project src/EMarket.Api
   dotnet user-secrets set "Jwt:Key" "<a long random value of 32+ characters>" --project src/EMarket.Api
   ```
3. **Mail inbox** (optional): `docker compose up -d`, then open http://localhost:8025. Without it, emails stay queued and retry.
4. **Start the API**, then the web app, in two terminals:
   ```bash
   dotnet run --project src/EMarket.Api --launch-profile https     # https://localhost:7001, Swagger-style OpenAPI in Development
   dotnet run --project src/EMarket.Web --launch-profile https     # https://localhost:7002
   ```
5. **First admin.** On the first start the API creates one admin (`admin@emarket.local`) and prints a random password **once** in its console. The admin must change it at first login. The password is stored nowhere.

Payments use a built-in **test gateway** by default (`Gateway:Mode = Fake`): pressing Pay on its page marks the payment paid, without any real money. To use the SSLCommerz sandbox, set `Gateway:Mode` to `SslCommerz` and put `Gateway:StoreId` and `Gateway:StorePassword` in user-secrets.

Configuration lives in `appsettings.json` (no secrets) and in user-secrets or environment variables (`Jwt__Key`, `ConnectionStrings__DefaultConnection`, `Gateway__StoreId`, `Smtp__Password`, ...). See [docs/deployment.md](docs/deployment.md).

## Test it

```bash
dotnet test                         # 98 unit and service tests, no database needed
python tools/e2e_api.py             # 62 checks against the running API
python tools/e2e_web.py             # 111 checks through the real web pages
```

The two `tools/e2e_*.py` scripts **drop and recreate `EMarketDb_Dev`** (never `EMarketDb`), start the API and web app on ports 5199 and 5300, and drive the whole marketplace: registration, seller approval, products, cash and online checkout, fulfilment, returns and refunds, payouts, reports, maintenance mode and access rules. Run them after `dotnet build`.

## What is covered

See [docs/srs-coverage.md](docs/srs-coverage.md) for each SRS feature, and for what is **not** done yet and why. In short: all 24 features have working code and tests; a few quality items (Redis, load tests, EF migrations, real SSLCommerz sandbox) are listed there as open.

## Money rules in one place

Money is never a stored number. Every movement is a pair of balanced, insert-only ledger lines (FR-24), and the seller and store-credit balances are sums of those lines. A database trigger refuses any update or delete of ledger, audit and order-status rows.

## Branches

Work for the full project is on `feature/full-project`. It is pushed for review and not merged into `main`.
