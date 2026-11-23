# SEN-GEN — Maintenance Plan (SmarterASP.NET)

**System:** SEN-GEN (ASP.NET Core `net10.0` + React client, SQL Server) on SmarterASP.NET shared hosting
**Prepared:** 2026-11-22
**Status:** Draft. It assumes the site is already live as described in `deploymentplan.md` (same
folder). Section references like "deployment §9" point to that file.

How to keep SEN-GEN healthy, safe and up to date after go-live.

---

## 1. Roles

| Role | Responsible for |
|---|---|
| **System maintainer** (developer) | Updates, bug fixes, deployments, database backups, hosting account |
| **Super Admin** | User accounts for staff, checking the audit trail, reporting problems |
| **School Admin / Academic Head** | System parameters, terms, rooms, program setup each semester |
| **SmarterASP.NET support** | The server, SQL Server, SSL renewal, hosting outages (open a ticket) |

Write down who holds each role (deployment §3.8) and keep the hosting login in a safe, shared place
(a password manager), not in one person's head.

---

## 2. Routine schedule

### Daily (2 minutes, can be automated)
- [ ] Open `https://<site>/health`. It should say **Healthy** (this checks the app **and** the database).
- [ ] Better: let a free uptime monitor (e.g. UptimeRobot) check `/health` every 5 minutes and
      email you when it fails. This also keeps the site from going to sleep on shared hosting.

### Weekly
- [ ] **Back up the database:** Control Panel → **Databases → MSSQL → Backup**. Download the
      `.bak` file and keep it **off the server** (e.g. Google Drive). Keep at least the last 4 weekly backups.
- [ ] Check the **Audit Trail** page for anything unusual (unknown accounts, many failed sign-ins,
      large deletions).
- [ ] Open the **email outbox** page. Requeue anything that failed permanently. If users say they
      didn't get password-reset or enrollment emails, see §5 "Emails".

### Monthly
- [ ] Check **disk and database quota** in the Control Panel. Delete old `logs\stdout_*.log` files
      if stdout logging was left on.
- [ ] Check for **security updates** (see §4) and deploy them if there are any.
- [ ] Run `npm audit` in `sengensystem.client` and `dotnet list package --vulnerable` in
      `SENGENSystem.Server`. Fix anything marked high or critical.
- [ ] **Test a backup restore** once in a while. Restore a recent `.bak` into a *separate test
      database* and check that it opens. A backup you have never restored is not proven to work.
- [ ] Remove or deactivate accounts of staff who have left.

### Every semester / term
- [ ] **Before enrollment opens:** take a backup, then set up the new term, rooms, sections and
      faculty loads in the system.
- [ ] Check **System parameters** (seat cap, institution name, Program Heads) and the
      **requirements catalog** for any changes from the registrar.
- [ ] Check the curriculum/subjects for program changes from the school.
- [ ] **Do not deploy updates during enrollment week** unless it is an urgent fix (see §3).
- [ ] **After the term closes:** take a full backup and label it with the term
      (e.g. `sengen_2026-2027_1stSem.bak`) and keep it long term.

### Yearly
- [ ] **Renew the hosting plan** before it expires. The site and database are removed if
      it lapses. Turn on auto-renew or set a calendar reminder 30 days ahead.
- [ ] Renew the **domain name** (if the school uses its own domain).
- [ ] Check that the **SSL certificate** auto-renews (Let's Encrypt renews about every 90 days. Check
      the expiry date in the browser's padlock).
- [ ] **Rotate secrets:** create a new Gmail app password and a new database password, update
      `appsettings.Production.json`, and redeploy. (Changing `Jwt:Key` signs everyone out. That is safe,
      and worth doing if the old key may have leaked.)
- [ ] Plan the **.NET upgrade** (see §4).

---

## 3. Releasing an update (change process)

Every change, even a small one, follows the same path:

1. **Branch:** make the change on a feature branch, not directly on `master`.
2. **Test locally:** run the app (F5) and try the changed feature.
3. **Automated checks:** push and open a pull request. **CI must pass**. It runs the server
   tests (`dotnet test`, with a real SQL Server) and `eslint` on the client. Do not merge on red.
4. **Database changes:** if you changed an entity, add a migration:
   ```powershell
   dotnet ef migrations add <ShortName> --project SENGENSystem.Server --output-dir Common/Persistence/Migrations
   ```
   Migrations run **automatically** when the site starts (`DbInitializer`). Never edit or delete
   a migration that is already deployed. Add a new one instead.
5. **Merge** into `master`.
6. **Deploy** (deployment §9):
   1. Back up the database.
   2. `.\publish-smarterasp.ps1`
   3. Upload `app_offline.htm` → upload and unzip `publish.zip` → delete `app_offline.htm`.
7. **Smoke test** (deployment §6 Step 11): `/health`, sign-in, one report, one email.
8. **Tag the release** so you know what is live, and add a row to the release log (deployment §13):
   ```powershell
   git tag v1.2.0 ; git push --tags
   ```

**Best time to deploy:** evenings or weekends, outside enrollment and grading periods. Tell staff
beforehand if the site will be down for more than a few minutes.

---

## 4. Keeping software up to date

| Component | Check | How to update |
|---|---|---|
| .NET runtime / SDK | Monthly ("Patch Tuesday", 2nd Tuesday) | Install the latest .NET 10 SDK, update the `Microsoft.*` NuGet packages to the latest `10.0.x`, test, deploy |
| NuGet packages (EF Core, JWT, QuestPDF, ClosedXML, Swashbuckle) | Monthly | `dotnet list package --outdated`. Take patch/minor updates. Test major updates on a branch first |
| npm packages (React, Vite, ESLint) | Monthly | `npm outdated`, `npm audit`. Run `npm audit fix` for safe fixes. Major versions (e.g. React 20, Vite 9) on a branch first |
| .NET major version | Yearly | .NET 10 is an **LTS** release (supported until about **Nov 2028**). Plan the move to the next LTS (.NET 12) before then, and first check that SmarterASP supports it |
| QuestPDF license | Yearly | The project uses the **Community** license. Check that the school still qualifies under QuestPDF's current terms |

Rule: **one kind of update per release.** Don't mix a framework upgrade with new features. If
something breaks, you'll know why.

---

## 5. Monitoring and troubleshooting

### Where to look
| What | Where |
|---|---|
| Is it up? Is the database reachable? | `https://<site>/health` (app + database), `/health/live` (app only) |
| Who did what | **Audit Trail** page in SEN-GEN |
| Which emails went out or failed | **Email outbox** page in SEN-GEN (can requeue failures) |
| Server error details | Error pages show a **reference** (trace id). Turn on `stdoutLogEnabled="true"` in `web.config`, reproduce the error, search the `logs` folder for that reference, then turn logging **off** again |
| Hosting problems | SmarterASP Control Panel, support tickets, KB → "Control Panel V10" |

### Emails
SEN-GEN does not send emails straight away. It saves them in an **outbox** and a background job
sends them, retrying **5 times** (over about half an hour) before giving up. So:
- A short Gmail or network problem fixes itself. The emails go out late, but they still go out.
- If emails stop completely: check the Gmail app password (it is revoked if the Google password
  changes or 2-Step Verification is turned off), then the outbox rows in the database for errors.
- On shared hosting the app sleeps when idle, so emails can be delayed until the next visit. The
  uptime monitor in §2 prevents this.

### Common problems
See deployment §11 (Troubleshooting). Most issues are 500.30 (settings or database), emails
(Gmail app password), PDF (32-bit app pool), or "Invalid Hostname" after a domain change
(`AllowedHosts`).

---

## 6. Backup and recovery

| Item | Backup | Where kept |
|---|---|---|
| Database | Weekly + before every deploy + end of each term | Downloaded `.bak` off the server (≥ 4 weekly + every term-end) |
| Source code | Every push | GitHub repository |
| `appsettings.Production.json` (secrets) | When it changes | Password manager / encrypted storage, **never** in Git |
| Uploaded files | Not needed | SEN-GEN stores no uploads on disk. Imported `.xlsx` data lives in the database |

### Recovery steps (the site is broken after an update; same as deployment §9 "Rollback")
1. Upload `app_offline.htm` to stop the site.
2. **Code problem:** check out the previous release tag (`git checkout v1.1.0`), run
   `.\publish-smarterasp.ps1`, and upload that build.
3. **Data problem** (bad migration, deleted records): Control Panel → **MSSQL → Restore** the
   backup taken just before the deploy, then upload the previous build too (the old code must
   match the old database).
4. Delete `app_offline.htm`, run the smoke test, and tell users.
5. Write down what happened and how to stop it happening again.

**Targets:** be able to restore service within **4 hours**, and lose at most **1 week** of data (less if
you back up daily during enrollment, which is recommended).

---

## 7. Security housekeeping

- Use strong, unique passwords for the Super Admin, School Admin, hosting account, database and Gmail.
  Turn on 2-factor authentication on the hosting account, Gmail, and both SEN-GEN admin accounts
  (**Profile → Two-factor**).
- If the site address changes (new domain), update `Email:ClientBaseUrl` and `AllowedHosts` in
  `appsettings.Production.json` and redeploy. Otherwise emailed links break or requests are refused.
- Remove accounts of people who have left, **the same week** they leave.
- Never commit `appsettings.Production.json`, `.publishsettings`, `publish.zip`, or database
  backups to Git.
- Keep `stdoutLogEnabled` **off** except while debugging.
- Keep `TrustServerCertificate=False` in the connection string.
- If a secret leaks (settings file shared, laptop lost): change the database password, Gmail app
  password and `Jwt:Key` immediately, then redeploy.
- Student data is personal data under the **Data Privacy Act of 2012 (RA 10173)**. Limit who has
  admin access, keep backups private, and report any breach to the school's Data Protection Officer.

---

## 8. Handover checklist (when a new maintainer takes over)

- [ ] Access to the GitHub repository (with admin rights)
- [ ] SmarterASP.NET account login (or its own sub-account)
- [ ] Gmail sender account access
- [ ] Domain registrar / DNS access (if a custom domain is used)
- [ ] A copy of `appsettings.Production.json` handed over securely
- [ ] Location of the database backups
- [ ] Walk through `deploymentplan.md` and this plan, and do one practice deployment together
- [ ] Change the passwords the previous maintainer knew

---

## 9. Maintenance log

Record every deployment, incident and major change here (or in GitHub Releases).

| Date | Version / tag | What changed | Done by | Backup taken? | Notes |
|---|---|---|---|---|---|
| | | | | | |
