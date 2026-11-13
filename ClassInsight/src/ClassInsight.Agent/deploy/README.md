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
# …or point at a custom location:
.\Install-Agent.ps1 -BinaryPath 'C:\Program Files\ClassInsight\ClassInsight.Agent.exe'
```

The service is registered as `ClassInsightAgent`, **Automatic** start, running as `LocalSystem`,
with **restart-on-failure** (5s, 5s, then every 10s) so it recovers if it exits (FR-AGENT-07).

## 3. Verify / manage

```powershell
Get-Service ClassInsightAgent            # status
Get-EventLog -LogName Application -Source 'ClassInsight Agent' -Newest 20   # diagnostics
```

Under the service, diagnostics go to the Windows **Event Log** (Event Viewer → Windows Logs →
Application, source *ClassInsight Agent*) since there's no console.

## 4. Uninstall (elevated)

```powershell
.\Uninstall-Agent.ps1
```

## Mass deployment

- **GPO:** run `Install-Agent.ps1` as a *Computer → Startup* script, with the published `agent\`
  folder staged on a share or copied locally.
- **Imaging:** publish once, copy the exe into the image, and run `Install-Agent.ps1` in the
  build/sysprep step.

The same `ClassInsight.Agent.exe` also runs interactively for development (`dotnet run`): the
Windows-service wiring is a no-op unless the Service Control Manager launched the process.
