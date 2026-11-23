# Deploying SEN-GEN on SmarterASP.NET

Seven steps. The code is already prepared for this: demo accounts are switched off outside
development, and one script builds the upload file.

---

## Step 1: Create the site and database on SmarterASP

In the SmarterASP **Control Panel**:

1. **Websites → Add site.** In the site's settings, set the framework to **.NET 10 (ASP.NET Core)**
   and the application pool to **64-bit** (the PDF reports need 64-bit).
2. **Databases → MSSQL → Add database.** Write down the four values it shows: **server**,
   **database name**, **user**, **password**.
3. **SSL:** turn on the free SSL certificate for the site's address.

> Tip: do everything on the free trial first. Move to a paid plan once it works.

---

## Step 2: Get a Gmail app password

SEN-GEN sends email from `noreply.classsched.stialam@gmail.com`. Gmail won't accept the normal
password from a server, so:

1. Sign in to that Gmail account → **Google Account → Security**.
2. Turn on **2-Step Verification** (if it's off).
3. Open **App passwords**, create one called "SEN-GEN", and copy the 16-letter password.

---

## Step 3: Fill in the settings file

1. In this `deploy\` folder, copy **`appsettings.Production.example.json`** and name the copy
   **`appsettings.Production.json`**.
2. Replace every placeholder:

| Setting | What to put |
|---|---|
| `DefaultConnection` | The **server, database, user, password** from Step 1 |
| `Jwt:Key` | Any long random text, 40+ characters (a password manager can generate one). Keep it secret |
| `SuperAdminEmail` / `SuperAdminPassword` | The Super Admin's real email, and a strong password |
| `AdminEmail` / `AdminPassword` | The School Admin's real email, and a strong password |
| `Email:Password` | The Gmail app password from Step 2 |
| `Email:ClientBaseUrl` | The site's address, e.g. `https://sengen.yourschool.edu.ph` (used in emailed links) |

This file holds the passwords, so **git ignores it**. It never goes into the repository.

---

## Step 4: Build the upload file

Open PowerShell in the repository folder and run:

```powershell
.\deploy\publish-smarterasp.ps1
```

It builds the server and the web client together and produces **`deploy\publish.zip`** (about 24 MB).
If any placeholder from Step 3 is still in the settings file, it stops and lists which fields are
left to fill in.

---

## Step 5: Upload

1. In the Control Panel, open the site's **File Manager**.
2. Upload `publish.zip` into the site's root folder.
3. Right-click it → **Unzip**, then delete the zip.

(Visual Studio's **Publish → Import profile** with the profile file from the Control Panel also
works, if you prefer that.)

### Or: one-click Publish from Visual Studio

Set up once:

1. Control Panel → your site → **Web Deploy** → download the **`.PublishSettings`** file.
2. In Visual Studio, right-click **SENGENSystem.Server** → **Publish** → **Add a publish profile**
   → **Import Profile** → choose the file → **Finish**.
3. Click **Publish**. When asked, enter the Web Deploy password and tick **Save password**.

After that, whenever there are changes: right-click **SENGENSystem.Server** → **Publish** → **Publish**.

The project includes `deploy\appsettings.Production.json` automatically, takes the site offline
during the copy, and refuses to publish if the settings file is missing or still has placeholders.
After editing the settings file, run `.\deploy\publish-smarterasp.ps1` once: it checks more (JWT key,
admin emails). Database changes apply on the site's next start.

### Or: publish straight from this PC with Web Deploy (no zip)

1. Control Panel → your site → **Web Deploy**: enable it and set its password.
2. Copy `deploy\webdeploy.example.json` to `deploy\webdeploy.local.json` and fill in the service URL,
   site name, user name, and site address from that page. It's git-ignored. Never put the password
   in it.
3. From the repository root, try a dry run first (changes nothing on the server):

   ```powershell
   .\deploy\deploy-webdeploy.ps1 -DryRun
   ```

4. Then publish for real:

   ```powershell
   .\deploy\deploy-webdeploy.ps1
   ```

It builds with the same checks as Step 4, asks for the Web Deploy password, and takes the site
offline during the copy so no file is "in use". Files that exist only on the server (logs) are
left alone. If it fails on the server's certificate, run it again with `-AllowUntrusted`. Port 8172
is sometimes blocked on school networks, so if it can't connect, try another network.

---

### Optional: create the tables yourself in SSMS first

The site creates its own tables on the first start. If that first start times out, or you'd rather
watch it happen, run `deploy\migrate.sql` in **SSMS 2022** before Step 6:

1. Connect to the database server with **SQL Server Authentication** (server, login and password
   from SmarterASP → **Databases → MSSQL**). On **Connection Properties**, set Encryption to
   **Mandatory** and leave "Trust server certificate" unticked.
2. Select your database in the toolbar dropdown. The script has no `USE` line.
3. Open `deploy\migrate.sql` and press **Execute** (F5).

It's safe to run more than once, and it does nothing if the site already created the tables. It
creates tables only. The two admin accounts and the STI programs are still added by the site on
its first start. After adding a new migration, regenerate it with `.\deploy\make-migrate-sql.ps1`.

---

## Step 6: Open the site and sign in

1. Browse to the site. The first visit takes up to a minute while it sets up the database
   (tables, the STI programs and subjects, and the admin accounts).
2. Sign in as the **Super Admin** with the password from Step 3. The system does **not** ask for a
   new one, so change it from **Profile → Change password** if anyone else has seen the settings
   file.
   - **No School Admin yet?** If `Seed:AdminEmail` was left as `""`, none is created. When you've
     chosen the person, the Super Admin adds them in **User management** with the role
     **School Admin**. Until then the Super Admin can do everything a School Admin can.
3. In **System parameters**, set the institution name, seat cap, and Program Heads.
4. Add the real staff in **User management**, then set up the term, buildings, and rooms.

A fresh install has **no** demo staff, students, or rooms. Everything is entered for real.

---

## Step 7: Quick check

| Try this | Expected |
|---|---|
| Open `https://<site>/health` | Shows `Healthy` |
| Refresh the browser on any inner page | The page loads (not a 404) |
| **Forgot password** on the sign-in page | The email arrives, and its link opens your site |
| Download a faculty-loading **PDF** | It opens |
| Approve a test enlistment | The student gets the email within a minute or two |

If all five work, the system is live.

---

## Updating later

1. **Back up the database** (Control Panel → MSSQL → Backup).
2. Run `.\deploy\deploy-webdeploy.ps1`. That's all.
   Or, without Web Deploy: run `.\deploy\publish-smarterasp.ps1`, then in the File Manager upload
   and unzip `publish.zip` over the old files. (If a file is "in use", first upload an empty file
   named `app_offline.htm`, then unzip, then delete `app_offline.htm`.)

Database changes apply automatically on the next start.

---

## If something goes wrong

| Problem | Likely cause |
|---|---|
| **500.30 error** on every page | A wrong value in `appsettings.Production.json`: usually the database connection, or a `Jwt:Key` shorter than 32 characters |
| Error mentions **"certificate"** or **"trust chain"** when connecting to the database | The database server's certificate name doesn't match the server address. Ask SmarterASP support for the certificate's host name and add `HostNameInCertificate=<that name>` to the connection string. Don't switch to `TrustServerCertificate=True`: that turns off the check that you're talking to the real database server |
| Site works, but **no emails** | Wrong Gmail app password, or SmarterASP blocks Gmail. Ask support whether outgoing SMTP to `smtp.gmail.com` on port 587 is allowed |
| **PDF downloads fail**, everything else works | The application pool is 32-bit. Switch it to 64-bit (Step 1) |
| Emails arrive **late** after a quiet period | SmarterASP put the idle site to sleep. Ask support to set the pool to "Always running", or use a free uptime monitor (e.g. UptimeRobot) to visit `/health` every 5 minutes |

To see the exact error: in the site's `web.config`, change `stdoutLogEnabled="false"` to `"true"`,
reload the page, read the file in the `logs` folder, then set it back to `"false"`.
