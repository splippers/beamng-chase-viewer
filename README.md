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
   A Lua module streams vehicle position, velocity, and orientation over WebSocket.  
   Like a heartbeat. A very large, very angry heartbeat.

2. **Quest VR Client**  
   Unity/Unreal renders a fog-drenched environment and places you inside it.  
   The chaser appears exactly where BeamNG says it should be.  
   No mercy. No rubber-banding. Just physics.

3. **You**  
   You run.  
   You fall.  
   You hear metal breathing behind you.

---

## Project Structure
```text
beamng-chase-viewer/
│
├── beamng-bridge/        # Lua telemetry mod for BeamNG
├── quest-app/            # Meta Quest VR client
├── docs/                 # Setup, diagrams, API notes
├── proto-scenes/         # Early horror testbeds
└── tools/                # Debuggers, sniffers, utilities
```

---

## Features
- Real-time vehicle tracking  
- VR chase mode with positional audio  
- Threat profiles (trucks, buses, cursed forklifts)  
- Impact Cam  
- Latency smoothing for fluid terror  

---

## Roadmap
- [ ] Multiplayer "shared nightmare" mode  
- [ ] Procedural fog-town generator  
- [ ] Hide-and-Seek with audio cues  
- [ ] Haptics on impact  
- [ ] Full BeamNG environment streaming  

---

## Documentation
- Setup Guide  
- Telemetry API  
- Quest Build Instructions  

(Links to be added when docs are created.)

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
MIT -- because fear should be open-source.
