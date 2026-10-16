# BillerRestoApi - security & deployment notes (integration-testing build)

> **Not declared production-ready.** This build was simplified for temporary API integration testing: the audit trail and
> security reporting were removed (see "What was removed"). Authentication and authorization are unchanged.

## Login HTTP 500 for correct credentials - how to find and fix the cause
**What is known.** Wrong credentials return 200 (`Success=false`), correct credentials return 500. The wrong-credential path runs
`throttle -> db-connect -> user-lookup -> password-verify`, so those stages work. A correct password additionally runs
`token-issue` (needs the JWT signing key) and `last-login-update` (`UPDATE tbllogin SET lastLogin`). The failure is in one of those
(or, rarely, in serialising the response). Most likely: **the signing key is not configured in production** - it is the only login
requirement that lives in a file/setting that is not part of the published output (`appSettings.secrets.config`; ASP.NET silently
ignores a missing file). This is NOT proven until the log line below is read. The code cannot fix a missing key (it would have to be
hard-coded), so the fix is configuration.

**Read the cause** (log folder: see below). Take `CorrelationId` from the 500 body (or `X-Correlation-Id` header) and search all `api-*.log`:
| Log line | Meaning |
|---|---|
| `evt":"app.start"` | Written at every start: `signingKey` = `ok-env`, `ok-config`, `missing`, `too-short-env`, `too-short-config`; `identity`, `logDir`, `logDirSource` (`app_data`; `configured` only if you set `Logging:Directory`), `logDirWritable`, `connectionStringPresent`, `requireHttps`. No key value, length, connection string or credentials. |
| `evt":"login.stages"` (one per attempt) | e.g. `throttle:ok/db-connect:ok/user-lookup:ok/password-verify:ok/token-issue:FAIL`; `outcome` = `success`, `invalid_credentials`, `throttled`, `key_unavailable`, `exception`; `userId` only for an existing account (no username/password is logged). |
| `evt":"login.signing_key_unavailable"` | Key missing or < 32 chars; field `signingKey` says which (`missing`, `too-short-env`, `too-short-config`). |
| `evt":"login.exception"` | `stage`, `exType`, `mysqlErr`, scrubbed `msg`, inner exception, `stack`. `stage=last-login-update` with `mysqlErr` 1054 = column missing, 1142 = no UPDATE privilege. |
| `evt":"http.request"` | method, normalised route, `status`, `durationMs`, `userId` (token user), same `cid`. |
| `evt":"exception"` | any other exception that reached a controller or the pipeline, once, with the same `cid`. |

## Where are the logs (everything stays inside the API folder)
**Default and only folder:** `<API physical folder>\App_Data\logs`, i.e. `HostingEnvironment.MapPath("~/App_Data/logs")`. The code creates
`App_Data\logs` automatically at start-up (and on first write) - you do not create it, you only make sure the app-pool identity may write
there. No external folder, service or setting is required. (`Logging:Directory` still exists as an optional override; if it is set but not
writable the logger uses `App_Data\logs`. Nothing outside the API folder is ever used.)

```
<API physical folder>\             e.g. D:\Sites\posapi\
  bin\  Content\  Scripts\  Views\  Web.config  Global.asax          <- deployed output
  appSettings.secrets.config                                          <- you copy this by hand; holds Auth:SigningKey; never overwritten by a deploy
  UploadedImages\                                                      <- existing item photos (app-pool needs Modify, as before)
  App_Data\                                                            <- app-pool identity needs Modify here (so the app can create logs\)
    logs\                                                              <- created by the app
      api-yyyyMMdd.log            first 10 MB of the UTC day
      api-yyyyMMdd-1.log          rolls over here, then -2; after 3 parts the rest of that day is dropped
```

**Permissions.** Grant the application-pool identity (`IIS AppPool\<PoolName>`) Modify on `App_Data` (inherited by `logs`):
`icacls "<API folder>\App_Data" /grant "IIS AppPool\<PoolName>:(OI)(CI)M"` (create `App_Data` first if your deploy removed it).
The first line of each start (`evt":"app.start"`) shows the real hosting account (`identity`), `logDirWritable` and `logDir`.
If the account cannot write: the API keeps serving normally (verified), nothing is logged to files, and the failure is reported to
`System.Diagnostics.Trace` and, when the account is allowed to, one Windows **Application** event-log entry (source `ASP.NET 4.0.30319.0`,
id 1000: "diagnostic log folder not writable ..."). No paths, secrets or request data are in that message.

**Bounds.** Per file 10 MB; 3 parts per day (<= 30 MB/day); files older than 30 days (`Logging:RetentionDays`) are deleted; and if the folder
still exceeds 200 MB (`Logging:MaxTotalMB`) the oldest files are deleted. Applied at start-up and at the first write of each UTC day.
Plain text files: anyone with filesystem access (including administrators) can read, change or delete them.

**Not downloadable over HTTP.** `Web.config` (`system.webServer/security/requestFiltering`) hides the `App_Data` segment and denies the `.log`
extension everywhere. Verified on IIS Express with the real `Web.config`: `/App_Data/logs/api-*.log`, `/App_Data/logs/`, `/App_Data/` and
`/App_Data` all return 404; a `.log` file in the site root is served (200) when the rule is removed and 404 with it (the test gave `.log` a MIME
type so IIS would otherwise serve it). Re-check after deploying: `curl -i https://<host>/App_Data/logs/<file>` must be 404.
This build assumes IIS 7+ integrated pipeline. If you host behind another web server, block `App_Data` and `*.log` there as well.

## Production configuration and deployment (nothing is deployed automatically)
1. **Publish** the Release output to the API folder. Keep these files you manage yourself: `appSettings.secrets.config` (signing key) and the
   existing `UploadedImages\`. Do not overwrite them with the deploy. The `BillerRestoDb` connection string in `Web.config` is unchanged by this build.
2. **Signing key** - exactly ONE source, >= 32 characters: `Auth:SigningKey` in `appSettings.secrets.config` next to `Web.config`, or the
   machine/app-pool variable `BILLER_AUTH_SIGNING_KEY` (needs `iisreset`). A non-blank env var overrides the config key. Never commit or hardcode it.
3. **Permissions** - Modify for `IIS AppPool\<PoolName>` on `App_Data` (see above) and `UploadedImages`.
4. **Database** - the login account needs `SELECT` on `tbllogin` and `UPDATE` on `lastLogin`; `tbllogin` needs `id, userName, password, type, lastLogin`.
5. **Recycle the app pool**, then open `<API folder>\App_Data\logs\api-<today>.log`. Expected first line: `app.start` with `signingKey` = `ok-env`/`ok-config`,
   `logDirWritable:true`, `logDirSource:"app_data"`, `connectionStringPresent:true`. Anything else tells you which item above to fix.
6. **Verify the logs are private:** `curl -i https://<host>/App_Data/logs/api-<today>.log` -> 404.
7. **Retry the failing login once**, take `CorrelationId` from the 500 body (or the `X-Correlation-Id` header) and search the log for it.
8. **Rollback:** redeploy the previous build. The log folder can be deleted at any time; the app recreates it.

## Authentication model
- `POST api/auth/login` (anonymous) verifies the PBKDF2 hash and returns the existing fields plus
  `Token`, `TokenType` ("Bearer") and `ExpiresAtUtc`.
- Token: compact HS256 JWT (iss `BillerRestoApi`, aud `ThermalPrinterTest`, sub=user id, name, role, iat, exp, jti).
  Lifetime `Auth:TokenLifetimeMinutes` (default 720 = 12 h). No refresh token: after expiry the app signs in again.
- Every Web API endpoint requires a valid token (global `ApiAuthorizeAttribute`). Only `[AllowAnonymous]`:
  `POST api/auth/login`, `GET api/health/database`.
- Admin-only (403 for other roles): `POST api/categories`, `POST api/items`, `PUT api/items/{id}`,
  `POST|DELETE api/items/{id}/image`. All other endpoints: any authenticated user.
- `created_by` / `updatedBy` on categories, items and bills is taken from the token, not the request body.

## Signing key (required, never committed)
1. On each server create `appSettings.secrets.config` next to `Web.config` (template: `appSettings.secrets.config.example`)
   with a unique random value of at least 32 characters, e.g. `openssl rand -base64 48`.
   Alternatively set the machine/app-pool environment variable `BILLER_AUTH_SIGNING_KEY`.
2. Without a key the API fails closed: login returns 500 and every protected endpoint returns 401.
3. Rotating the key invalidates all issued tokens (users sign in again).
4. This file is not part of the publish output; copy it deliberately. It is listed in `.gitignore`.

## Database account (replace root / empty password)
```sql
CREATE USER 'biller_api'@'<api-host>' IDENTIFIED BY '<strong-password>';
GRANT SELECT, INSERT, UPDATE ON biller_resto.* TO 'biller_api'@'<api-host>';  -- no DROP/ALTER/DELETE/GRANT
FLUSH PRIVILEGES;
```
Then change only the `BillerRestoDb` connection string in the *deployment's* `Web.config`
(or move it to a `configSource` file excluded from source control) and recycle the app pool.
Verify with `GET api/health/database` (200) and that `root` is no longer used.

## HTTPS
1. Bind a trusted certificate to the IIS site (443) or terminate TLS at a reverse proxy.
2. In the deployed `Web.config` set `Security:RequireHttps` = `true` (and `Security:TrustForwardedProto` = `true`
   only when a proxy that sets `X-Forwarded-Proto` is the sole path to the app). Plain-HTTP requests then get
   403 (deliberately not a redirect, which would resend credentials).
3. Verify: `curl -i http://host/api/categories` -> 403; `curl -i https://host/api/health/database` -> 200 with
   `Strict-Transport-Security`; `openssl s_client -connect host:443` shows a valid chain.
4. MAUI: set `ApiConfig` custom URL to the `https://` host. Remove the cleartext `domain-config` entries in
   `Platforms/Android/Resources/xml/network_security_config.xml` for release builds.

## Known limits
- `tbllogin` has no location/tenant columns, so per-user location scoping cannot be enforced; all authenticated
  users may use locations 1, 2 and 8 and read all bills (matches the current app, where Bill Reports is open to all roles).
- Role/enabled state is read at login; a demoted or deleted user keeps access until the token expires (<= 12 h).
- Most legacy `tbllogin` rows still hold non-PBKDF2 passwords and cannot log in until their passwords are reset.
- `/UploadedImages/*` (item photos) and the MVC HelpPage remain publicly readable; disable HelpPage in production.
- Login throttle is in-memory per process.

## What was removed / kept in the integration-testing simplification
**Removed (diagnostic/reporting only; no endpoint, contract, schema or authentication behaviour depended on them):**
`Data/AuditLog.cs`, `Security/AuditAttribute.cs` and the ten `[Audit(...)]` tags (attempted/succeeded/failed trail for bills, categories, items,
images, reports, logins); `Data/ConfigCheck.cs` (startup security/config report); denial and token-state logging in `ApiAuthorizeAttribute` /
`BearerTokenHandler` and the `TokenService.Validate(..., out reason)` overload; the second (audit) log channel, background queue, field allowlist,
request-count summary and per-client IP hash in `DiagLog`.
**Kept unchanged:** `AuthController` login flow (plus the `login.stages` trace), `PasswordHasher`, `TokenService.Issue/Validate`, `AuthSettings`,
`BearerTokenHandler` (token -> user, HTTPS enforcement), global `ApiAuthorizeAttribute` (401/403, admin-only routes), `LoginThrottle`, SQL
parameterisation, upload checks, `Correlation` (id in header and 5xx body), `RequestLoggingHandler` (simplified), `DiagExceptionLogger`.
**Consequence:** there is no longer a record of who created/changed which category/item/bill, nor of 401/403 denials. Restore an audit trail
before a real production release. No authentication-free mode was added.

## Tests
`dotnet test BillerRestoApi\Tests\Unit` (isolated: in-memory Web API pipeline, fake closed-port DB, and a throw-away `biller_ut_*` schema on a
LOCAL test MariaDB that each test drops and verifies; skipped as Inconclusive when no local server is reachable; no production data or
credentials): covers login success/failure/key missing/short/valid, `lastLogin` and lookup schema failures, DB connect failure, every stage trace with
the response correlation id, 401/403 enforcement, correlation header, safe 500 body, one exception line per failure with the same id, logger
hygiene (forging, truncation, secret masking, cap, retention, folder fallback, write failure never changes a response) and the `Web.config` rules.

