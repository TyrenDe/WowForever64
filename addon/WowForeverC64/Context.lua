local _, ns = ...
local Api = ns.Api
local Context = {}
ns.Context = Context

function Context.Reset()
    Context.dead, Context.level, Context.quest, Context.zone = nil, nil, nil, nil
    Context.zoneName, Context.pendingQuest, Context.lastQuest = nil, nil, nil
end

function Context.UpdateDeath()
    if ns.Collector.worldReady then
        Context.dead = Api.Call("UnitIsDeadOrGhost", UnitIsDeadOrGhost, "boolean", "player")
    end
end

function Context.UpdateZone()
    if not ns.Collector.worldReady then return end
    local name = Api.Call("GetMinimapZoneText", GetMinimapZoneText, "string")
    if not name or name == "" then name = Api.Call("GetZoneText", GetZoneText, "string") end
    if not name or name == "" or name == Context.zoneName then return end
    local previous = Context.zoneName
    Context.zoneName = name
    local now = Api.Call("GetTime", GetTime, "number")
    if previous and now and not ns.Collector.combatEvent and ns.Collector.snapshot.inCombat ~= true then
        Context.zone = { value = name, at = now }
        Api.Note("context.zone", "queued")
    end
end

function Context.LevelUp(value)
    local level = Api.Read("event.level", value, "number")
    local now = Api.Call("GetTime", GetTime, "number")
    if level and level == math.floor(level) and level >= 1 and level <= 65535 and now
        and (not Context.level or Context.level.value ~= level) then
        Context.level = { value = level, at = now }
        Api.Note("context.level", "queued")
    end
end

function Context.LevelChanged(oldValue, newValue, realValue)
    local real = Api.Read("event.levelReal", realValue, "boolean")
    local oldLevel = Api.Read("event.oldLevel", oldValue, "number")
    local newLevel = Api.Read("event.newLevel", newValue, "number")
    if real and oldLevel and newLevel and newLevel > oldLevel then Context.LevelUp(newLevel) end
end

local function QuestTitle(id)
    local title = Api.Call("GetTitleForQuestID", C_QuestLog and C_QuestLog.GetTitleForQuestID, "string", id)
    return title and title:find("%S") and title or nil
end

local function QueueQuest(title, at)
    Context.quest = { value = title or "TITLE UNAVAILABLE", at = at }
    Api.Note("context.quest", title and "queued" or "queued-without-title")
end

function Context.QuestTurnedIn(value)
    local id = Api.Read("event.questID", value, "number")
    local now = Api.Call("GetTime", GetTime, "number")
    if not id or id <= 0 or id ~= math.floor(id) or not now then return end
    local last = Context.lastQuest
    if last and last.id == id and now - last.at < 5 then return end
    Context.lastQuest = { id = id, at = now }
    Context.pendingQuest = nil
    local title = QuestTitle(id)
    if title then
        QueueQuest(title, now)
    elseif C_QuestLog and type(C_QuestLog.RequestLoadQuestByID) == "function" then
        local pending = Context.lastQuest
        Context.pendingQuest = pending
        if pcall(C_QuestLog.RequestLoadQuestByID, id) then
            C_Timer.After(0.8, function()
                if Context.pendingQuest ~= pending then return end
                Context.pendingQuest = nil
                QueueQuest(nil, pending.at)
            end)
        else
            Context.pendingQuest = nil
            QueueQuest(nil, now)
        end
    else
        QueueQuest(nil, now)
    end
end

function Context.QuestDataLoaded(value, result)
    local pending = Context.pendingQuest
    if not pending then return end
    local id = Api.Read("event.questLoadID", value, "number")
    if id ~= pending.id then return end
    Context.pendingQuest = nil
    local success = Api.Read("event.questLoadSuccess", result, "boolean")
    QueueQuest(success and QuestTitle(id), pending.at)
end
