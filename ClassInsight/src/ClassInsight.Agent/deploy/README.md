# Deploying the ClassInsight agent to lab PCs

The agent runs as an **auto-start Windows service** (FR-AGENT-02). Once installed it starts at
boot, before any student logs in, finds the classroom server over the LAN (UDP discovery), and
joins with no code and no per-machine configuration.

> Deploy only on institution-owned machines you are authorized to manage, on the lab network.
> See `plan.md` for the deployment and trust model.

## 1. Publish a self-contained build

From the `ClassInsight.Agent` project folder (produces an exe with no .NET install required on
the lab PC):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o .\deploy\agent
```

This drops `ClassInsight.Agent.exe` into `deploy\agent\`, where `Install-Agent.ps1` looks by default.

## 2. Install the service (elevated)

```powershell
# Run from an Administrator PowerShell
.\Install-Agent.ps1
# ...or install from a specific published build:
.\Install-Agent.ps1 -BinaryPath 'C:\build\agent\ClassInsight.Agent.exe'
```

The installer **copies the agent into `%ProgramFiles%\ClassInsight`** (override with `-InstallDir`)
and **locks that folder's ACL** - Administrators/SYSTEM get full control, standard Users get only
read & execute - then registers the service to run from there. This matters: the service runs as
`LocalSystem`, so if the binary lived in a student-writable folder a student could swap the exe and
run code as SYSTEM at boot. The installer **refuses to proceed if the installed binary is writable
by non-admins**.

The service is registered as `ClassInsightAgent`, **Automatic** start, running as `LocalSystem`,
with **restart-on-failure** (5s, 5s, then every 10s) so it recovers if it exits (FR-AGENT-07).

### How it runs

Windows services live in **session 0**, which can't see the student's desktop. So the service is
a small **supervisor**: it launches the same `ClassInsight.Agent.exe --session-agent` *inside the
active console session* (the signed-in student's desktop, `winsta0\default`), running as SYSTEM.
That session agent does the discovery, telemetry and screen streaming. The supervisor:

- relaunches it within seconds if it exits, and moves it when the console user changes
  (log off / switch user);
- holds it in a kill-on-close job object, so it can never outlive the service;
- runs it as SYSTEM, so a standard student can't end it from Task Manager (FR-AGENT-07).

At the Windows logon screen there is no user desktop to capture; the agent stays registered
(telemetry continues) and the stream resumes once someone signs in.

### Optional per-lab settings

`appsettings.json` next to the installed exe (none are needed on a normal lab LAN):

| Setting | Use |
| ------- | --- |
| `ClassInsight:RoomName` | Join only the server with this room name, when several rooms share one network. |
| `ClassInsight:ServerAddress` | `host` or `host:port` to try when UDP broadcast discovery is blocked. |
| `ClassInsight:ReportBrowserActivity` | Report the foreground browser tab's **host** (domain only). Default `true`. |

The agent's identity is a random per-install id kept in `%ProgramData%\ClassInsight\agent-id`
(the installer protects that folder).

## 3. Verify / manage

```powershell
Get-Service ClassInsightAgent            # status
Get-EventLog -LogName Application -Source 'ClassInsight Agent' -Newest 20   # diagnostics
```

Under the service, diagnostics go to the Windows **Event Log** (Event Viewer â†’ Windows Logs â†’
Application, source *ClassInsight Agent*) since there's no console.

## 4. Uninstall (elevated)

```powershell
.\Uninstall-Agent.ps1            # removes the service and %ProgramFiles%\ClassInsight
.\Uninstall-Agent.ps1 -KeepFiles # removes only the service, leaves the files
```

## Mass deployment

- **GPO:** run `Install-Agent.ps1` as a *Computer â†’ Startup* script, with the published `agent\`
  folder staged on a share or copied locally.
- **Imaging:** publish once, copy the exe into the image, and run `Install-Agent.ps1` in the
  build/sysprep step.

The same `ClassInsight.Agent.exe` also runs interactively for development (`dotnet run`): the
Windows-service wiring is a no-op unless the Service Control Manager launched the process, and
the agent then monitors the current session directly.
