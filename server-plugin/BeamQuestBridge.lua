--[[
  BeamQuestBridge.lua — BeamMP Server Plugin + BeamNG Client Mod
  ===============================================================
  TWO roles in one file:

  1. SERVER PLUGIN (BeamMP)
     Receives vehicle states from BeamMP clients and streams them to Quest 3
     viewers via WebSocket on port 37421.

     Install: <BeamMP-Server>/Resources/Server/BeamQuestBridge/main.lua

  2. CHASE MODE AI (BeamNG client mod)
     Receives the Quest player's world position via UDP on port 37421 and
     uses BeamNG's AI system to steer the chaser vehicle toward them.
     Also handles the client snippet that sends vehicle telemetry upstream.

     Install: <BeamNG>/mods/BeamQuestBridge/ (as a BeamNG mod)

  Architecture:
    Quest 3 ──(UDP pos)──► BeamNG mod ──► AI steers vehicle
    BeamNG mod ──(UDP states)──► Quest 3 viewer

  Override ports with env vars BQ_WS_PORT, BQ_POS_PORT.
--]]

local M           = {}
local json        = require("json")
local socket      = require("socket")

-- ── Config ──────────────────────────────────────────────────────────────────
local WS_PORT     = tonumber(os.getenv("BQ_WS_PORT"))  or 37421
local POS_PORT    = tonumber(os.getenv("BQ_POS_PORT")) or 37421  -- receives player pos
local STATE_PORT  = tonumber(os.getenv("BQ_STATE_PORT")) or 37420 -- sends vehicle states
local TICK_RATE   = 1 / 20   -- 20 Hz broadcast
local MAX_CLIENTS = 8

-- ── State ───────────────────────────────────────────────────────────────────
local vehicleStates  = {}  -- [vehicleId] → state table
local playerPos      = nil -- last received player position from Quest
local posUdp         = nil -- UDP socket for receiving player position (BeamNG mod side)
local stateUdp       = nil -- UDP socket for sending states (BeamNG mod side)
local questAddr      = nil -- Quest 3 IP:port to send states to
local wsClients     = {}  -- list of connected WebSocket client sockets
local wsServer      = nil
local lastBroadcast = 0

-- ── BeamMP server hooks ──────────────────────────────────────────────────────

function M.onInit()
  MP.RegisterEvent("onVehicleSpawn",   "onVehicleSpawn")
  MP.RegisterEvent("onVehicleEdited",  "onVehicleEdited")
  MP.RegisterEvent("onVehicleDeleted", "onVehicleDeleted")
  MP.RegisterEvent("onPlayerDisconnect", "onPlayerDisconnect")

  wsServer = socket.bind("*", WS_PORT)
  if wsServer then
    wsServer:settimeout(0)
    print("[BeamQuestBridge] WebSocket server listening on :" .. WS_PORT)
  else
    print("[BeamQuestBridge] ERROR: could not bind port " .. WS_PORT)
  end
end

-- Called when a player's vehicle changes (position, rotation, etc.)
-- data is a JSON string from the client mod
function M.onVehicleEdited(playerID, vehicleID, data)
  local ok, decoded = pcall(json.decode, data)
  if not ok then return end

  local playerName = MP.GetPlayerName(playerID) or ("Player" .. playerID)
  local entry = vehicleStates[vehicleID] or {}

  entry.id    = tostring(vehicleID)
  entry.name  = playerName
  entry.model = decoded.model or (entry.model or "unknown")
  entry.px    = decoded.px or entry.px or 0
  entry.py    = decoded.py or entry.py or 0
  entry.pz    = decoded.pz or entry.pz or 0
  entry.rx    = decoded.rx or entry.rx or 0
  entry.ry    = decoded.ry or entry.ry or 0
  entry.rz    = decoded.rz or entry.rz or 0
  entry.rw    = decoded.rw or entry.rw or 1
  entry.vx    = decoded.vx or 0
  entry.vy    = decoded.vy or 0
  entry.vz    = decoded.vz or 0
  entry.spd   = decoded.spd or 0
  entry.rpm   = decoded.rpm or 0
  entry.gear  = decoded.gear or 0
  entry.fuel  = decoded.fuel or 1
  entry.thr   = decoded.thr or 0
  entry.brk   = decoded.brk or 0
  entry.str   = decoded.str or 0
  entry.dmg   = decoded.dmg or 0

  vehicleStates[vehicleID] = entry
end

function M.onVehicleSpawn(playerID, vehicleID, data)
  M.onVehicleEdited(playerID, vehicleID, data)
end

function M.onVehicleDeleted(playerID, vehicleID)
  vehicleStates[vehicleID] = nil
end

function M.onPlayerDisconnect(playerID)
  -- Remove all vehicles owned by this player
  for vid, state in pairs(vehicleStates) do
    if state.name == (MP.GetPlayerName(playerID) or "") then
      vehicleStates[vid] = nil
    end
  end
end

-- ── WebSocket broadcast ───────────────────────────────────────────────────────

local function acceptNewClients()
  if not wsServer then return end
  local client = wsServer:accept()
  if client then
    client:settimeout(0)
    table.insert(wsClients, client)
    print("[BeamQuestBridge] Quest viewer connected (" .. #wsClients .. " total)")
  end
end

local function broadcast(msg)
  local dead = {}
  for i, client in ipairs(wsClients) do
    local ok, err = pcall(function()
      client:send(msg .. "\n")
    end)
    if not ok then
      table.insert(dead, i)
    end
  end
  -- Remove dead clients in reverse order
  for i = #dead, 1, -1 do
    table.remove(wsClients, dead[i])
  end
end

function M.onUpdate(dt)
  acceptNewClients()

  if #wsClients == 0 then return end

  lastBroadcast = lastBroadcast + dt
  if lastBroadcast < TICK_RATE then return end
  lastBroadcast = 0

  local vehicles = {}
  for _, state in pairs(vehicleStates) do
    table.insert(vehicles, state)
  end

  local frame = {
    t  = os.clock(),
    vs = vehicles,
  }

  broadcast(json.encode(frame))
end

-- ── BeamNG mod entry points (when loaded as a BeamNG client mod) ─────────────

-- Called by BeamNG when the mod loads
function M.onInit()
  -- Open UDP socket to receive player position from Quest
  posUdp = socket.udp()
  posUdp:setsockname("*", POS_PORT)
  posUdp:settimeout(0)

  -- Open UDP socket to send vehicle states to Quest
  stateUdp = socket.udp()
  stateUdp:settimeout(0)

  -- Quest address: set BQ_QUEST_IP or default to broadcast
  local questIp = os.getenv("BQ_QUEST_IP") or "255.255.255.255"
  questAddr = { ip = questIp, port = STATE_PORT }

  print("[BeamQuestBridge] BeamNG mod active — Chase mode ready")
  print("[BeamQuestBridge] Listening for player pos on UDP :" .. POS_PORT)
  print("[BeamQuestBridge] Sending states to " .. questIp .. ":" .. STATE_PORT)
end

-- Called every BeamNG game tick
function M.onUpdate(dt)
  -- ── Receive player position from Quest ────────────────────────────────────
  if posUdp then
    local data, ip, port = posUdp:receivefrom()
    if data then
      local ok, decoded = pcall(json.decode, data)
      if ok and decoded then
        playerPos = decoded
        questAddr = { ip = ip, port = STATE_PORT }  -- remember Quest's IP
      end
    end
  end

  -- ── Steer AI toward player position ──────────────────────────────────────
  if playerPos then
    local chaser = be:getPlayerVehicle(0)
    if chaser then
      local ai = chaser.ai
      if ai then
        -- Set AI to chase the player's world position
        ai:setMode("span")
        ai:setTargetPosition(playerPos.px, playerPos.py, playerPos.pz)
        ai:setAggression(1.0)  -- maximum aggression
        ai:setSpeedMode("limit")
        ai:setSpeed(40)        -- 40 m/s max (~144 km/h) — terrifying
      end
    end
  end

  -- ── Broadcast all vehicle states to Quest ─────────────────────────────────
  lastBroadcast = (lastBroadcast or 0) + dt
  if lastBroadcast < TICK_RATE then return end
  lastBroadcast = 0

  if not stateUdp or not questAddr then return end

  local vehicles = {}
  local count    = be:getObjectCount()
  for i = 0, count - 1 do
    local obj = be:getObject(i)
    if obj and obj:getType() == "BeamNGVehicle" then
      local pos = obj:getPosition()
      local rot = obj:getRotation()
      local vel = obj:getVelocity()
      local el  = obj.electrics or {}

      table.insert(vehicles, {
        id    = tostring(obj:getId()),
        name  = obj:getName() or ("Vehicle" .. obj:getId()),
        model = obj.jbeam or "unknown",
        px = pos.x, py = pos.y, pz = pos.z,
        rx = rot.x, ry = rot.y, rz = rot.z, rw = rot.w,
        vx = vel.x, vy = vel.y, vz = vel.z,
        spd  = obj:getGroundSpeed() or 0,
        rpm  = el.rpmspin or 0,
        gear = el.gear_index or 0,
        fuel = el.fuel or 1,
        thr  = el.throttle or 0,
        brk  = el.brake or 0,
        str  = el.steering or 0,
        dmg  = 0,
      })
    end
  end

  local frame   = json.encode({ t = os.clock(), vs = vehicles })
  stateUdp:sendto(frame, questAddr.ip, questAddr.port)
end

return M
