using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace WowForever.C64.Core;

public sealed class UltimateRestClient : IUltimateClient
{
    private readonly HttpClient http;
    private readonly string? password;

    public UltimateRestClient(HttpClient http, string? password = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (http.BaseAddress is null)
            throw new ArgumentException("HttpClient must have a BaseAddress.", nameof(http));
        if (password?.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException("Network password cannot contain line breaks.", nameof(password));
        this.http = http;
        this.password = password;
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "v1/version");
        using var result = await SendAsync(request, cancellationToken);
        if (!result.RootElement.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(version.GetString()))
            throw new UltimateApiException("GET v1/version returned no API version. Check the host and firmware.");
        return version.GetString()!;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        SendMachineCommandAsync("pause", cancellationToken);

    private async Task SendMachineCommandAsync(string command, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Put, $"v1/machine:{command}");
        using var result = await SendAsync(request, cancellationToken);
    }

    public async Task WriteMemoryAsync(ushort address, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (data.Length == 0 || data.Length > 65536 - address)
            throw new ArgumentOutOfRangeException(nameof(data), "Memory writes must be nonempty and cannot pass $FFFF.");

        // PUT avoids the firmware's temporary attachment files, which can stop accepting uploads.
        const int chunkSize = 128;
        for (var offset = 0; offset < data.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, data.Length - offset);
            var hex = Convert.ToHexString(data.Span.Slice(offset, length));
            using var request = CreateRequest(HttpMethod.Put,
                $"v1/machine:writemem?address={address + offset:X4}&data={hex}");
            using var result = await SendAsync(request, cancellationToken);
        }
    }

    public async Task<byte[]> ReadMemoryAsync(ushort address, int length, CancellationToken cancellationToken = default)
    {
        if (length < 1 || length > 65536 - address)
            throw new ArgumentOutOfRangeException(nameof(length), "Memory reads cannot pass $FFFF.");
        using var request = CreateRequest(HttpMethod.Get, $"v1/machine:readmem?address={address:X4}&length={length}");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await http.SendAsync(request, cancellationToken);
        await CheckStatusAsync(response, request, cancellationToken);
        if (response.Content.Headers.ContentType?.MediaType != "application/octet-stream")
            throw new UltimateApiException("Memory read did not return application/octet-stream.");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length != length) throw new UltimateApiException($"Memory read returned {bytes.Length} bytes; expected {length}.");
        return bytes;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(password))
            request.Headers.Add("X-Password", password);
        return request;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(request, cancellationToken);
        var operation = $"{request.Method} {request.RequestUri}";
        await CheckStatusAsync(response, request, cancellationToken);

        JsonDocument result;
        try
        {
            result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (JsonException)
        {
            throw new UltimateApiException($"{operation}: expected a JSON response from the Ultimate REST API.");
        }

        if (result.RootElement.ValueKind != JsonValueKind.Object
            || !result.RootElement.TryGetProperty("errors", out var errors)
            || errors.ValueKind != JsonValueKind.Array)
        {
            result.Dispose();
            throw new UltimateApiException($"{operation}: response is missing the documented errors array.");
        }

        if (errors.GetArrayLength() != 0)
        {
            var detail = string.Join("; ", errors.EnumerateArray().Select(error => error.ToString()));
            result.Dispose();
            if (!string.IsNullOrEmpty(password))
                detail = detail.Replace(password, "[redacted]", StringComparison.Ordinal);
            throw new UltimateApiException($"{operation}: device reported {detail}");
        }

        return result;
    }

    private async Task CheckStatusAsync(HttpResponseMessage response, HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = "";
            if (response.Content.Headers.ContentType?.MediaType == "application/json")
            {
                try
                {
                    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    if (body.RootElement.ValueKind == JsonValueKind.Object
                        && body.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                    {
                        detail = string.Join("; ", errors.EnumerateArray().Select(error => error.ToString()));
                        if (!string.IsNullOrEmpty(password)) detail = detail.Replace(password, "[redacted]", StringComparison.Ordinal);
                        if (detail.Length > 1024) detail = detail[..1024];
                        if (detail.Length > 0) detail = " " + detail;
                    }
                }
                catch (JsonException)
                {
                    // Preserve the HTTP failure when a device returns malformed error JSON.
                }
            }
            var hint = response.StatusCode switch
            {
                HttpStatusCode.Forbidden => " Check C64_PASSWORD and the Ultimate Network Password.",
                HttpStatusCode.NotFound => " The endpoint or requested resource is unavailable.",
                _ => " Check the device and network; a send may have partially completed."
            };
            throw new UltimateApiException($"{request.Method} {request.RequestUri}: HTTP {(int)response.StatusCode}.{detail}{hint}");
        }
    }
}
