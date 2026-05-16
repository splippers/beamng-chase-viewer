--[[
  BeamQuest Bridge — BeamNG.drive GE Extension
  =============================================
  Streams vehicle states to the BeamQuest VR viewer on Meta Quest 3.
  Receives the viewer's player position and steers the AI chaser toward it.

  Configuration (via environment variables or edit defaults below):
    BQ_QUEST_IP   — Quest 3 IP address (default: 255.255.255.255 broadcast)
    BQ_STATE_PORT — UDP port to send states to Quest (default: 37420)
    BQ_POS_PORT   — UDP port to receive player position from Quest (default: 37421)

  Usage:
    1. Start BeamNG, load a level (Gridmap V2 or Flatland recommended)
    2. Spawn a vehicle you want to be the chaser
    3. Enable AI: Esc → AI → Chase (or this extension enables it automatically)
    4. Start BeamQuest Viewer on Quest 3 in Chase mode
    5. The AI will begin targeting the player's VR position
--]]

local M = {}

-- ── Configuration ────────────────────────────────────────────────────────────
local CONFIG = {
  questIp    = os.getenv("BQ_QUEST_IP")    or "255.255.255.255",
  statePort  = tonumber(os.getenv("BQ_STATE_PORT")) or 37420,
  posPort    = tonumber(os.getenv("BQ_POS_PORT"))   or 37421,
  sendHz     = 20,    -- vehicle state broadcast rate
  aiSpeed    = 35,    -- chaser max speed m/s (~126 km/h)
  aiAggression = 1.0, -- 0-1 BeamNG AI aggression
}

-- ── State ─────────────────────────────────────────────────────────────────────
local socket      = require("socket")
local stateSocket = nil  -- sends vehicle states → Quest
local posSocket   = nil  -- receives player pos ← Quest

local playerPos   = nil  -- last known Quest player position {x,y,z}
local lastSendTime = 0
local sendInterval = 1.0 / CONFIG.sendHz
local chaserId    = nil  -- vehicle ID designated as the chaser

local log = function(msg) print("[BeamQuestBridge] " .. tostring(msg)) end

-- ── Lifecycle ─────────────────────────────────────────────────────────────────

function M.onInit()
  -- Send socket (vehicle states → Quest)
  stateSocket = socket.udp()
  stateSocket:settimeout(0)
  stateSocket:setsockname("*", 0)  -- OS assigns ephemeral port

  -- Receive socket (player pos ← Quest)
  posSocket = socket.udp()
  posSocket:settimeout(0)
  local ok, err = posSocket:setsockname("*", CONFIG.posPort)
  if not ok then
    log("ERROR binding pos socket on :" .. CONFIG.posPort .. " — " .. tostring(err))
  end

  log("Initialized")
  log("  Sending states → " .. CONFIG.questIp .. ":" .. CONFIG.statePort)
  log("  Listening for player pos on :" .. CONFIG.posPort)
end

function M.onExtensionLoaded()
  M.onInit()
end

-- ── Per-frame update ──────────────────────────────────────────────────────────

function M.onUpdate(dtReal, dtSim, dtRaw)
  receivePlayerPosition()
  updateChaserAI()

  lastSendTime = lastSendTime + dtReal
  if lastSendTime < sendInterval then return end
  lastSendTime = 0

  broadcastVehicleStates()
end

-- ── Receive player position from Quest ───────────────────────────────────────

local function receivePlayerPosition()
  if not posSocket then return end
  local data, senderIp = posSocket:receivefrom()
  if not data then return end

  local ok, decoded = pcall(jsonDecode, data)
  if ok and decoded and decoded.px then
    playerPos = { x = decoded.px, y = decoded.py, z = decoded.pz }
    -- Remember sender IP so we can send states back to their actual address
    if senderIp and senderIp ~= "0.0.0.0" then
      CONFIG.questIp = senderIp
    end
  end
end

-- expose as module-level so onUpdate can call it without closure overhead
M.receivePlayerPosition = receivePlayerPosition

-- ── Steer AI chaser ───────────────────────────────────────────────────────────

local function updateChaserAI()
  if not playerPos then return end

  -- Find or designate the chaser vehicle
  if not chaserId then
    -- Use the first player-owned vehicle
    local v = be:getPlayerVehicle(0)
    if v then chaserId = v:getId() end
  end
  if not chaserId then return end

  local chaser = be:getObjectByID(chaserId)
  if not chaser then chaserId = nil; return end

  local ai = chaser:getController("ai")
  if not ai then return end

  ai:setMode("flee")         -- "flee" mode in reverse = chase; override below
  ai:setMode("span")         -- "span" = navigate to target position
  ai:setTargetPosition(
    playerPos.x,
    playerPos.y,
    playerPos.z)
  ai:setAggression(CONFIG.aiAggression)
  ai:setSpeedMode("limit")
  ai:setSpeed(CONFIG.aiSpeed)
end

M.updateChaserAI = updateChaserAI

-- ── Broadcast all vehicle states ──────────────────────────────────────────────

local function broadcastVehicleStates()
  if not stateSocket then return end

  local vehicles = {}
  local count    = be:getObjectCount()

  for i = 0, count - 1 do
    local obj = be:getObject(i)
    if obj and obj.getVehicleType and obj:getVehicleType() ~= "" then
      local pos = obj:getPosition()
      local rot = obj:getRotation()
      local vel = obj:getVelocity()
      local el  = {}

      -- Try to access electrics safely
      pcall(function()
        el = obj:getController("electrics") or {}
      end)

      local function elval(k) return type(el[k]) == "number" and el[k] or 0 end

      vehicles[#vehicles+1] = {
        id    = tostring(obj:getId()),
        name  = obj:getName() or ("v" .. obj:getId()),
        model = obj.jbeam or "",
        px = pos.x, py = pos.y, pz = pos.z,
        rx = rot.x, ry = rot.y, rz = rot.z, rw = rot.w,
        vx = vel.x, vy = vel.y, vz = vel.z,
        spd  = type(obj.getGroundSpeed) == "function" and obj:getGroundSpeed() or 0,
        rpm  = elval("rpmspin"),
        gear = elval("gear_index"),
        fuel = type(el.fuel) == "number" and el.fuel or 1,
        thr  = elval("throttle"),
        brk  = elval("brake"),
        str  = elval("steering"),
        dmg  = 0,
      }
    end
  end

  if #vehicles == 0 then return end

  local frame = jsonEncode({
    t  = socket.gettime(),
    vs = vehicles,
  })

  stateSocket:sendto(frame, CONFIG.questIp, CONFIG.statePort)
end

M.broadcastVehicleStates = broadcastVehicleStates

-- ── Console commands ──────────────────────────────────────────────────────────

-- beamquest.setChaserID(id) — manually designate a vehicle as the chaser
function M.setChaserId(id)
  chaserId = tonumber(id)
  log("Chaser set to vehicle ID " .. tostring(chaserId))
end

-- beamquest.setQuestIP("192.168.x.x") — point at a specific Quest
function M.setQuestIp(ip)
  CONFIG.questIp = ip
  log("Quest IP set to " .. ip)
end

function M.status()
  log("Quest IP:    " .. CONFIG.questIp)
  log("State port:  " .. CONFIG.statePort)
  log("Pos port:    " .. CONFIG.posPort)
  log("Player pos:  " .. (playerPos and
    string.format("%.1f, %.1f, %.1f", playerPos.x, playerPos.y, playerPos.z)
    or "not received yet"))
  log("Chaser ID:   " .. tostring(chaserId))
end

return M
