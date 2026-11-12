# ClassInsight

Real-time classroom monitoring for school computer labs — a Faronics Insight–style
system (not a clone of its code or assets). Teachers see a live grid of student
screens, plus per-machine processes and bandwidth, updated continuously.

> **Intended use.** ClassInsight is deployed by a school/IT admin on **institution-owned
> lab machines** for supervised classroom use on a local network. It is management
> software for devices the school controls — not consumer spyware. Deploy it only on
> machines you are authorized to manage, and follow your institution's acceptable-use
> and privacy policy.

## Platforms

| Component            | Runs on                         | Tech                                  |
| ------------------- | ------------------------------- | ------------------------------------- |
| **Student agent**   | Windows (lab PCs)               | .NET 10 native app + WebRTC sender    |
| **Teacher app**     | Windows, macOS, iOS, Android    | .NET MAUI + WebRTC receiver           |
| **Coordination server** | Any (the teacher machine or a lab server) | ASP.NET Core + SignalR          |

## How it fits together

```
          UDP broadcast discovery (port 47800)
   ┌──────────────────────────────────────────────────┐
   │                                                    │
┌──┴───────────┐   SignalR    ┌──────────────────┐   SignalR   ┌─────────────┐
│ Student agent │◄────────────►│  ClassInsight    │◄───────────►│ Teacher app │
│  (Windows)    │  roster +    │  server          │  roster +   │ (MAUI)      │
│               │  signalling  │  (SignalR hub)   │  signalling │             │
└──────┬────────┘              └──────────────────┘             └──────┬──────┘
       │                                                                │
       │          WebRTC H.264 screen video (peer-to-peer)             │
       └────────────────────────────────────────────────────────────►─┘
            telemetry (CPU / memory / bandwidth / processes) via hub
```

- **Discovery is automatic.** An agent broadcasts a UDP probe on launch; the server
  answers with the exact hub URL. No IP typing, no login — the agent is "absorbed"
  the moment the app opens. The server also emits periodic beacons.
- **The server is a coordinator/relay only.** It manages the roster and relays WebRTC
  SDP/ICE between a viewing teacher and a student. It never touches the video.
- **Screen video is real WebRTC H.264**, flowing peer-to-peer — continuous video, not
  periodic screenshots.
- **Telemetry** (processes, live bandwidth, CPU/memory) is a separate stream pushed
  over the hub on a fixed cadence, independent of the video.

## Project layout

```
ClassInsight/
  ClassInsight.slnx
  src/
    ClassInsight.Shared/     Wire contracts + protocol constants (all components reference this)
      Protocol.cs            Ports, hub path, method names, versioning
      Contracts/             Discovery, Roster, Signaling, Telemetry DTOs
    ClassInsight.Server/      ASP.NET Core coordination server  ← built
      Hubs/ClassroomHub.cs   Roster, WebRTC signalling relay, telemetry fan-out
      Services/
        RoomRegistry.cs      In-memory roster (no DB, no login)
        DiscoveryBeaconService.cs   UDP probe/response + beacon
        ServerIdentity.cs
      Program.cs
```

Planned next:

```
    ClassInsight.Agent/       Windows student agent (screen capture + telemetry + WebRTC sender)
    ClassInsight.Teacher/     .NET MAUI teacher app (live grid + WebRTC receiver)
```

## Run the server

```bash
cd src/ClassInsight.Server
dotnet run
```

It listens on `http://0.0.0.0:5080` (hub at `/hub/classroom`) and answers UDP
discovery on port `47800`. Configure the room name / port in `appsettings.json`:

```json
{ "ClassInsight": { "Port": 5080, "RoomName": "Computer Lab 1", "TeacherCode": "ABC123", "AllowOpenTeacherMode": false } }
```

## Security posture

- **Students auto-join with no code** (the zero-friction requirement). Becoming a
  **teacher** is gated and the server **fails closed**:
  - `TeacherCode` set → teacher apps must present it.
  - nothing set → the server **auto-generates a code and prints it to the console** at
    startup, and enforces it. Zero admin effort, no accidental open door.
  - fully open (no teacher auth) requires a deliberate
    `ClassInsight:AllowOpenTeacherMode=true`, which logs a prominent warning.
  - failed join-code attempts are **throttled and locked out** (per IP and globally),
    so the short typable code can't be brute-forced online.
- **WebRTC signalling is pair-gated.** The server only relays SDP/ICE between a
  teacher↔student pair it authorised when the teacher called `RequestStream`, so no
  client can inject signalling into arbitrary connections.
- **No CORS policy** is registered — the native clients don't need it, and a wildcard
  policy with credentials would be unsafe. Add an explicit origin allowlist only if a
  browser teacher view is introduced.

`GET /` returns live status (server id, room, connected student/teacher counts).

## Wire protocol (v1)

All components share `ClassInsight.Shared`. Key method names live in
`Protocol.ServerMethods` / `Protocol.ClientMethods`:

**Client → server:** `RegisterStudent`, `RegisterTeacher`, `GetRoster`,
`RequestStream`, `StopStream`, `SendOffer`, `SendAnswer`, `SendIceCandidate`,
`PushTelemetry`.

**Server → client:** `RosterUpdated`, `StudentJoined`, `StudentLeft`,
`StudentStateChanged`, `StreamRequested`, `StreamStopRequested`, `ReceiveOffer`,
`ReceiveAnswer`, `ReceiveIceCandidate`, `TelemetryUpdated`.

**WebRTC negotiation:** teacher calls `RequestStream(studentConnId)` → server tells
the student `StreamRequested(teacherConnId)` → student (the media sender) produces an
offer via `SendOffer` → relayed to teacher as `ReceiveOffer` → teacher `SendAnswer` →
`ReceiveAnswer` → ICE candidates exchanged both ways. Video then flows peer-to-peer.

## Status

- [x] Shared wire contracts + protocol constants
- [x] Coordination server: SignalR hub (roster, signalling relay, telemetry fan-out)
- [x] Zero-config LAN auto-discovery (UDP probe/response + beacon) — verified end-to-end
- [ ] Windows student agent — screen capture, WebRTC H.264 sender, telemetry collector
- [ ] MAUI teacher app — discovery client, live WebRTC grid, processes/bandwidth views
- [ ] Hardening: TURN/relay fallback, per-room codes, agent packaging/deployment
```
