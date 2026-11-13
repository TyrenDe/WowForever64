local _, ns = ...
local View = {}
ns.View = View
local CAMP_BENEFITS_SPELL_ID = 1229741

local function Text(value)
    if value == nil then return "unavailable" end
    return tostring(value)
end

local function Plain(text)
    return text:gsub("|c%x%x%x%x%x%x%x%x", ""):gsub("|r", "")
        :gsub("|T.-|t", ""):gsub("|A.-|a", ""):gsub("|n", "\n")
end

local function AppendTooltipLines(lines, text, prefix)
    local plain = Plain(text):gsub("\r\n", "\n"):gsub("\r", "\n")
    for line in (plain .. "\n"):gmatch("(.-)\n") do
        line = line:match("^%s*(.-)%s*$")
        if line ~= "" then lines[#lines + 1] = prefix .. line end
    end
end

local function Timer(aura, snapshot)
    if not snapshot.expirationAuthoritative then return "FROZEN - time not live" end
    if aura.expirationTime == nil or aura.duration == nil then return "time unavailable" end
    if aura.expirationTime == 0 then return "no expiration reported" end
    local now = ns.Api.Call("GetTime", GetTime, "number")
    if not now then return "time unavailable" end
    local remaining = math.max(0, math.floor(aura.expirationTime - now))
    return string.format("%d:%02d", math.floor(remaining / 60), remaining % 60)
end

function View.LiveText()
    local snapshot = ns.Collector.snapshot
    local lines = {
        "WoW Forever C64 - Status Effects",
        snapshot.status,
        "",
        "STATUS EFFECTS: " .. #(snapshot.auras or {}) .. " readable entries",
        "Aura access: " .. Text(snapshot.auraStatus),
    }
    if not snapshot.expirationAuthoritative then
        lines[#lines + 1] = "Last known buffs are frozen; expiration values are not live."
    end
    local campSeen = false
    for _, aura in ipairs(snapshot.auras or {}) do
        lines[#lines + 1] = ""
        lines[#lines + 1] = (aura.kind == "HARMFUL" and "DEBUFF: " or "BUFF: ") .. Text(aura.name) .. " [" .. Text(aura.spellId) .. "]  " .. Timer(aura, snapshot)
        if aura.spellId == CAMP_BENEFITS_SPELL_ID then
            campSeen = true
            lines[#lines + 1] = "  Game tooltip text:"
            for _, text in ipairs(aura.tooltipLines) do
                AppendTooltipLines(lines, text, "  ")
            end
            lines[#lines + 1] = "  Tooltip: " .. aura.tooltipStatus
        end
    end
    if not campSeen then
        lines[#lines + 1] = ""
        lines[#lines + 1] = "Camp Benefits was not identified in this readable snapshot."
    end
    lines[#lines + 1] = ""
    lines[#lines + 1] = "Pixel bridge: " .. (ns.Bridge.enabled and "ON" or "OFF") .. ". Windows command: watch."
    return table.concat(lines, "\n")
end

function View.ReportText()
    local snapshot = ns.Collector.snapshot
    local lines = {
        "WoW Forever C64 diagnostic report v0.5.0",
        "Client: " .. Text(ns.clientVersion) .. " build " .. Text(ns.clientBuild) .. " interface " .. Text(ns.clientInterface),
        "Status: " .. snapshot.status,
        "In combat: " .. Text(snapshot.inCombat),
        "Expiration values authoritative now: " .. Text(snapshot.expirationAuthoritative),
        "Aura captured at client uptime: " .. Text(snapshot.auraCapturedAt),
        "Aura scan: " .. Text(snapshot.auraStatus),
        "In combat, aura/tooltip fields are deliberately NOT QUERIED.",
        "Numbers below describe the last out-of-combat capture, not current combat state.",
        "Camp Benefits spell ID: " .. CAMP_BENEFITS_SPELL_ID,
        "",
    }
    lines[#lines + 1] = "\nAURAS AT CAPTURE"
    for _, aura in ipairs(snapshot.auras or {}) do
        lines[#lines + 1] = ""
        for _, key in ipairs({ "index", "kind", "name", "spellId", "auraInstanceID", "duration", "expirationTime", "remainingAtCapture", "applications", "tooltipStatus" }) do
            lines[#lines + 1] = key .. ": " .. Text(aura[key])
        end
        for _, text in ipairs(aura.tooltipLines) do AppendTooltipLines(lines, text, "tooltip: ") end
    end
    lines[#lines + 1] = "\nSESSION COUNTERS"
    for _, key in ipairs({ "refreshes", "combatEntries", "combatExits", "blockedRefreshes" }) do
        lines[#lines + 1] = key .. ": " .. ns.Collector.counters[key]
    end
    lines[#lines + 1] = "\nAPI/FIELD OBSERVATIONS (counts for this login/reload)"
    local labels = {}
    for label in pairs(ns.Api.observations) do labels[#labels + 1] = label end
    table.sort(labels)
    for _, label in ipairs(labels) do
        local states = {}
        for state, count in pairs(ns.Api.observations[label]) do states[#states + 1] = state .. "=" .. count end
        table.sort(states)
        lines[#lines + 1] = label .. ": " .. table.concat(states, ", ")
    end
    return table.concat(lines, "\n")
end

local function SetText(text)
    local rows = 1
    for line in (text .. "\n"):gmatch("(.-)\n") do rows = rows + math.max(1, math.ceil(#line / 75)) end
    View.editor:SetHeight(math.max(400, rows * 18))
    View.editor:SetText(text)
end

local function CreateWindow()
    if View.frame then return end
    local frame = CreateFrame("Frame", "WowForeverC64Window", UIParent, "BasicFrameTemplateWithInset")
    frame:SetSize(640, 560)
    frame:SetPoint("CENTER")
    frame:SetClampedToScreen(true)
    frame:SetMovable(true)
    frame:EnableMouse(true)
    frame:RegisterForDrag("LeftButton")
    frame:SetScript("OnDragStart", frame.StartMoving)
    frame:SetScript("OnDragStop", frame.StopMovingOrSizing)
    frame.TitleText:SetText("WoW Forever C64 - Status Effects")
    table.insert(UISpecialFrames, "WowForeverC64Window")
    View.frame = frame

    local scroll = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
    scroll:SetPoint("TOPLEFT", 14, -36)
    scroll:SetPoint("BOTTOMRIGHT", -34, 64)
    local editor = CreateFrame("EditBox", nil, scroll)
    editor:SetMultiLine(true)
    editor:SetAutoFocus(false)
    editor:SetFontObject(ChatFontNormal)
    editor:SetWidth(580)
    editor:SetHeight(400)
    editor:SetMaxLetters(0)
    editor:SetScript("OnEscapePressed", function(self) self:ClearFocus(); frame:Hide() end)
    scroll:SetScrollChild(editor)
    View.editor = editor

    local function Button(text, x, action)
        local button = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
        button:SetSize(130, 24)
        button:SetPoint("BOTTOMLEFT", x, 32)
        button:SetText(text)
        button:SetScript("OnClick", action)
    end
    Button("Refresh", 14, function() ns.Collector.Refresh(); View.Update() end)
    Button("Copy report", 152, View.ShowReport)
    Button("Live view", 290, View.ShowLive)
    local hint = frame:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
    hint:SetPoint("BOTTOMLEFT", 14, 14)
    hint:SetText("Copy report freezes this text for selection: click inside, Ctrl+A, Ctrl+C.")
end

function View.Update()
    if View.frame and View.frame:IsShown() and not View.reportMode then SetText(View.LiveText()) end
end

function View.ShowLive()
    CreateWindow()
    View.reportMode = false
    View.frame:Show()
    View.Update()
end

function View.ShowReport()
    CreateWindow()
    View.reportMode = true
    View.frame:Show()
    local report = View.ReportText()
    SetText(report)
    WowForeverC64DB.lastReport = report
    View.editor:SetFocus()
    View.editor:HighlightText()
end

function View.Toggle()
    if View.frame and View.frame:IsShown() then View.frame:Hide() else View.ShowLive() end
end
