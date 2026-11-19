# Deployment notes

The code runs the same on Windows (IIS or Kestrel) and Linux. Public hosting is outside this version; these notes say what to set when it happens.

## Settings that must come from the environment

Never put these in a file in the repository (NFR-42). Use user-secrets locally and environment variables on a server (a double underscore replaces the colon).

| Setting | Used by | Note |
|---|---|---|
| `Jwt__Key` | API | At least 32 characters. The API refuses to start without it. |
| `ConnectionStrings__DefaultConnection` | API | Use a login that can read and write data but cannot change the schema (NFR-28). |
| `Gateway__Mode` | API | `Fake` for demos. `SslCommerz` for the sandbox or live. |
| `Gateway__StoreId`, `Gateway__StorePassword`, `Gateway__BaseUrl`, `Gateway__CallbackBaseUrl` | API | Only in `SslCommerz` mode. The callback URL must be reachable by the gateway. |
| `Smtp__Host`, `Smtp__Port`, `Smtp__UseTls`, `Smtp__User`, `Smtp__Password`, `Smtp__From` | API | |
| `Storage__Root` | API | Folder for private files (NID, return photos) and product images. Keep it **outside** the web folder and back it up. |
| `DataProtection__KeysFolder` | API | **Required in production.** NID numbers and payout accounts are encrypted with these keys. Lose the keys and that data cannot be read. Back them up with the database. |
| `Seed__AdminEmail` | API | Email of the first admin. |
| `Jobs__Enabled` | API | `false` stops the Hangfire jobs (for one-off tools). Run exactly one API instance with jobs on until a queue is added. |
| `Web__BaseUrl` | API | Where the gateway sends the buyer back. |
| `Api__BaseUrl` | Web | Address of the API. |
| `Csp__FormActions` | Web | Origins the checkout form may redirect to (the payment page). Defaults to the API origin. |

## Database

1. Run `db/EMarketDb_schema.sql` on a new SQL Server 2019+ instance.
2. Hangfire creates its own tables in the `HangFire` schema at first start; the login used at that time needs permission to create them (or create them once with a migration login).
3. The ledger, audit and order-history tables have insert-only triggers. The retention job for old audit rows must disable and re-enable the audit trigger (see the notes at the end of the SQL file).
4. Backups: a full backup every day, kept 7 days, in a folder the web server does not serve (NFR-8, NFR-79). Test one restore before release.

## Before going public

- HTTPS only (HSTS is on outside Development). Set `ASPNETCORE_ENVIRONMENT=Production`.
- Replace the built-in test gateway with SSLCommerz credentials and try the sandbox: payment, callback, failure, cancel and a refund.
- Load the full list of Bangladesh upazilas and areas into `Location`. The script has only a sample.
- Get the legal points of the SRS (consumer rights, data protection, tax on commission and delivery fees) reviewed.
- Set up log shipping and alerts for failed payments, failed jobs and 5xx answers.
