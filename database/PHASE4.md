# Phase 4: manual account setup

The `roles` and `users` tables already appear in `scripts/002_create_master_tables.sql`. No new tables or schema migrations are required. Do not rerun that baseline against existing tables.

1. Run `inserts/001_insert_roles.sql` if the roles have not been seeded.
2. From the solution root, run `dotnet run --project src/StockFlow.Web -- --hash-password`. Enter and confirm your chosen application password (at least 12 characters); it is not displayed. This command only generates a hash and does not connect to the database.
3. Paste the generated hash into `inserts/003_insert_admin.sql` in place of `PASTE_GENERATED_HASH_HERE`.
4. Execute that SQL manually against the database containing the Phase 2 tables. It creates username `admin` and does not replace an existing account.
5. Start the web app and open `/Account/Login`. Use `admin` and your chosen application password, not the PostgreSQL server password.

Usernames are case-sensitive, matching the existing database uniqueness constraint. Disabled users and disabled roles cannot sign in. Category reads allow Admin, Sales, and Service; category writes and database health require Admin. The basic `/api/health` remains public. All Razor Pages require login except Login, AccessDenied, and Error.

Cookie-authenticated API writes require the `X-CSRF-TOKEN` header. The signed-in layout supplies its value in `<meta name="csrf-token">`. API authentication failures return HTTP 401/403. Logout uses a POST form with Razor's anti-forgery token. Production requires HTTPS; local Development also supports HTTP.

Existing login cookies expire after eight hours with sliding renewal. Changes to a user's role or active status apply on their next login; immediate session revocation is not implemented in this phase.

Run the database-independent password and login checks with `dotnet run --project tests/StockFlow.AuthChecks`. After manually creating the account, verify login with the chosen password, page refresh, POST logout, and rejection of an incorrect password in the browser. A successful database-backed login and browser cookie round trip still require your configured database and seeded account.
