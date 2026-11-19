local addonName, ns = ...
local frame = CreateFrame("Frame")
local pending = false
local initialized = false
ns.version = "0.6.1"

local function Print(text)
    DEFAULT_CHAT_FRAME:AddMessage("|cff80d0ffC64:|r " .. text)
end

local function RequestRefresh()
    if pending or not initialized then return end
    pending = true
    C_Timer.After(0.25, function()
        pending = false
        ns.Collector.Refresh()
    end)
end

local function Initialize()
    for _, field in ipairs({ { "clientVersion", 1, "string" }, { "clientBuild", 2, "string" }, { "clientInterface", 4, "number" } }) do
        ns[field[1]] = ns.Api.Call(field[1], function() return select(field[2], GetBuildInfo()) end, field[3])
    end
    ns.Collector.Publish()
    initialized = true
    SLASH_WOWFOREVERC641 = "/c64"
    SlashCmdList.WOWFOREVERC64 = function(message)
        local command, argument = message:lower():match("^%s*(%S*)%s*(.-)%s*$")
        if command == "" or command == "show" then
            ns.View.Toggle()
        elseif command == "report" then
            ns.View.ShowReport()
        elseif command == "refresh" then
            ns.Collector.Refresh()
            Print(ns.Collector.snapshot.status)
        elseif command == "status" then
            Print(("Addon %s, bridge v%d, %d px cells, %d context packets sent."):format(
                ns.version, ns.Bridge.version, ns.Bridge.cellSize or 2, ns.Bridge.contextPackets))
        elseif command == "hide" then
            if ns.View.frame then ns.View.frame:Hide() end
        elseif command == "bridge" then
            if argument == "2" or argument == "3" then ns.Bridge.SetCellSize(tonumber(argument)) end
            ns.Bridge.SetEnabled(argument ~= "off")
            Print("Pixel bridge " .. (ns.Bridge.enabled and "on" or "off"))
        else
            Print("/c64 show | hide | refresh | report | status | bridge on/off")
        end
    end
    -- Timers update display text and retry delayed tooltip data; no per-frame aura scans.
    C_Timer.NewTicker(1, function()
        ns.Context.UpdateDeath()
        ns.View.Update()
    end)
    C_Timer.NewTicker(0.2, ns.Bridge.Tick)
    C_Timer.NewTicker(5, RequestRefresh)
    Print("Status bridge loaded. /c64 shows effects; /c64 bridge off hides the pixel patch.")
end

frame:SetScript("OnEvent", function(_, event, arg1, arg2, arg3)
    if event == "ADDON_LOADED" then
        if arg1 == addonName then Initialize() end
        return
    end
    if not initialized then return end
    if event == "PLAYER_REGEN_DISABLED" then
        ns.Context.zone = nil
        ns.Collector.CombatStarted()
    elseif event == "PLAYER_REGEN_ENABLED" then
        ns.Collector.CombatEnded()
        RequestRefresh()
    elseif event == "PLAYER_LEAVING_WORLD" then
        ns.Collector.worldReady = false
        ns.Context.zone = nil
        ns.Collector.Freeze("PAUSED - LOADING WORLD")
    elseif event == "PLAYER_ENTERING_WORLD" then
        if ns.Api.Read("event.initialLogin", arg1, "boolean") == true then
            ns.Collector.Reset()
            ns.Context.Reset()
            ns.Bridge.ResetSession()
        end
        ns.Collector.worldReady = true
        ns.Collector.combatEvent = false
        ns.Context.UpdateDeath()
        ns.Context.UpdateZone()
        RequestRefresh()
    elseif event == "PLAYER_LOGOUT" then
        ns.Collector.Freeze("OFFLINE - SAVED SNAPSHOT")
    elseif event == "PLAYER_LEVEL_UP" then
        ns.Context.LevelUp(arg1)
    elseif event == "PLAYER_LEVEL_CHANGED" then
        ns.Context.LevelChanged(arg1, arg2, arg3)
    elseif event == "QUEST_TURNED_IN" then
        ns.Context.QuestTurnedIn(arg1)
    elseif event == "QUEST_DATA_LOAD_RESULT" then
        ns.Context.QuestDataLoaded(arg1, arg2)
    elseif event == "UNIT_AURA" then
        -- Ignore UNIT_AURA's updateInfo entirely, including its secret members.
        if not ns.Collector.combatEvent then
            local unit = ns.Api.Read("event.unit", arg1, "string")
            if unit == "player" then RequestRefresh() end
        end
    else
        ns.Context.UpdateZone()
        RequestRefresh()
    end
end)

for _, event in ipairs({
    "ADDON_LOADED", "PLAYER_ENTERING_WORLD", "PLAYER_LEAVING_WORLD", "PLAYER_LOGOUT",
    "PLAYER_REGEN_DISABLED", "PLAYER_REGEN_ENABLED",
    "ZONE_CHANGED", "ZONE_CHANGED_INDOORS", "ZONE_CHANGED_NEW_AREA", "UNIT_AURA",
}) do
    frame:RegisterEvent(event)
end

for _, event in ipairs({ "PLAYER_LEVEL_UP", "PLAYER_LEVEL_CHANGED", "QUEST_TURNED_IN", "QUEST_DATA_LOAD_RESULT" }) do
    -- A client without one of these events still has the aura dashboard.
    ns.Api.Note(event, pcall(frame.RegisterEvent, frame, event) and "registered" or "missing-event")
end
