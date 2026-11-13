local _, ns = ...
local Api = { observations = {} }
ns.Api = Api

function Api.Note(label, status)
    local entry = Api.observations[label] or {}
    entry[status] = (entry[status] or 0) + 1
    Api.observations[label] = entry
end

function Api.Read(label, value, expectedType)
    if type(issecretvalue) ~= "function" then
        Api.Note(label, "no-secret-detector")
        return nil, "no-secret-detector"
    end
    if issecretvalue(value) then
        Api.Note(label, "secret-omitted")
        return nil, "secret-omitted"
    end
    local status = type(value) == expectedType and "ok" or "absent-or-wrong-type"
    if expectedType == "number" and status == "ok" then
        if value ~= value or value == math.huge or value == -math.huge then
            status = "invalid-number"
        end
    end
    Api.Note(label, status)
    if status == "ok" then return value, status end
    return nil, status
end

function Api.Call(label, func, expectedType, ...)
    if type(func) ~= "function" then
        Api.Note(label, "missing-api")
        return nil, "missing-api"
    end
    local ok, value = pcall(func, ...)
    if not ok then
        -- Never stringify an exception or a secret return value.
        Api.Note(label, "api-error")
        return nil, "api-error"
    end
    return Api.Read(label, value, expectedType)
end

function Api.Field(label, source, key, expectedType)
    return Api.Call(label, function() return source[key] end, expectedType)
end

function Api.CombatState()
    local fighting = Api.Call("UnitAffectingCombat", UnitAffectingCombat, "boolean", "player")
    local lockdown = C_RestrictedActions and C_RestrictedActions.InCombatLockdown or InCombatLockdown
    local locked = Api.Call("InCombatLockdown", lockdown, "boolean")
    if fighting == true or locked == true then return true end
    if fighting == false and locked == false then return false end
    return nil
end

function Api.CanReadAuras()
    local secret = Api.Call("ShouldAurasBeSecret", C_Secrets and C_Secrets.ShouldAurasBeSecret, "boolean")
    return secret == false
end

function Api.LimitText(text, limit)
    if #text <= limit then return text end
    while limit > 0 do
        local nextByte = text:byte(limit + 1)
        if nextByte < 128 or nextByte >= 192 then break end
        limit = limit - 1
    end
    return text:sub(1, limit)
end

function Api.Tooltip(index, auraInstanceID, filter)
    local data, status
    local byInstance = C_TooltipInfo and (filter == "HELPFUL" and C_TooltipInfo.GetUnitBuffByAuraInstanceID or C_TooltipInfo.GetUnitAuraByAuraInstanceID)
    if type(byInstance) == "function" then
        if auraInstanceID == nil then
            Api.Note("tooltip.data", "missing-readable-instance-id")
            return {}, "missing-readable-instance-id"
        end
        data, status = Api.Call("tooltip.data", byInstance, "table", "player", auraInstanceID, filter)
    else
        data, status = Api.Call("tooltip.data", C_TooltipInfo and C_TooltipInfo.GetUnitAura, "table", "player", index, filter)
    end
    if not data then return {}, status end
    local lines = Api.Field("tooltip.lines", data, "lines", "table")
    if not lines then return {}, "unavailable" end

    local result = {}
    for lineIndex = 1, 32 do
        local line = Api.Field("tooltip.line", lines, lineIndex, "table")
        if not line then break end
        for _, side in ipairs({ "leftText", "rightText" }) do
            local text = Api.Field("tooltip." .. side, line, side, "string")
            if text and text ~= "" then
                -- Preserve the game's wording; do not infer effects from stat-name lists.
                result[#result + 1] = Api.LimitText(text, 512)
            end
        end
    end
    return result, #result > 0 and "readable-lines-may-be-incomplete" or "no-readable-lines"
end

local function ReadAuras(filter)
    if not Api.CanReadAuras() then return nil, "restricted-or-unknown" end
    if not C_UnitAuras or type(C_UnitAuras.GetAuraDataByIndex) ~= "function" then
        Api.Note("aura.data", "missing-api")
        return nil, "missing-api"
    end

    local auras = {}
    local partial = false
    for index = 1, 256 do
        local indexSecret = Api.Call("ShouldUnitAuraIndexBeSecret",
            C_Secrets and C_Secrets.ShouldUnitAuraIndexBeSecret, "boolean", "player", index, filter)
        if indexSecret == nil then return nil, "index-restriction-unknown" end
        if indexSecret then
            partial = true
        else
            local ok, raw = pcall(C_UnitAuras.GetAuraDataByIndex, "player", index, filter)
            if not ok then
                Api.Note("aura.data", "api-error")
                return nil, "api-error"
            end
            if issecretvalue(raw) then
                Api.Note("aura.data", "secret-omitted")
                return nil, "secret-omitted"
            end
            if raw == nil then return auras, partial and "partial" or "ok" end
            local readable = Api.Read("aura.data", raw, "table")
            if not readable then return nil, "invalid-aura" end

            local aura = { index = index, kind = filter }
            for _, field in ipairs({ "name", "spellId", "auraInstanceID", "duration", "expirationTime", "applications" }) do
                local value, status = Api.Field("aura." .. field, readable, field, field == "name" and "string" or "number")
                aura[field] = value
                if status ~= "ok" then partial = true end
            end
            aura.tooltipLines, aura.tooltipStatus = Api.Tooltip(index, aura.auraInstanceID, filter)
            auras[#auras + 1] = aura
        end
    end
    return auras, "partial-scan-limit"
end

function Api.Auras()
    local helpful, helpfulStatus = ReadAuras("HELPFUL")
    if not helpful then return nil, helpfulStatus end
    local harmful, harmfulStatus = ReadAuras("HARMFUL")
    if not harmful then return nil, harmfulStatus end
    for _, aura in ipairs(harmful) do helpful[#helpful + 1] = aura end
    return helpful, helpfulStatus == "ok" and harmfulStatus == "ok" and "ok" or "partial"
end
