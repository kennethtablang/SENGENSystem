# SEN-GEN — Deployment Plan (SmarterASP.NET)

**Target host:** SmarterASP.NET (shared Windows / IIS hosting + MS SQL Server)
**System:** ASP.NET Core `net10.0` server that also serves the built React (Vite) client from `wwwroot`,
EF Core + SQL Server, SignalR (live reports), SMTP email through Gmail, QuestPDF / ClosedXML exports,
one background service (the email outbox sender).
**Prepared:** 2026-11-22 · **Updated:** 2026-11-23 (site, temporary address and Web Deploy filled in;
settings rechecked)
**Status:** Draft. Items marked **[VERIFY]** depend on the SmarterASP plan or control panel and must be
confirmed before go-live. Items marked **[FILL IN]** are values only you can supply. Collect them in §3.

> Companion documents in this folder: `README.md` (the short 7-step version), `maintenanceplan.md`
> (what to do after go-live). The build script `publish-smarterasp.ps1` and both settings files are in this folder too.

> **About the control panel:** SmarterASP's new panel ("Control Panel V10") is behind your login, so
> these steps could not be checked screen by screen. Each step says **what** to find. If a label
> differs, look for the word in **bold** (for example "Databases", "SSL", "File Manager", "Web Deploy")
> in the side menu or search box, and use the **V10** section of the SmarterASP knowledge base, not V5.

---

## 1. Goals and scope

1. Put SEN-GEN on a public HTTPS address that STI Alaminos staff (Super Admin, School Admin,
   Academic Head, Registrar, Admission Officer, faculty) and students can reach.
2. Keep student personal data (SIS records, grades, enlistments) safe: no demo accounts, no default
   passwords, no secrets in git, regular database backups. Student data falls under the
   **Data Privacy Act of 2012 (RA 10173)**.
3. Make redeploys repeatable: every new version ships the same way, with a backup taken first and a
   way back if it fails.

Out of scope for the first release: automatic deploys from GitHub (Phase 6, optional) and a school
subdomain such as `sengen.<school-domain>` (it needs the school's IT office to add the DNS record).

---

## 2. How the system maps onto SmarterASP.NET

| System part | Where it lives on SmarterASP |
|---|---|
| ASP.NET Core server + built React client (`wwwroot`) | One IIS site, ASP.NET Core Module V2, **in-process** hosting, **64-bit** app pool |
| SQL Server database | SmarterASP MS SQL database (`SQLxxxx.site4now.net`). Tables are created by EF Core migrations on first start |
| Secrets (JWT key, Gmail app password, connection string, admin passwords) | `appsettings.Production.json`, filled in on your PC and shipped inside the zip. **Git-ignored, never committed** |
| Uploaded files | **None stored on disk.** The `.xlsx` imports (pre-enrollment list, academic records) are read in memory and saved to the database, so a database backup covers all data |
| Email | Gmail SMTP `smtp.gmail.com:587` (outbound from the host). Bulk mail goes through a database **outbox** that the `OutboxDispatcher` background service sends and retries |
| Live updates | SignalR hub `/hubs/reports`. Uses WebSockets when the host allows it, otherwise falls back to slower long polling |
| Health checks | `/health` (app + database) and `/health/live` (app only), both anonymous |
| API docs (Swagger) | **Development only.** Not served in production |
| Demo data | Off unless `Seed:DemoData=true`. Production never sets it |
| Logs | `stdout` logs in `logs\` when switched on in `web.config` (off by default) |

**What the server does on its own at startup** (`DbInitializer`): applies pending migrations, creates
the system-parameters row, creates the **Super Admin** and **School Admin** accounts if none exist,
and seeds the STI programs and subjects. The first start can take up to a minute.

---

## 3. What you need to provide (fill-in worksheet)

Fill this in before Phase 1. **Do not write passwords in this file.** Put them in a password manager
and only in `appsettings.Production.json`. The **Status** column shows what the current local
`appsettings.Production.json` has (checked 2026-11-23, values not shown).

### 3.1 Hosting account and site

| # | What | Example / format | Your value | Where it is used | Status |
|---|---|---|---|---|---|
| H1 | SmarterASP **plan** | Free 60-day trial for staging, then a paid plan with .NET 10 + MSSQL **[VERIFY]** | **Basic** | §6 Step 1 | Set. **[VERIFY]** in the panel that Basic includes an MS SQL database and lists .NET 10 for the site |
| H2 | Account owner / login email | Who owns and pays for the account | `kennethrrtablang@gmail.com` | Handover (`maintenanceplan.md` §8) | Set |
| H3 | **Site name** (as shown in the panel) | `sengen` or `site1` | `kenneth3224-002-site1` | §6 Step 1, Web Deploy | Set |
| H4 | **Temporary site address** | `http://<account>-001-site1.xxxx.smarterasp.net` | `https://kenneth3224-002-site1.ktempurl.com` | Staging, UAT, smoke tests | HTTPS works. Shows **IIS 403 Forbidden** until the first deploy, because the site folder is still empty (checked 2026-11-23) |
| H5 | Site root folder | `h:\root\home\<account>\www\site1` | `h:\root\home\kenneth3224-002\www\site1` | File Manager / FTP upload | Set. `SENGENSystem.Server.dll`, `web.config` and `wwwroot\` go **directly** in `site1`. Web Deploy targets the site by name and doesn't need this path |
| H6 | FTP host / user (password in the password manager) | `ftp.site4now.net`, `<account>-001` | Host `win1032.site4now.net`, user `kenneth3224-002` | Upload fallback (§6 Step 8) | Set. Same server as Web Deploy (H7) |
| H7 | Web Deploy service URL | `https://winXXXX.site4now.net:8172` | `https://win1032.site4now.net:8172/MsDeploy.axd?site=kenneth3224-002-site1` | §6 Option B | Set in `deploy\webdeploy.local.json`. Port 8172 reachable from the development PC (2026-11-23) |
| H8 | Web Deploy user name (password in the password manager) | `<account>` | `kenneth3224-002` | §6 Option B | Set in `deploy\webdeploy.local.json`. The script asks for the password each run |

### 3.2 Domain and HTTPS

| # | What | Example / format | Your value | Where it is used | Status |
|---|---|---|---|---|---|
| D1 | **Public address** users will type | The temp address (H4) at first, later e.g. `https://sengen.<school-domain>` | Now: `https://kenneth3224-002-site1.ktempurl.com`. Later: `https://sengen.stialaminos.edu.ph` | `Email:ClientBaseUrl`, `AllowedHosts` | `ClientBaseUrl` set to the **temp address** (no trailing slash), so emailed links work before the domain exists. Switch it to the domain in §6 Step 4 |
| D2 | Who manages the school's DNS | School IT office contact | Kenneth Tablang (account owner, H2) | §6 Step 4 | Set. Needs edit access to the DNS zone for `stialaminos.edu.ph` |
| D3 | DNS record the panel asks for | `CNAME sengen → <site>.smarterasp.net` or an `A` record | [FILL IN] | §6 Step 4 | — |
| D4 | SSL certificate | Free Let's Encrypt from the panel **[VERIFY]** | **Free** certificate from the SmarterASP panel | §6 Step 3 | The temp address already serves HTTPS (2026-11-23). Add the free certificate for `sengen.stialaminos.edu.ph` after its DNS record (D3) is in place. Free certificates renew automatically, but check the expiry date in the panel each term (`maintenanceplan.md`) |

### 3.3 Database

| # | What | Example / format | Your value | Where it is used | Status |
|---|---|---|---|---|---|
| B1 | **SQL server** | `SQL1234.site4now.net` | `sql1003.site4now.net` (SQL Server 2022) | Connection string | Set |
| B2 | **Database name** | `db_abc123_sengen` | `db_ad16f0_sengendb` (panel name `sengendb`) | Connection string | Set |
| B3 | **Database user** | `db_abc123_sengen_admin` | `db_ad16f0_sengendb_admin` | Connection string | Set. Is `db_owner`, so the first start can create the tables |
| B4 | **Database password** | (password manager) | — | Connection string | Set. **Connection tested 2026-11-23** from the development PC: encrypted, database empty |
| B5 | Certificate host name, only if you see a "trust chain" error | From SmarterASP support | Not needed | `HostNameInCertificate=` in the connection string | The server certificate verified with `TrustServerCertificate=False`. Keep it `False`, even though the panel's sample string says `True` |

### 3.4 Admin accounts (created on first start)

| # | What | Rules | Your value | Where it is used | Status |
|---|---|---|---|---|---|
| A1 | **Super Admin email** | A real mailbox, **all lowercase** (sign-in lowercases what is typed, the seeder does not) | In `appsettings.Production.json` | `Seed:SuperAdminEmail` | Set: valid address, all lowercase (checked 2026-11-23) |
| A2 | Super Admin password | Strong and unique, meets the password policy (password manager) | — | `Seed:SuperAdminPassword` | Set |
| A3 | **School Admin email** | A real mailbox, all lowercase. A **different** account from the Super Admin (A1) | [FILL IN] | `Seed:AdminEmail` | **Still not an email address, no `@` (rechecked 2026-11-23).** Fix it before the first start: the account is created only once |
| A4 | School Admin password | Strong and unique (password manager) | — | `Seed:AdminPassword` | Set |

> If a seed password is left empty, the account gets a built-in default password and must change it
> on first sign-in. Don't rely on that in production: always set both passwords.

### 3.5 Email

| # | What | Example / format | Your value | Where it is used | Status |
|---|---|---|---|---|---|
| E1 | Sender Gmail account | `noreply.classsched.stialam@gmail.com` | — | `Email:User`, `Email:FromAddress` | Set |
| E2 | **Gmail app password** (16 letters, 2-Step Verification must be on) | (password manager) | — | `Email:Password` | Set (16 characters, checked 2026-11-23). Without it SEN-GEN does **not** send email, it only writes it to the log |
| E3 | Display name on emails | `STI.SEN-GEN` (default) | [FILL IN if different] | `Email:FromName` | Default |

### 3.6 Security secrets

| # | What | How to make it | Where it is used | Status |
|---|---|---|---|---|
| S1 | **JWT signing key** | 32+ characters, random (command in §5). Never the development key from git; the server refuses to start with it | `Jwt:Key` | Set |

### 3.7 School data to enter after the first sign-in

| # | What | Who provides it | Where in SEN-GEN |
|---|---|---|---|
| R1 | Institution name, section seat cap, unit-load ceilings, time slots | School Admin | **System parameters** |
| R2 | Program Heads | School Admin / Academic Head | **System parameters** |
| R3 | Staff list: name, email, role (Academic Head, Registrar, Admission Officer, faculty) | School Admin | **User management** |
| R4 | Active school year and semester | Registrar / School Admin | **Academic setup** |
| R5 | Buildings and rooms (capacity, lecture room / computer lab) | School Admin | **Academic setup → Buildings / Rooms** |
| R6 | Curriculum changes from the seeded STI programs | Academic Head | **Curriculum** |
| R7 | Admission requirements per program | Registrar / Admission Officer | **Requirements catalog** |
| R8 | Faculty loads and preferred teaching times | Academic Head | **Faculty load** |
| R9 | Pre-enrollment list (`.xlsx`), if used | Admission Officer | **Pre-enrollment → Import** |

### 3.8 Operations contacts

| # | What | Your value |
|---|---|---|
| O1 | System maintainer (deploys, backups) | [FILL IN] |
| O2 | Email that receives uptime alerts | [FILL IN] |
| O3 | Where database backups are kept (off the server, e.g. a school Google Drive folder) | [FILL IN] |
| O4 | School Data Protection Officer | [FILL IN] |

---

## 4. Before go-live: code and configuration changes

| ID | Change | Why | State |
|---|---|---|---|
| C1 | Demo data only with `Seed:DemoData=true` | Sample accounts and passwords are in source control | **Done** |
| C2 | Server refuses a missing, short or development `Jwt:Key` outside Development | Stops forged sign-in tokens | **Done** |
| C3 | `publish-smarterasp.ps1` refuses to build while placeholders remain, and leaves out the development settings | Stops a half-filled settings file from shipping | **Done** |
| C4 | Set `AllowedHosts` in `appsettings.Production.json` to the real host names (temp address and domain, separated by `;`) | `appsettings.json` has `*`, which accepts any Host header | **Done** (2026-11-23): `kenneth3224-002-site1.ktempurl.com;sengen.stialaminos.edu.ph` |
| C5 | Add `app.UseHsts()` outside Development in `Program.cs` | The server redirects to HTTPS but sends no HSTS header, so browsers can still try plain HTTP first | **To do** (recommended) |
| C6 | Lowercase `Seed:SuperAdminEmail` / `Seed:AdminEmail` when seeding | Sign-in lowercases the typed email, so a seeded address with capitals can never sign in | **To do** (until then, type the emails in lowercase, §3.4) |

---

## 5. Production configuration reference

`deploy\appsettings.Production.json`. Copy it from `deploy\appsettings.Production.example.json`.

| Key | Value | Notes |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `Data Source=B1;Initial Catalog=B2;User Id=B3;Password=B4;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=True` | Keep `Encrypt=True` and `TrustServerCertificate=False` |
| `Jwt:Key` | S1 | 32+ random characters |
| `Seed:SuperAdminEmail` / `Seed:SuperAdminPassword` | A1 / A2 | Used only when no Super Admin exists yet |
| `Seed:AdminEmail` / `Seed:AdminPassword` | A3 / A4 | Used only when no School Admin exists yet |
| `Email:User`, `Email:FromAddress` | E1 | |
| `Email:Password` | E2 | Gmail app password, not the account password |
| `Email:ClientBaseUrl` | D1, `https://`, **no trailing slash** | Emailed links (reset password, survey) use it |
| `AllowedHosts` | `kenneth3224-002-site1.ktempurl.com;sengen.stialaminos.edu.ph` | C4. Add any new address here before pointing it at the site |

Set in `appsettings.json` already and normally left alone: `Jwt:Issuer`, `Jwt:Audience`,
`Jwt:ExpiryMinutes` (480), `Email:Host` (`smtp.gmail.com`), `Email:Port` (587), `Email:FromName`.

`ASPNETCORE_ENVIRONMENT` defaults to **Production** when it isn't set, which is what IIS on
SmarterASP does. Don't set it to Development on the server.

To generate `Jwt:Key` in PowerShell:
```powershell
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

---

## 6. Deployment steps

### Step 1: Create the site (H1–H5)
1. Log in to the SmarterASP **Control Panel** → **Websites**. A trial usually already has one site;
   otherwise **Add Site**. Write down the site name and temporary address (H3, H4).
2. In the site's settings, set the framework to **.NET Core / .NET 10** ("No Managed Code" pipeline)
   and **Enable 32-bit applications = off** (64-bit pool, needed for PDF reports). Recycle the pool
   if asked. **[VERIFY]** .NET 10 is listed. If not, use Option C below.
3. **[VERIFY]** **WebSockets** is on for the site (IIS setting). Live reports still work without it,
   only more slowly.

### Step 2: Create the database (B1–B4)
**Databases → MSSQL → Add database**, for example `sengen`. Create the user, then copy the four
values or the ready-made connection string. Don't create tables: SEN-GEN does it on first start.

### Step 3: Turn on HTTPS (D4)
**SSL** → enable the free Let's Encrypt certificate for the temp address (and the domain, after
Step 4). Test with `https://` from the start, because sign-in tokens and redirects expect it.

### Step 4: Point the domain (D1–D3). Phase 3, not needed for staging
In **Domains / Domain Pointer**, add the domain to the site. Ask the school IT office (D2) to add the
DNS record the panel shows (D3). Repeat Step 3 for the domain. Then update `Email:ClientBaseUrl` and
`AllowedHosts` and redeploy.

### Step 5: Gmail app password (E2)
Sign in to the sender account → **Google Account → Security** → turn on **2-Step Verification** →
**App passwords** → create "SEN-GEN" → copy the 16 letters.

### Step 6: Fill in `appsettings.Production.json` (§5)
Every value from §3. Fix the School Admin email (A3) **before** the first start.

### Step 7: Build the zip
From the repository root:
```powershell
.\deploy\publish-smarterasp.ps1
```
It stops on any placeholder, runs `dotnet publish` (Release, `win-x64`, framework-dependent), which
also builds the React client into `wwwroot`, checks `wwwroot\index.html`, drops the development
settings and writes **`deploy\publish.zip`** (about 24 MB) and the folder `deploy\publish\`.
With Option B below you don't need this step: the Web Deploy script runs it for you.

### Step 8: Upload (Option A, recommended)
1. **File Manager** → the site's root folder (H5). Delete the default `index.html` / `hostingstart.html`.
2. Upload `publish.zip` → **Extract** into the same folder → delete the zip.
3. Check that `SENGENSystem.Server.dll`, `web.config`, `appsettings.Production.json` and `wwwroot\`
   are **directly** in the root, not in a subfolder.

If the browser upload fails, use FTP (H6, e.g. FileZilla), then extract with the File Manager.

**Option B, Web Deploy from this PC (no zip upload):** the target is already filled in
`deploy\webdeploy.local.json` (H3, H7, H8; git-ignored). From the repository root, in your own
PowerShell window:
```powershell
.\deploy\deploy-webdeploy.ps1 -DryRun   # shows what would change, changes nothing
.\deploy\deploy-webdeploy.ps1           # builds (Step 7 checks), asks for the Web Deploy password, deploys
```
It takes the site offline during the copy (no "file in use") and leaves server-only files such as
logs alone. If it fails on the server's certificate, re-run with `-AllowUntrusted`. A 401 means a
wrong user name or Web Deploy password. Port 8172 is sometimes blocked on school networks.

*Visual Studio alternative:* panel → **Web Deploy** → download the `.publishsettings`, then
**SENGENSystem.Server → Publish → Import Profile**. That path does **not** include
`deploy\appsettings.Production.json`, so copy it to the site by hand afterwards. Never commit the
`.publishsettings` file.

**Option C, self-contained (only on HTTP 500.31, runtime missing):**
```powershell
dotnet publish .\SENGENSystem.Server -c Release -r win-x64 --self-contained true -o publish
```
Copy `deploy\appsettings.Production.json` into the publish output folder, zip the contents, upload as above (100 MB+).

### Step 9: First start
Open `https://<site>/` and wait up to a minute while it migrates and seeds. Don't keep refreshing.

### Step 10: Sign in and secure the admin accounts (§7)

### Step 11: Smoke tests

| Test | Expected |
|---|---|
| `https://<site>/health` | `Healthy` (app + database) |
| `https://<site>/health/live` | `Healthy` |
| An inner page, then **F5** | Reloads, no 404 |
| Sign in / out as School Admin and Super Admin | Works |
| **Forgot password** | Email arrives and its link opens **this site**, not localhost |
| Submit a test SIS | Saved; the student login is provisioned and its email arrives |
| Approve a test enlistment | The student's notice arrives within a minute or two |
| Download the Faculty Loading **PDF** and a semester **Excel** export | Both open |
| Open a live report in two browsers, change data in one | The other updates |
| `http://<site>` (no s) | Redirects to `https://` |
| `https://<site>/swagger` | Not found (Swagger is development-only) |

Delete the test SIS / enlistment records afterwards, or do these tests on staging only.

---

## 7. Securing the seeded accounts

- Only two accounts are created in production: **Super Admin** and **School Admin**, from `Seed:*`.
  No demo staff, faculty or students exist unless `Seed:DemoData=true`, which must never be set here.
- The seed values are used **once**. Changing them later in the settings file does nothing. Change
  passwords in **Profile → Change password**, and other people's in **User management → Reset password**.
- If anyone besides the two admins has seen `appsettings.Production.json`, change both passwords right
  after the first sign-in. The system does not force a change when the passwords came from the file.
- Turn on **two-factor sign-in** for both admin accounts (**Profile → Two-factor**).
- Create a personal account for each staff member in **User management**. Don't share the admin logins.
- After go-live you can blank the two `Seed:*Password` values in the server's copy of the file. They
  are no longer read once the accounts exist.

---

## 8. Backups and restore

| Item | When | How | Kept where (O3) |
|---|---|---|---|
| Database | Before every deploy, weekly, daily during enrollment week, end of each term | **Databases → MSSQL → Backup**, download the `.bak` | Off the server; at least 4 weekly + every term-end |
| `appsettings.Production.json` | When it changes | Copy | Password manager / encrypted storage, never git |
| Source code | Every push | GitHub, tag each release | GitHub |

No uploaded files are stored on disk (§2), so the database backup is the whole data set.

**Restore test (do it once before go-live):** restore the latest `.bak` into a second, empty test
database, point a local copy of the app at it, sign in, open a report. A backup that has never been
restored doesn't count.

---

## 9. Redeploys and rollback

**Redeploy:**
1. Back up the database (§8).
2. `.\deploy\publish-smarterasp.ps1`
3. Upload an empty **`app_offline.htm`** to the site root, which stops the app and releases files.
4. Upload and extract the new `publish.zip` over the old files.
5. Delete `app_offline.htm`. New migrations run automatically on start.
6. Run the smoke tests (§6 Step 11). Record the release (§13). Tag it: `git tag vX.Y.Z; git push --tags`.

Deploy in the evening or at weekends, **never during enrollment week** unless it is an urgent fix.

**Rollback:**
1. Upload `app_offline.htm`.
2. Check out the previous tag, run the script, upload that build.
3. If the new version ran a migration, also **restore the pre-deploy backup**. Old code must run
   against the old database.
4. Delete `app_offline.htm`, run the smoke tests, tell users.

---

## 10. Monitoring

- **Uptime monitor** (e.g. UptimeRobot, free): check `https://<site>/health` every 5 minutes and
  alert O2. This also keeps the shared-hosting app pool from going idle, which would otherwise delay
  outbox emails until the next visit. **[VERIFY]** whether the plan offers "Always running" instead.
- **Errors:** error pages show a **reference** (trace id). To find it: set `stdoutLogEnabled="true"` in
  the server's `web.config`, reproduce, search `logs\` for the reference, then set it back to `false`.
- **Who did what:** the **Audit Trail** page.
- **Email:** the **email outbox** page shows sent and failed mail and can requeue failures.
- **Quota:** check disk and database usage in the panel monthly.

---

## 11. Troubleshooting

| Problem | Likely cause / fix |
|---|---|
| **HTTP 500.30** on every page | Startup failed: wrong connection string, or `Jwt:Key` short / the development key. Turn on stdout logs (§10) |
| **HTTP 500.31 / 500.32** | .NET 10 runtime missing or 32/64-bit mismatch. Fix Step 1, or use Option C |
| **HTTP 500.19** | Broken `web.config` or missing hosting module. Re-upload; contact support if it persists |
| **HTTP 400 "Bad Request – Invalid Hostname"** | The address used isn't in `AllowedHosts` (C4) |
| **403 / default SmarterASP page** | Files are in a subfolder. Move them up to the root (Step 8.3) |
| **"certificate" / "trust chain"** error on the database | Get the certificate host name from support and add `HostNameInCertificate=<name>` (B5). **Don't** set `TrustServerCertificate=True` |
| **Login failed for user 'db_…'** | Wrong DB user/password, or the user isn't mapped to the database |
| Admin can't sign in with the seeded email | The seeded email had capitals or isn't a valid email (§3.4, C6). Fix it directly in the `Users` table, or delete the row and restart so it is re-seeded |
| Inner pages **404 on refresh** | Incomplete upload. `wwwroot\index.html` must exist |
| **No emails** at all | `Email:Password` empty (mail is only logged), wrong app password, or outbound SMTP 587 blocked **[VERIFY]** with support |
| Emails arrive late | Idle app pool. See §10 |
| **PDF downloads fail** | App pool is 32-bit. Switch to 64-bit |
| Emailed links open `localhost` | `Email:ClientBaseUrl` not changed |
| Live reports don't update | WebSockets off and long polling blocked. Check Step 1.3 |
| Slow first visit after a quiet period | Normal on shared hosting. The uptime monitor (§10) prevents it |

---

## 12. Rollout phases

| Phase | What | Output |
|---|---|---|
| 0. Prepare | Fill in §3, choose the plan (H1), make changes C4–C6, generate secrets | Repo ready, secrets stored safely |
| 1. Staging on the temp URL | Steps 1–3 and 5–11 of §6 on the temporary address | Working site on the temp URL |
| 2. UAT | Registrar, Admission Officer, Academic Head and a few students test real flows: SIS, documents, scheduling, enlistment, reports | Signed-off issue list |
| 3. Domain + SSL | Step 4, update `AllowedHosts` and `ClientBaseUrl`, redeploy, rerun smoke tests | Production URL live |
| 4. Data load | Clear the UAT test records (or start a fresh database), enter §3.7 | Production data in place |
| 5. Go-live | Announce the URL to staff and students, watch logs, uptime and the outbox for the first week | Live system |
| 6. Optional | GitHub Actions deploy job (Windows runner + `msdeploy`, publish profile as a repo secret) after the existing CI passes | One-click deploys |

---

## 13. Release log

| Date | Version / tag | Migration? | Backup taken? | Deployed by | Smoke tests | Notes |
|---|---|---|---|---|---|---|
| | | | | | | |

---

## 14. Go-live checklist

- [ ] Every [FILL IN] in §3 has a value; passwords are in the password manager
- [ ] `ASPNETCORE_ENVIRONMENT` is not Development (Swagger off, demo data off)
- [ ] `Seed:DemoData` is not set
- [ ] New production `Jwt:Key`, not the development key
- [ ] Database placeholders replaced; `Encrypt=True`, `TrustServerCertificate=False`
- [ ] School Admin and Super Admin emails are real, lowercase addresses
- [ ] `Email:Password` is the Gmail app password, and a test email has been received
- [ ] `Email:ClientBaseUrl` is the public HTTPS address
- [ ] `AllowedHosts` lists only the real host names (C4)
- [ ] HSTS added (C5)
- [ ] Both admin passwords changed or confirmed private; 2FA on for both (§7)
- [ ] SSL active, and HTTP → HTTPS redirect works
- [ ] App pool 64-bit; PDF and Excel exports work
- [ ] WebSockets on; live reports update
- [ ] Uptime monitor on `/health`
- [ ] Database backup taken and a restore tested once (§8)
- [ ] `stdoutLogEnabled` is `false`
- [ ] `appsettings.Production.json`, `publish.zip` and `.publishsettings` are **not** in git

---

## References

- SmarterASP KB, supported .NET Core versions: https://www.smarterasp.net/support/kb/a1986/supported-versions-of-_net-core.aspx
- SmarterASP KB, publish .NET 10 self-contained (VS 2026): https://www.smarterasp.net/support/kb/a2320/core-to-core-how-to-publish-_net-10-with-self-contained-mode-in-visual-studio-2026.aspx
- SmarterASP KB index (use **Control Panel V10**): https://www.smarterasp.net/support/kb/root.aspx
