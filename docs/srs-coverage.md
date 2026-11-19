# SRS coverage

Status of every SRS v2.0 feature on this branch. **Done** means working code with automated tests (unit tests, and the end-to-end scripts in `tools/`). Numbers refer to the SRS.

## Features

| # | Feature | Status | Notes |
|---|---|---|---|
| 1 | Registration and authentication | Done | PBKDF2 hash, email verify (24 h), resend limit 3/hour, login lock, 15-minute JWT, one-time refresh token with replay detection, reset (30 min), change password, first-login admin password change. |
| 2 | Profile and addresses | Done | Email change needs the link; one default address; linked division, district and area. |
| 3 | Seller registration and verification | Done | Encrypted NID and payout account, private documents, admin-only access with an audit row per opening, 7-day reapply rule. |
| 4 | Product management | Done | Variants, shop-wide SKUs, 1 to 5 images (re-encoded, thumbnails), approval flow, copy, soft delete, low-stock alert once per crossing. |
| 5 | Browsing, search, filter | Done | 20 per page, filters, sort, cached category tree and home page (60 s). |
| 6 | Shopping cart | Done | Limits, availability check, own-product rule. |
| 7 | Wishlist | Done | |
| 8 | Checkout | Done | One order per shop in one transaction, idempotency key, stock reservation, per-shop delivery fee, coupon and credit split. |
| 9 | Payment | Done with a gap | Server-to-server verification, once per transaction id, expiry job, 10-minute reconciliation, refund by refund id. **The SSLCommerz client has not been run against the real sandbox** (no credentials here); a built-in test gateway is used by default. |
| 10 | Order tracking (buyer) | Done | Cancel rules, reorder, PDF invoice (QuestPDF). |
| 11 | Seller order management | Done | State machine, tracking note, packing slip, 48-hour confirm timeout job. |
| 12 | Reviews | Done with a gap | The optional **review title** (FR-12.2) is not stored: the table has no title column. |
| 13 | Returns and refunds | Done | Partial returns, not-received reports, escalation, refund rules, store-credit and gateway refunds, retry. |
| 14 | Notifications | Done | In-app and email via the outbox with 3 retries. Unread badge refreshes every 30 s. |
| 15 | Seller dashboard, earnings, shop settings | Done | Balances come from the ledger; payouts; password-checked payout details; vacation mode. |
| 16 | Admin: users and shops | Done | Block, delete, suspend, create admin, audit. |
| 17 | Admin: products and categories | Done with a gap | FR-17.11 (mark which fields changed in the approval queue) is not done. |
| 18 | Admin: orders, payments, payouts | Done with a gap | FR-18.11 Excel export exists for the sales report and the seller earnings, **not** for orders and payments lists. |
| 19 | Admin: coupons and banners | Done | |
| 20 | Admin: dashboard and reports | Done | Sales, users, finance; Excel export; 5-minute cache; one-year range limit. Charts are server-drawn SVG, not ApexCharts. |
| 21 | Admin: system settings | Done | Range-checked, audited, maintenance mode. |
| 22 | Static pages and error handling | Done | Admin HTML is sanitised; friendly 403/404/429/500/Maintenance pages with a correlation id. |
| 23 | Contact messages | Done | |
| 24 | Financial ledger | Done | Insert-only, balanced, booked once per event; nightly balance check job. |

## Quality requirements that are not done

These are listed so nobody has to guess.

| Item | State |
|---|---|
| AD-10 Redis cache | Not done. An in-memory cache with the same 60-second rule is used. Redis is optional in the SRS. |
| AD-6 AutoMapper, AD-9 MediatR | Not used. Mapping is written by hand and the outbox is written directly in the same transaction as the business change, which gives the same guarantee (FR-14.4). |
| AD-12 Seq | Logs go to the console with a correlation id (Serilog). No Seq viewer is wired. |
| NFR-47 EF Core migrations | The schema is created by `db/EMarketDb_schema.sql` (35 tables, checks, triggers, seed). There are no EF migrations; the model is mapped to that script. |
| NFR-8, NFR-28, NFR-79 | Backups and a separate migration login are documented in `docs/deployment.md`, not automated. |
| NFR-48, NFR-50 to NFR-60 | No Playwright tests in three browsers and no load tests were run. Performance numbers are targets, not measurements. |
| NFR-69 | Every business rule has behaviour covered by tests, but there is no rule-to-test list and no coverage report yet. |
| NFR-76 | Export and anonymisation of a deleted user's data is not built. |
| NFR-40 | Login (10/min), verification links (3/hour) and the contact form (3/hour) are limited. The global 100 calls a minute per user is not. |
| Integration tests | The service tests use an in-memory database. Real-SQL behaviour (triggers, row versions, filtered indexes) is checked by the end-to-end scripts, not by Testcontainers. |
| Demo data (SRS 6.7) | Not seeded. Only roles, categories, settings and locations come with the SQL script, and the location script has only a sample of areas (Dhaka and Chattogram cities). |
| Legal review | Not done (SRS Appendix F). |

## What the tests prove

- 98 unit and service tests: auth rules, seller approval, catalog, cart, checkout, order flow, payments, refunds, returns, reviews, earnings and payouts, admin, jobs, file safety, invoices, and the layer rules.
- `tools/e2e_api.py`: 62 checks against the running API.
- `tools/e2e_web.py`: 111 checks through the real web pages, including the whole seller, buyer and admin journey, access rules, anti-forgery, security headers and maintenance mode.
