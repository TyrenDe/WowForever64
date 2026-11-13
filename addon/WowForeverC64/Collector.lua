local _, ns = ...
local Api = ns.Api
local Collector = {
    snapshot = { schemaVersion = 1, status = "WAITING FOR WORLD", auras = {}, expirationAuthoritative = false },
    combatEvent = false,
    worldReady = false,
    counters = { refreshes = 0, combatEntries = 0, combatExits = 0, blockedRefreshes = 0 },
}
ns.Collector = Collector

function Collector.Publish()
    -- Only our sanitized tables are persisted. SavedVariables are not a live file bridge.
    if type(WowForeverC64DB) ~= "table" then WowForeverC64DB = {} end
    WowForeverC64DB.schemaVersion = 1
    WowForeverC64DB.snapshot = Collector.snapshot
    WowForeverC64DB.observations = Api.observations
    WowForeverC64DB.counters = Collector.counters
    if ns.Bridge then ns.Bridge.Publish() end
    if ns.View then ns.View.Update() end
end

function Collector.Freeze(status)
    Collector.snapshot.status = status
    Collector.snapshot.expirationAuthoritative = false
    Collector.Publish()
end

function Collector.Refresh()
    if not Collector.worldReady then return end
    local combat = Collector.combatEvent and true or Api.CombatState()
    if combat ~= false then
        Collector.counters.blockedRefreshes = Collector.counters.blockedRefreshes + 1
        Collector.snapshot.inCombat = combat
        Collector.Freeze(combat == true and "PAUSED - IN COMBAT" or "PAUSED - COMBAT STATE UNKNOWN")
        return
    end

    Collector.snapshot.inCombat = false
    if type(issecretvalue) ~= "function" then
        Collector.Freeze("UNAVAILABLE - NO SECRET DETECTOR")
        return
    end

    local auras, auraStatus = Api.Auras()
    Collector.snapshot.auraStatus = auraStatus
    if not auras then
        Collector.Freeze("PAUSED - AURA DATA UNAVAILABLE")
        return
    end

    local now = Api.Call("GetTime", GetTime, "number")
    for _, aura in ipairs(auras) do
        if now and aura.expirationTime and aura.expirationTime > 0 then
            aura.remainingAtCapture = math.max(0, aura.expirationTime - now)
        end
    end

    Collector.snapshot.auras = auras
    Collector.snapshot.auraCapturedAt = now
    Collector.snapshot.expirationAuthoritative = now ~= nil
    Collector.snapshot.status = auraStatus == "ok" and "LIVE - OUT OF COMBAT" or "PARTIAL - SOME FIELDS UNAVAILABLE"
    Collector.counters.refreshes = Collector.counters.refreshes + 1
    Collector.Publish()
end

function Collector.CombatStarted()
    Collector.combatEvent = true
    Collector.counters.combatEntries = Collector.counters.combatEntries + 1
    Collector.snapshot.inCombat = true
    -- Do not read UNIT_AURA payloads, aura APIs, or tooltip APIs during combat.
    Collector.Freeze("PAUSED - IN COMBAT")
end

function Collector.CombatEnded()
    Collector.combatEvent = false
    Collector.counters.combatExits = Collector.counters.combatExits + 1
    Collector.Refresh()
end
