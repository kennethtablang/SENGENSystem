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

1. In `SENGENSystem.Server\`, copy **`appsettings.Production.example.json`** and name the copy
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
.\publish-smarterasp.ps1
```

It builds the server and the web client together and produces **`publish.zip`** (about 24 MB).
If any placeholder from Step 3 is still in the settings file, it stops and lists which fields are
left to fill in.

---

## Step 5: Upload

1. In the Control Panel, open the site's **File Manager**.
2. Upload `publish.zip` into the site's root folder.
3. Right-click it → **Unzip**, then delete the zip.

(Visual Studio's **Publish → Import profile** with the profile file from the Control Panel also
works, if you prefer that.)

---

## Step 6: Open the site and sign in

1. Browse to the site. The first visit takes up to a minute while it sets up the database
   (tables, the STI programs and subjects, and the two admin accounts).
2. Sign in as the **School Admin** and the **Super Admin** with the passwords from Step 3.
   The system does **not** ask for a new one, so change each from **Profile → Change password**
   if anyone else has seen the settings file.
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
2. Run `.\publish-smarterasp.ps1` again.
3. In the File Manager, upload and unzip `publish.zip` over the old files.
   (If a file is "in use", first upload an empty file named `app_offline.htm`, then unzip, then
   delete `app_offline.htm`.)

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
