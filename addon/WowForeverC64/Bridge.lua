local _, ns = ...
local Bridge = { enabled = true, version = 5, sequence = 0, snapshotID = 0, contextPackets = 0, cells = {}, previous = {} }
ns.Bridge = Bridge
local WIDTH, HEIGHT, CHUNK_SIZE, MAX_PAYLOAD = 64, 5, 108, 8192
-- Leave headroom for desktop capture when HDR expands UI brightness.
local DATA_BRIGHTNESS = 0.35

local function UInt(value, count)
    local bytes = {}
    for index = 1, count do
        bytes[index] = string.char(value % 256)
        value = math.floor(value / 256)
    end
    return table.concat(bytes)
end

local function Milliseconds(value)
    return math.floor((value or 0) * 1000) % 4294967296
end

local function Adler32(data)
    local a, b = 1, 0
    for index = 1, #data do
        a = (a + data:byte(index)) % 65521
        b = (b + a) % 65521
    end
    return b * 65536 + a
end

local function Description(aura)
    local lines = {}
    local length, truncated = 0, false
    if aura.spellId ~= 1229741 then return lines, false end
    for _, raw in ipairs(aura.tooltipLines or {}) do
        local plain = raw:gsub("|c%x%x%x%x%x%x%x%x", ""):gsub("|r", "")
            :gsub("|T.-|t", ""):gsub("|A.-|a", ""):gsub("|n", "\n")
        for line in (plain .. "\n"):gmatch("(.-)\n") do
            line = line:match("^%s*(.-)%s*$")
            if line ~= "" and line ~= aura.name then
                local limited = ns.Api.LimitText(line, 512)
                if #lines < 24 and length + #limited <= 2048 then
                    lines[#lines + 1] = limited
                    length = length + #limited
                    if #limited < #line then truncated = true end
                else
                    truncated = true
                end
            end
        end
    end
    return lines, truncated
end

local function EncodeEffect(aura)
    local name = ns.Api.LimitText(aura.name or "", 160)
    local flags = (aura.kind == "HARMFUL" and 1 or 0)
        + (aura.expirationTime == 0 and 2 or 0)
        + (aura.expirationTime and aura.expirationTime > 0 and 4 or 0)
        + (aura.applications and 8 or 0)
        + (aura.duration and 16 or 0)
    local descriptions, truncated = Description(aura)
    local parts = { UInt(aura.spellId or 0, 4), UInt(aura.auraInstanceID or 0, 4), string.char(flags, #name),
        UInt(Milliseconds(aura.expirationTime), 4), UInt(math.min(65535, math.max(0, math.floor(aura.applications or 0))), 2),
        UInt(math.min(4294967295, math.floor(math.max(0, aura.duration or 0) * 1000)), 4),
        string.char(#descriptions), name }
    for _, text in ipairs(descriptions) do
        parts[#parts + 1] = UInt(#text, 2) .. text
    end
    return table.concat(parts), truncated or #name < #(aura.name or "")
        or (aura.applications ~= nil and aura.applications > 65535)
end

local function EncodeSnapshot(snapshot)
    local ordered = {}
    for _, aura in ipairs(snapshot.auras or {}) do if aura.spellId == 1229741 then ordered[#ordered + 1] = aura end end
    for _, aura in ipairs(snapshot.auras or {}) do if aura.spellId ~= 1229741 and aura.kind == "HARMFUL" then ordered[#ordered + 1] = aura end end
    for _, aura in ipairs(snapshot.auras or {}) do if aura.spellId ~= 1229741 and aura.kind ~= "HARMFUL" then ordered[#ordered + 1] = aura end end
    local records, length, detailsTruncated = {}, 1, false
    for _, aura in ipairs(ordered) do
        local record, truncated = EncodeEffect(aura)
        if #records >= 64 or length + #record > MAX_PAYLOAD then break end
        records[#records + 1] = record
        length = length + #record
        detailsTruncated = detailsTruncated or truncated
    end
    return string.char(#records) .. table.concat(records), detailsTruncated or #records < #ordered
end

local function UpdateScale()
    local _, physicalHeight = GetPhysicalScreenSize()
    local scale = 768 / physicalHeight / UIParent:GetEffectiveScale()
    if Bridge.scale ~= scale then
        Bridge.frame:SetScale(scale)
        Bridge.frame:ClearAllPoints()
        Bridge.frame:SetPoint("BOTTOMLEFT", UIParent, "BOTTOMLEFT", 8, 8)
        Bridge.scale = scale
    end
end

local function CreatePatch()
    if Bridge.frame then return end
    local frame = CreateFrame("Frame", "WowForeverC64PixelBridge", UIParent)
    local cell = WowForeverC64DB and WowForeverC64DB.bridgeCellSize or 2
    if cell ~= 2 and cell ~= 3 then cell = 2 end
    Bridge.cellSize = cell
    frame:SetSize(WIDTH * cell, HEIGHT * cell)
    frame:SetFrameStrata("TOOLTIP")
    frame:SetFrameLevel(100)
    frame:EnableMouse(false)
    for index = 0, WIDTH * HEIGHT - 1 do
        local texture = frame:CreateTexture(nil, "OVERLAY")
        texture:SetSize(cell, cell)
        texture:SetPoint("TOPLEFT", (index % WIDTH) * cell, -math.floor(index / WIDTH) * cell)
        texture:SetColorTexture(0, 0, 0, 1)
        Bridge.cells[index] = texture
    end
    local markers = { {1,0,1}, {0,1,1}, {1,1,1}, {0,0,0}, {1,1,0}, {0,0,1}, {1,1,1}, {0,1,0} }
    for index, rgb in ipairs(markers) do Bridge.cells[index - 1]:SetColorTexture(rgb[1], rgb[2], rgb[3], 1) end
    for level = 0, 15 do
        local intensity = level / 15 * DATA_BRIGHTNESS
        Bridge.cells[8 + level]:SetColorTexture(intensity, intensity, intensity, 1)
    end
    Bridge.frame = frame
    UpdateScale()
end

local function SetSnapshot(payload, partial, truncated)
    Bridge.partial = partial
    Bridge.truncated = truncated
    Bridge.payload = payload
    Bridge.snapshotID = (Bridge.snapshotID + 1) % 65536
    Bridge.checksum = Adler32(payload)
    Bridge.chunkCount = math.ceil(#payload / CHUNK_SIZE)
    Bridge.nextChunk = 0
    Bridge.burstRemaining = Bridge.chunkCount
end

function Bridge.Publish()
    -- Only the sanitized collector model reaches this encoder, including during combat.
    local snapshot = ns.Collector.snapshot
    local payload, truncated = EncodeSnapshot(snapshot)
    local partial = snapshot.auraStatus ~= "ok"
    if payload == Bridge.payload and partial == Bridge.partial and truncated == Bridge.truncated then
        Bridge.pending = nil
        return
    end
    if Bridge.burstRemaining and Bridge.burstRemaining > 0 then
        -- Finish the current snapshot, then send the newest queued state.
        Bridge.pending = { payload, partial, truncated }
    else
        Bridge.pending = nil
        SetSnapshot(payload, partial, truncated)
    end
end

local function EncodeContext(now)
    local context = ns.Context
    local flags = context.dead == nil and 0 or (context.dead and 2 or 1)
    local parts = {}
    local level, quest, zone = context.level, context.quest, context.zone
    if level and now - level.at < 30 then
        flags = flags + 4
        parts[#parts + 1] = UInt(Milliseconds(level.at), 4) .. UInt(level.value, 2)
    end
    for _, entry in ipairs({ { quest, 8, 54 }, { zone, 16, 36 } }) do
        local event = entry[1]
        if event and now - event.at < 30 then
            local name = ns.Api.LimitText(event.value, entry[3])
            flags = flags + entry[2]
            parts[#parts + 1] = UInt(Milliseconds(event.at), 4) .. string.char(#name) .. name
        end
    end
    return string.char(flags) .. table.concat(parts)
end

function Bridge.ResetSession()
    if Bridge.session then Bridge.session = (Bridge.session + 1) % 4294967296 end
    Bridge.payload, Bridge.pending = nil, nil
    Bridge.burstRemaining, Bridge.sequence, Bridge.snapshotID = 0, 0, 0
    Bridge.contextPayload, Bridge.contextResyncAt = nil, nil
    Bridge.sentContext = false
end

function Bridge.Tick()
    if not Bridge.enabled then return end
    local now = ns.Api.Call("bridge.GetTime", GetTime, "number")
    if not now then return end
    CreatePatch()
    UpdateScale()
    Bridge.frame:Show()
    if not Bridge.payload then Bridge.Publish() end
    Bridge.session = Bridge.session or (Milliseconds(now) + math.random(1, 1000000)) % 4294967296
    Bridge.sequence = (Bridge.sequence + 1) % 65536
    if Bridge.burstRemaining == 0 and Bridge.pending then
        SetSnapshot(unpack(Bridge.pending))
        Bridge.pending = nil
    end
    if Bridge.burstRemaining == 0 and now >= (Bridge.resyncAt or 0) then
        Bridge.nextChunk, Bridge.burstRemaining = 0, Bridge.chunkCount
    end
    local context = EncodeContext(now)
    local index, chunk, kind = 65535, "", 0
    -- Context has its own frame so it can get through during multi-chunk aura bursts.
    local contextDue = context ~= Bridge.contextPayload or now >= (Bridge.contextResyncAt or 0)
    if contextDue and (not Bridge.sentContext or Bridge.burstRemaining == 0) then
        Bridge.contextPayload, Bridge.contextResyncAt = context, now + 1
        Bridge.contextPackets = Bridge.contextPackets + 1
        chunk, kind = context, 1
    elseif Bridge.burstRemaining > 0 then
        index = Bridge.nextChunk
        chunk = Bridge.payload:sub(index * CHUNK_SIZE + 1, (index + 1) * CHUNK_SIZE)
        Bridge.nextChunk = Bridge.nextChunk + 1
        Bridge.burstRemaining = Bridge.burstRemaining - 1
        if Bridge.burstRemaining == 0 then Bridge.resyncAt = now + 3 end
    end
    Bridge.sentContext = kind == 1
    local snapshot = ns.Collector.snapshot
    local available = ns.Collector.worldReady and snapshot.expirationAuthoritative and snapshot.auraCapturedAt
        and now - snapshot.auraCapturedAt <= 15
    local flags = (snapshot.inCombat == true and 1 or 0)
        + (available and not Bridge.pending and 2 or 0)
        + (Bridge.partial and 4 or 0)
        + (Bridge.truncated and 8 or 0)
        + (ns.Collector.worldReady and 16 or 0)
        + (available and 32 or 0)
    local prefix = "C64W" .. string.char(Bridge.version, flags) .. UInt(Bridge.sequence, 2)
        .. UInt(Bridge.session, 4) .. UInt(Bridge.snapshotID, 2) .. UInt(index, 2)
        .. UInt(Bridge.chunkCount, 2) .. UInt(#Bridge.payload, 2) .. UInt(#chunk, 2) .. UInt(Bridge.checksum, 4)
    local suffix = UInt(Milliseconds(now), 4) .. string.char(kind, 0)
    local packet = prefix .. UInt(Adler32(prefix .. suffix .. chunk), 4) .. suffix .. chunk
    for byteIndex = 1, #packet do
        local value = packet:byte(byteIndex)
        if Bridge.previous[byteIndex] ~= value then
            -- Match the grayscale calibration ramp exactly, including under HDR color management.
            local high = math.floor(value / 16) / 15 * DATA_BRIGHTNESS
            local low = (value % 16) / 15 * DATA_BRIGHTNESS
            local cellIndex = 32 + (byteIndex - 1) * 2
            Bridge.cells[cellIndex]:SetColorTexture(high, high, high, 1)
            Bridge.cells[cellIndex + 1]:SetColorTexture(low, low, low, 1)
            Bridge.previous[byteIndex] = value
        end
    end
end

function Bridge.SetCellSize(cell)
    if cell ~= 2 and cell ~= 3 then return end
    WowForeverC64DB.bridgeCellSize = cell
    CreatePatch()
    Bridge.cellSize = cell
    Bridge.frame:SetSize(WIDTH * cell, HEIGHT * cell)
    for index = 0, WIDTH * HEIGHT - 1 do
        Bridge.cells[index]:SetSize(cell, cell)
        Bridge.cells[index]:ClearAllPoints()
        Bridge.cells[index]:SetPoint("TOPLEFT", (index % WIDTH) * cell, -math.floor(index / WIDTH) * cell)
    end
end

function Bridge.SetEnabled(enabled)
    Bridge.enabled = enabled
    if not enabled and Bridge.frame then Bridge.frame:Hide() end
    if enabled then Bridge.Publish(); Bridge.Tick() end
end
