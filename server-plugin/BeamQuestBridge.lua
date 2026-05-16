--[[
  BeamQuestBridge.lua — BeamMP Server Plugin
  ============================================
  Streams live vehicle states to connected BeamQuest viewers via WebSocket.

  Install:
    Copy this file to <BeamMP-Server>/Resources/Server/BeamQuestBridge/main.lua

  This plugin runs on the BeamMP SERVER.  It receives vehicle-state packets
  from all connected clients and re-broadcasts them as JSON to any WebSocket
  listener (your Quest 3 running BeamQuestViewer).

  Architecture:
    BeamNG game → BeamMP client mod → BeamMP server → [this plugin] → Quest 3

  The WebSocket server listens on port 37421 by default.
  Override with env var BQ_WS_PORT.
--]]

local M           = {}
local json        = require("json")
local socket      = require("socket")

-- ── Config ──────────────────────────────────────────────────────────────────
local WS_PORT     = tonumber(os.getenv("BQ_WS_PORT")) or 37421
local TICK_RATE   = 1 / 20   -- 20 Hz broadcast
local MAX_CLIENTS = 8

-- ── State ───────────────────────────────────────────────────────────────────
local vehicleStates = {}  -- [vehicleId] → state table
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

-- ── BeamNG client-side Lua helper (attach to vehicle via mod) ────────────────
--[[
  The server only receives what the client sends.  To stream full telemetry
  (rpm, fuel, etc.) you also need a BeamNG client mod that sends these values.

  Drop this snippet in your BeamNG mod's onUpdate:

    local function onUpdate(dt)
      local v = be:getPlayerVehicle(0)
      if not v then return end
      local pos = v:getPosition()
      local rot = v:getRotation()
      local vel = v:getVelocity()
      local electrics = v.electrics

      local data = jsonEncode({
        model = v.jbeam,
        px=pos.x, py=pos.y, pz=pos.z,
        rx=rot.x, ry=rot.y, rz=rot.z, rw=rot.w,
        vx=vel.x, vy=vel.y, vz=vel.z,
        spd = v:getGroundSpeed(),
        rpm = electrics.rpmspin or 0,
        gear = electrics.gear_index or 0,
        fuel = electrics.fuel or 1,
        thr  = electrics.throttle or 0,
        brk  = electrics.brake or 0,
        str  = electrics.steering or 0,
        dmg  = v:getDamage() or 0,
      })
      -- BeamMP will relay this to the server's onVehicleEdited
      TriggerServerEvent("BeamQuestBridge:vehicleUpdate", data)
    end
--]]

return M
