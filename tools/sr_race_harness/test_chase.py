"""
Runs the race mode's police chase (apps/new-modes/sr_race/mode.lua) in a stub world, without Assetto Corsa.

The stub is a round track in the XZ plane. Cars move along it at the speeds each scenario gives them, the cops as
the mode's physics calls allow (held, capped, put somewhere else). Everything CSP gives the mode is stubbed as far as
the mode uses it. What comes out is the result file the mode writes, which each scenario checks.

It checks the mode's own logic: when the patrol shows up, who is busted, who gets away, the roadblock, what the
result says. Not how CSP's AI drives, which only the game can show.

    pip install lupa
    python tools/sr_race_harness/test_chase.py
"""

import json
import os
import sys

import lupa

HERE = os.path.dirname(os.path.abspath(__file__))
MODE = os.path.join(HERE, '..', '..', 'apps', 'new-modes', 'sr_race', 'mode.lua')

STUB = r'''
local world = ...
local L = world.length
local R = L / (2 * math.pi)

-- vectors -------------------------------------------------------------------
local V = {}
V.__index = V
local function isvec(v) return type(v) == 'table' and getmetatable(v) == V end
function vec3(x, y, z) return setmetatable({ x = x or 0, y = y or 0, z = z or 0 }, V) end
function vec2(x, y) return vec3(x, y, 0) end
function vec4(x, y, z, w) local v = vec3(x, y, z); v.w = w; return v end
function V:set(x, y, z)
  if isvec(x) then self.x, self.y, self.z = x.x, x.y, x.z else self.x, self.y, self.z = x, y, z end
  return self
end
function V:add(v) self.x, self.y, self.z = self.x + v.x, self.y + v.y, self.z + v.z; return self end
function V:sub(v) self.x, self.y, self.z = self.x - v.x, self.y - v.y, self.z - v.z; return self end
function V:scale(k) self.x, self.y, self.z = self.x * k, self.y * k, self.z * k; return self end
function V:addScaled(v, k) self.x, self.y, self.z = self.x + v.x * k, self.y + v.y * k, self.z + v.z * k; return self end
function V:length() return math.sqrt(self.x * self.x + self.y * self.y + self.z * self.z) end
function V:distance(v) return math.sqrt((self.x - v.x) ^ 2 + (self.y - v.y) ^ 2 + (self.z - v.z) ^ 2) end
function V:dot(v) return self.x * v.x + self.y * v.y + self.z * v.z end
function V:normalize() local l = self:length(); if l > 0 then self:scale(1 / l) end; return self end
V.__sub = function(a, b) return vec3(a.x - b.x, a.y - b.y, a.z - b.z) end
V.__add = function(a, b) return vec3(a.x + b.x, a.y + b.y, a.z + b.z) end
V.__unm = function(a) return vec3(-a.x, -a.y, -a.z) end
function rgb(r, g, b) return { r = r, g = g, b = b } end
function rgbm(r, g, b, m) return { r = r, g = g, b = b, m = m } end

-- the track -----------------------------------------------------------------
local function wrap(s) return s - math.floor(s / L) * L end
local function pointAt(s, lateral)
  local a = wrap(s) / R
  local dir = vec3(-math.sin(a), 0, math.cos(a))
  local right = vec3(-dir.z, 0, dir.x)
  return vec3(R * math.cos(a), 0, R * math.sin(a)):addScaled(right, lateral or 0), dir, right
end
local function progressOf(v)
  local a = math.atan(v.z, v.x)
  return wrap(a * R) / L
end

-- the cars ------------------------------------------------------------------
local sim = { time = 0, isSessionStarted = false, isInMainMenu = false, isPaused = false, carsCount = world.cars,
  trackLengthM = L, raceSessionType = 3 }
local cars, controls = {}, {}
for i = 0, world.cars - 1 do
  cars[i] = { index = i, total = -10 * (i + 1), speedKmh = 0, lapCount = 0, damage = { [0] = 0, 0, 0, 0 }, engineLifeLeft = 1000,
    gearboxDamage = 0, wheels = {}, aabbSize = vec3(1.9, 1.4, 5), aabbCenter = vec3(0, 0.6, 0), fuel = 40,
    waterTemperature = 90, oilTemperature = 100, oilPressure = 4, previousLapTimeMs = 0, racePosition = i + 1, collidedWith = 0,
    position = vec3(), velocity = vec3(), look = vec3(), up = vec3(0, 1, 0), side = vec3(), splinePosition = 0 }
  for w = 0, 3 do cars[i].wheels[w] = { isBlown = false, suspensionDamage = 0, tyreWear = 0, tyreVirtualKM = 0 } end
  controls[i] = { throttle = 1, stop = 0, top = 1e9, active = true, placed = 0 }
end
local function refresh(car)
  local pos, dir, right = pointAt(car.total, 0)
  car.position:set(pos)
  car.look:set(dir)
  car.side:set(right)
  car.velocity:set(dir):scale(car.speedKmh / 3.6)
  car.splinePosition = wrap(car.total) / L
  car.lapCount = math.max(0, math.floor(car.total / L))
end
for i = 0, world.cars - 1 do refresh(cars[i]) end

-- CSP -------------------------------------------------------------------------
local log = {}
local targets = {}
local timers = {}
local saved = {}
local quit = false
local placements = {}
local iniValues = world.ini

ac = {
  FolderID = { ACDocuments = 1, ScriptOrigin = 2 },
  LightType = { Regular = 1 },
  getSim = function() return sim end,
  getCar = function(i) return cars[i] end,
  getDriverName = function(i) return i == 0 and 'Player' or i == 1 and 'Rival' or 'Police' end,
  getCarName = function(i) return 'car_' .. i end,
  -- As CSP reads race.ini: a value is split at its commas; with no default the list comes back, with a string default
  -- only the first item (the trap that once dropped every cop but the first)
  INIConfig = { raceConfig = function() return { get = function(_, section, key, default)
    local v = iniValues[section .. '.' .. key]
    if v == nil then return default end
    local items = {}
    for part in tostring(v):gmatch('[^,]+') do items[#items + 1] = part end
    if default == nil then return items end
    return items[1] end } end },
  -- Each cop's target, as the mode announces it: the stub's cars block the cop behind it
  log = function(s)
    log[#log + 1] = s
    local cop, racer = s:match('Cop (%d+) after car (%d+)')
    if not cop then cop, racer = s:match('Cop (%d+) in its speed trap saw car (%d+)') end
    if cop then targets[tonumber(cop)] = tonumber(racer) end
  end,
  setMessage = function(t, d) log[#log + 1] = '[message] ' .. t .. ': ' .. tostring(d) end,
  setCarActive = function(i, a) controls[i].active = a end,
  getCarLeaderboardPosition = function(i) return cars[i].racePosition end,
  getTrackID = function() return 'stub_loop' end,
  getTrackLayout = function() return '' end,
  getFolder = function() return 'C:\\stub' end,
  onCarCollision = function() end,
  onCarJumped = function() end,
  disableCarRecovery = function() end,
  tryToStart = function() return true end,
  setStartMessage = function() end,
  shutdownAssettoCorsa = function() quit = true end,
  worldCoordinateToTrackProgress = progressOf,
  trackProgressToWorldCoordinateTo = function(t, r) r:set(pointAt(t * L, 0)) end,
  getTrackAISplineSides = function() return vec2(world.sides or 6, world.sides or 6) end,
  getCameraPosition = function() return vec3() end,
  LightSource = function() return { dispose = function() end } end,
  AudioEvent = { fromFile = function() return { start = function() end, seek = function() end, stop = function() end,
    dispose = function() end, setPosition = function() end } end },
}
physics = {
  allowed = function() return true end,
  blockTeleportingToPits = function() return {} end,
  setCarBodyDamage = function() end, setCarEngineLife = function() end,
  setCarNoInput = function() end, lockUserControlsFor = function() end, forceUserBrakesFor = function() end,
  setAIThrottleLimit = function(i, v) controls[i].throttle = v end,
  setAIStopCounter = function(i, v) controls[i].stop = v end,
  setAITopSpeed = function(i, v) controls[i].top = v / 3.6 end,
  disableCarCollisions = function() end,
  setCarVelocity = function(i, v) cars[i].speedKmh = v:length() * 3.6 end,
  setAICarPosition = function(i, pos, facing)
    local car = cars[i]
    -- On the lap that puts it at most a quarter lap behind the player or up to three quarters ahead: the mode puts
    -- cops a few hundred metres behind a racer, or ahead (a roadblock, a trap anywhere round the lap)
    local s = progressOf(pos) * L
    car.total = s + math.ceil((cars[0].total - 0.25 * L - s) / L) * L
    controls[i].placed = controls[i].placed + 1
    placements[#placements + 1] = { index = i, t = sim.time / 1000, progress = progressOf(pos) }
    refresh(car)
  end,
  awakeCar = function() end, setEngineStallEnabled = function() end, setEngineRPM = function() end,
  setAIPitStopRequest = function() end, preventAIFromRetiring = function() end,
  setAILevel = function() end, setAIAggression = function() end, setExtraAIGrip = function() end,
  setAISplineAbsoluteOffset = function() end,
  setGentleStop = function(i, v) controls[i].gentle = v ~= false end,
  setCarAutopilot = function() end,
  raycastTrack = function(pos) return pos.y end,
}
render = { circle = function() end }
JSON = {}
function JSON.stringify(v)
  local t = type(v)
  if t == 'nil' then return 'null' end
  if t == 'boolean' or t == 'number' then return tostring(v) end
  if t == 'string' then return string.format('%q', v) end
  if #v > 0 or next(v) == nil then
    if next(v) == nil then return '{}' end
    local parts = {}
    for _, x in ipairs(v) do parts[#parts + 1] = JSON.stringify(x) end
    return '[' .. table.concat(parts, ',') .. ']'
  end
  local parts = {}
  for k, x in pairs(v) do parts[#parts + 1] = string.format('%q', tostring(k)) .. ':' .. JSON.stringify(x) end
  return '{' .. table.concat(parts, ',') .. '}'
end
io.createDir = function() end
io.save = function(p, s) saved[p] = s; return true end
io.move = function(a, b) saved[b] = saved[a]; saved[a] = nil; return true end
io.deleteFile = function(p) saved[p] = nil end
os.preciseClock = os.clock
function setInterval(f, t) timers[#timers + 1] = { f = f, every = t, due = t }; return #timers end
function clearInterval(id) if timers[id] then timers[id].dead = true end end
function setTimeout(f, t) timers[#timers + 1] = { f = f, due = t, once = true }; return #timers end
script = {}

-- the run ---------------------------------------------------------------------
local function step(dt, t)
  sim.time = sim.time + dt * 1000
  if not sim.isSessionStarted and t >= 3 then sim.isSessionStarted = true end
  for i = 0, world.cars - 1 do
    local car, c = cars[i], controls[i]
    local want
    if not sim.isSessionStarted then want = 0
    elseif i == 0 then want = world.playerSpeed(t)
    elseif i == 1 then want = world.rivalSpeed(t)
    else want = world.copSpeed(t, i) end
    if i > 0 and (c.throttle <= 0 or c.stop > 0) then want = 0 end
    if i > 0 then want = math.min(want, c.top) end
    c.stop = math.max(0, c.stop - dt)
    -- A car being stopped gently (the busted player, pulled over) stops
    if c.gentle then want = 0 end
    car.total = car.total + want * dt
    car.speedKmh = want * 3.6
    refresh(car)
  end
  for _, timer in ipairs(timers) do
    if not timer.dead then
      timer.due = timer.due - dt
      if timer.due <= 0 then
        timer.f()
        if timer.once then timer.dead = true else timer.due = timer.every end
      end
    end
  end
end

return function(modeSource)
  local chunk = assert(load(modeSource, '=mode.lua'))
  chunk()
  local t, dt = 0, 0.05
  while t < world.maxSeconds and not quit do
    step(dt, t)
    if script.update then script.update(dt) end
    t = t + dt
  end
  local result
  for _, v in pairs(saved) do result = v end
  return result, table.concat(log, '\n'), t, #placements
end
'''


def run(name, cars, ini, player, rival, cop, max_seconds=400, length=2000.0, sides=6.0):
    lua = lupa.LuaRuntime(unpack_returned_tuples=True)
    world = lua.table_from({
        'length': length, 'cars': cars, 'maxSeconds': max_seconds, 'sides': sides,
        'ini': lua.table_from(ini),
        'playerSpeed': player, 'rivalSpeed': rival, 'copSpeed': cop,
    })
    runner = lua.execute(STUB, world)
    with open(MODE, encoding='utf-8') as f:
        source = f.read()
    result, log, seconds, placements = runner(source)
    if result is None:
        print(log)
        raise AssertionError(f'{name}: no result written after {seconds:.0f} s')
    return json.loads(result), log, seconds, placements


def check(name, condition, log):
    if not condition:
        print(log)
        raise AssertionError(name)


POLICE_INI = {'STREET_ROD.RACE_TYPE': 'ROAD', 'STREET_ROD.POLICE': '2,3', 'STREET_ROD.POLICE_SPOT': '0.1'}


def overtaken_and_busted():
    # The cops are faster: the player's gets past him, and he is pulled over
    result, log, _, _ = run('overtaken', 4, POLICE_INI, lambda t: 30, lambda t: 20, lambda t, i: 45)
    pursuit = result['pursuit']
    check('the patrol showed up', pursuit['started'], log)
    check('a cop got past the player', 'got past car 0' in log, log)
    check('the player is busted', pursuit['player'] == 'BUSTED', log)
    check('before the line the race ends BUSTED', result['session']['end_reason'] == 'BUSTED', log)
    check('one cop for each racer', 'Cop 2 after car 0' in log and 'Cop 3 after car 1' in log, log)
    check('the police are not participants', len(result['participants']) == 2, log)
    check('both cops were read from POLICE=2,3', pursuit['police'] == 2, log)


def escaped_after_the_roadblocks():
    # Slow cops: they fall back, try their roadblock once each, chase again once dodged, and are left behind for good
    result, log, _, _ = run('escaped', 4, POLICE_INI, lambda t: 30, lambda t: 20, lambda t, i: 12)
    pursuit = result['pursuit']
    check('the player got away', pursuit['player'] == 'ESCAPED', log)
    check('the race was won at the line', result['session']['end_reason'] == 'FINISHED', log)
    check('the player finished first', result['participants'][0]['performance']['final_position'] == 1, log)
    check('a roadblock went up', 'sets up a roadblock' in log, log)
    check('a dodged roadblock chases again', 'got past its roadblock, after it' in log, log)
    check('nobody was busted by a roadblock ahead of them', 'got past car' not in log, log)


def rival_overtaken_the_player_gets_away():
    # The rival's cop is fast and gets past him; the player's is slow
    result, log, _, _ = run('rival busted', 4, POLICE_INI, lambda t: 30, lambda t: 20,
                            lambda t, i: 40 if i == 3 else 12)
    pursuit = result['pursuit']
    check('the rival is busted', pursuit['rival'] == 'BUSTED', log)
    check('by being overtaken', 'got past car 1' in log, log)
    check('the player got away', pursuit['player'] == 'ESCAPED', log)
    check('and won at the line', result['session']['end_reason'] == 'FINISHED', log)


def the_line_is_home():
    # A cop as fast as the player, on his tail the whole way: he crosses the line, and he made it
    result, log, _, _ = run('the line', 4, POLICE_INI, lambda t: 30, lambda t: 20, lambda t, i: 30)
    pursuit = result['pursuit']
    check('the race ends at the line', result['session']['end_reason'] == 'FINISHED', log)
    check('a player chased over the line got away', pursuit['player'] == 'ESCAPED', log)
    check('and won', result['participants'][0]['performance']['final_position'] == 1, log)


def no_police_no_pursuit():
    result, log, _, _ = run('no police', 2, {'STREET_ROD.RACE_TYPE': 'ROAD'}, lambda t: 30, lambda t: 20, lambda t, i: 0)
    check('no pursuit', 'pursuit' not in result, log)
    check('won', result['participants'][0]['performance']['final_position'] == 1, log)


TRAPS_INI = {'STREET_ROD.RACE_TYPE': 'ROAD', 'STREET_ROD.POLICE': '2,3', 'STREET_ROD.POLICE_SPOT': '0.1',
             'STREET_ROD.POLICE_MODE': 'TRAPS'}


def trap_sees_the_player_who_is_then_busted():
    # An 8 km loop: straight enough for traps. The trap cop is faster and gets past him
    result, log, _, _ = run('trap busted', 4, TRAPS_INI, lambda t: 30, lambda t: 20, lambda t, i: 45,
                            max_seconds=700, length=8000.0)
    pursuit = result['pursuit']
    check('the cops were parked in traps', log.count('parked in a speed trap') == 2, log)
    check('a trap saw the player', 'saw car 0 go by' in log, log)
    check('and got past him', 'got past car 0' in log, log)
    check('the player is busted', pursuit['player'] == 'BUSTED', log)


def each_racer_gets_a_trap_of_his_own():
    # Slow cops. The player goes past the first trap first; the rival, behind him, finds it taken and gets the second
    result, log, _, _ = run('a trap each', 4, TRAPS_INI, lambda t: 30, lambda t: 27, lambda t, i: 10,
                            max_seconds=700, length=8000.0)
    check('a trap saw the player', 'saw car 0 go by' in log, log)
    check('the other trap saw the rival', 'saw car 1 go by' in log, log)
    check('two different cops', log.count('in its speed trap saw car') == 2, log)


def only_the_rival_is_seen_and_the_player_finishes_clean():
    # One trap. The rival is faster, goes past it first and stops later on; the player never has a cop after him
    ini = dict(TRAPS_INI, **{'STREET_ROD.POLICE': '2'})
    result, log, _, _ = run('rival seen', 3, ini, lambda t: 25, lambda t: 35 if t < 200 else 0, lambda t, i: 45,
                            max_seconds=700, length=8000.0)
    pursuit = result['pursuit']
    check('the trap saw the rival', 'saw car 1 go by' in log, log)
    check('the rival is busted', pursuit['rival'] == 'BUSTED', log)
    check("the player's chase was never on", pursuit.get('player') is None, log)
    check('the player finished the race and won it', result['session']['end_reason'] == 'FINISHED'
          and result['participants'][0]['performance']['final_position'] == 1, log)


def no_room_for_traps_means_a_patrol():
    result, log, _, _ = run('no room', 4, TRAPS_INI, lambda t: 30, lambda t: 20, lambda t, i: 12, sides=2.0)
    check('no trap spot found', 'No spot for a speed trap' in log, log)
    check('the patrol came instead', 'after car 0 from 200 m back' in log, log)
    check('and the chase ran', result['pursuit']['started'], log)


if __name__ == '__main__':
    for test in (overtaken_and_busted, escaped_after_the_roadblocks, rival_overtaken_the_player_gets_away, the_line_is_home, no_police_no_pursuit,
                 trap_sees_the_player_who_is_then_busted, each_racer_gets_a_trap_of_his_own,
                 only_the_rival_is_seen_and_the_player_finishes_clean, no_room_for_traps_means_a_patrol):
        test()
        print(f'ok  {test.__name__}')
    sys.exit(0)
