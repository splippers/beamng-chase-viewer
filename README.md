# beamng-chase-viewer
### A VR horror-chase experience powered by BeamNG.drive telemetry

> "You hear the engine long before you understand you're the reason it woke up."

---

## What This Is

`beamng-chase-viewer` turns your Meta Quest into a place you should not be.

BeamNG runs on your PC.  
Your headset listens.  
And somewhere in the dark, something impossibly heavy begins to move.

This is not a game.  
It is an encounter.

---

## The Premise

You stand alone in a dim, half-finished world.  
The kind of place where the air tastes like dust and old memories.  
Then the telemetry link snaps alive.

A truck stirs inside BeamNG.  
It knows where you are.  
It knows how fast you're moving.  
And it is coming.

---

## How It Works

1. **BeamNG Telemetry Hook**  
   A Lua module streams vehicle position, velocity, and orientation over UDP/WebSocket.  
   Like a heartbeat. A very large, very angry heartbeat.

2. **Quest VR Client** (native .NET 8 + Vulkan + OpenXR — no Unity overhead)  
   Renders a fog-drenched environment and places you inside it.  
   The chaser appears exactly where BeamNG says it should be.  
   No mercy. No rubber-banding. Just physics.

3. **You**  
   You run.  
   You fall.  
   You hear metal breathing behind you.

---

## Modes

| Mode | What it does |
|---|---|
| **Chase** | You're on foot in VR; a BeamNG vehicle hunts you in real time |
| **Spectator** | Free-fly observer — watch any session from above, cycle targets |
| **Cockpit** | Sit inside the vehicle with head-tracked camera + telemetry HUD |
| **Replay** | Play back a `.beamrec` recording with speed control and scrubbing |

---

## Architecture

```
beamng-chase-viewer/
├── server-plugin/BeamQuestBridge.lua   ← BeamMP server plugin / BeamNG Lua mod
├── src/
│   ├── Protocol/                       ← swappable data sources
│   │   ├── IVehicleDataSource.cs
│   │   ├── VehicleState.cs             ← WorldFrame, VehicleState, OutGaugePacket
│   │   ├── BeamNGUDPSource.cs          ← BeamNG Lua mod → UDP stream
│   │   ├── BeamMPWebSocketSource.cs    ← BeamMP server plugin → WebSocket
│   │   ├── OutGaugeSource.cs           ← BeamNG built-in OutGauge UDP
│   │   ├── ReplaySource.cs             ← .beamrec JSON-Lines playback
│   │   └── ReplayRecorder.cs           ← tees any source to file
│   ├── World/
│   │   ├── VehicleManager.cs           ← vehicle table + interpolation
│   │   ├── VehicleMeshBuilder.cs       ← procedural meshes (upgradeable to OBJ)
│   │   └── TerrainLoader.cs            ← heightmap PNG / raw32
│   ├── Modes/
│   │   ├── SpectatorMode.cs
│   │   ├── CockpitMode.cs
│   │   └── ReplayMode.cs
│   ├── UI/
│   │   ├── TelemetryHUD.cs
│   │   └── UIManager.cs
│   └── XR/ + Rendering/ + Core/       ← shared Vulkan layer with SLQuest
└── BeamQuestViewer.csproj              ← .NET 8, Silk.NET, no Unity
```

Stack: native .NET 8 Android + Silk.NET (OpenXR + Vulkan) — same layer as the
SLQuest Second Life viewer. No Unity, no engine overhead, runs lean on Quest 3.

---

## Data Sources

### BeamNG UDP mod (recommended — best for Chase mode)
1. Add the client-side snippet from `server-plugin/BeamQuestBridge.lua` to your BeamNG mod
2. Run BeamNG, load a level
3. Quest app listens on UDP :37420 by default

### BeamMP server plugin (multiplayer spectator)
1. Copy `server-plugin/BeamQuestBridge.lua` to `<BeamMP-Server>/Resources/Server/BeamQuestBridge/main.lua`
2. WebSocket stream on port 37421
3. Set env `BQ_SOURCE=ws BQ_HOST=<server-ip>`

### OutGauge (built-in, single vehicle, telemetry only)
1. BeamNG: Options → Other → OutGauge → enable, port 4444
2. Set `BQ_SOURCE=outgauge` — speed/RPM/gear but no world position

### Replay
Set `BQ_SOURCE=replay BQ_REPLAY=/path/to/file.beamrec`  
Record any live source with `BQ_RECORD=1`.

---

## Features
- Real-time vehicle tracking with client-side interpolation
- VR chase mode with positional audio
- Threat profiles (trucks, buses, cursed forklifts)
- Spectator / Cockpit / Replay modes
- Latency smoothing for fluid terror

---

## Roadmap
- [ ] Chase mode: locomotion + collision / impact response
- [ ] Multiplayer "shared nightmare" mode
- [ ] Procedural fog-town generator (BeamNG Flatland as the arena)
- [ ] Hide-and-seek with audio cues
- [ ] Haptics on impact
- [ ] Full BeamNG environment streaming (terrain + props)
- [ ] Threat profile selector (truck, bus, cursed forklift)

---

## Controls (Quest 3 — Spectator/Cockpit)

| Input | Action |
|---|---|
| Right thumbstick | Fly forward/back/strafe |
| Left thumbstick | Yaw turn |
| A | Cycle follow target |
| B | Release follow |
| Left thumbstick click | Toggle wrist menu |

---

## Coordinate System

BeamNG uses right-handed Y-up — same as OpenXR. No axis swap needed.

---

## Warnings

This project may cause:
- Sudden sprinting
- Uncontrolled swearing
- The belief that something heavy is behind you
- The urge to never turn around

If you hear the engine when the headset is off...  
...that's on you.

---

## License

MIT — because fear should be open-source.  
Not affiliated with BeamNG GmbH or the BeamMP Team.  
BeamNG.drive® is a registered trademark of BeamNG GmbH.
