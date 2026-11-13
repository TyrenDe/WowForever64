using System.Text.Json;
using System.Text;
using WowForever.C64.ConsoleApp;
using WowForever.C64.Core;

using var cancellation = new CancellationTokenSource();
Console.OutputEncoding = Encoding.UTF8;
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    var options = CommandOptions.Parse(args);
    if (options.Command == "help")
    {
        Console.WriteLine("""
            WoW Forever - C64 Ultimate status effects

            preview [--message "HELLO C64"]
            status
            send [--message "HELLO C64"]
            watch
            preview --snapshot samples/status-effects.json
            send --snapshot samples/status-effects.json
            preview --snapshot samples/status-effects.json --output artifacts/fantasy-dashboard.png
            charset --output artifacts/fantasy-charset.bin
            charset --output artifacts/fantasy-atlas.png

            Options: --host <C64-IP-or-hostname>, --config <JSON-file>, --timeout-seconds <1..120>
            Default settings: appsettings.json beside the executable.
            Display settings: screenAddress, charsetAddress, optional charsetFile under display.
            --charset <2KB-file> overrides charsetFile; --page <index> selects a preview/send page.
            Host precedence: --host > C64_HOST > JSON host.
            Network password: C64_PASSWORD environment variable (optional).
            Prepare the C64 at its standard BASIC screen first; see README.md.
            send pauses the CPU, uploads a RAM charset, configures text mode, and writes the cells.
            Further sends update the display while the CPU remains paused.
            watch reads the visible addon pixel stream and updates the themed status screen.
            Use windowed/borderless WoW in the foreground. No game input is sent.
            """);
        return 0;
    }

    var charset = options.Command == "status" ? null
        : options.CharsetPath is null ? FantasyCharset.Default : CharacterSet.Load(options.CharsetPath);
    if (options.Command == "charset")
    {
        var path = Path.GetFullPath(options.OutputPath!);
        if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            ScreenPreview.SavePng(path, ScreenPreview.CharsetAtlas(charset!));
        else if (Path.GetExtension(path).Equals(".bin", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, charset!.ToArray(), cancellation.Token);
        }
        else throw new ArgumentException("Charset output must end in .bin or .png.");
        Log($"EXPORTED: {path}");
        return 0;
    }

    // Validate and render before any network operations or memory writes.
    var snapshot = options.SnapshotPath is null ? null
        : StatusSnapshot.Parse(await File.ReadAllTextAsync(options.SnapshotPath, cancellation.Token));
    var screen = options.Command is "preview" or "send"
        ? snapshot is null ? TestScreenRenderer.Render(options.Message, charset) : DashboardRenderer.Render(snapshot, page: options.Page, charset: charset)
        : null;
    if (options.Command == "preview")
    {
        Console.WriteLine(screen!.ToText());
        if (options.OutputPath is not null)
        {
            ScreenPreview.SavePng(options.OutputPath, screen);
            Log($"PREVIEW: {Path.GetFullPath(options.OutputPath)} (exact glyph bytes and cell colors)");
        }
        return 0;
    }

    using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
    using var http = new HttpClient(handler)
    {
        BaseAddress = options.Endpoint,
        Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
    };
    var client = new UltimateRestClient(http, options.Password);
    if (options.Command == "watch")
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Live pixel capture requires Windows. Preview and send work without it.");
        Log("WATCHING: status effects only. Return to WoW; Ctrl+C stops the companion and leaves the C64 CPU paused.");
        await new LiveDashboard(client, new WindowsPixelSource(options.Watch.ProcessName), options.Watch, options.Vic, charset!, Log).RunAsync(cancellation.Token);
        return 0;
    }
    Log($"CONNECTING: {options.Endpoint}");
    var version = await client.GetVersionAsync(cancellation.Token);
    Log($"ONLINE: Ultimate REST API {version}");
    if (options.Command == "status")
        return 0;

    Log($"SENDING: paused character mode, charset ${options.Vic.CharsetAddress:X4}, screen ${options.Vic.ScreenAddress:X4}.");
    await new C64Display(client, options.Vic).SendAsync(screen!, cancellation.Token);
    Log("SENT: charset, VIC setup, and screen acknowledged. CPU remains paused; further sends can update the display.");
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Log("CANCELLED: the CPU may remain paused and a send may be partial. Rerun send to replace the complete screen.");
    return 130;
}
catch (OperationCanceledException)
{
    Log("OFFLINE: request timed out. Check the C64 address, REST service, and network. The CPU may remain paused and a send may be partial; rerun send after reconnecting.");
    return 1;
}
catch (HttpRequestException ex)
{
    Log($"OFFLINE: network request failed ({ex.HttpRequestError}). Check the address, REST service, and network. The CPU may remain paused and a send may be partial; rerun send after reconnecting.");
    return 1;
}
catch (UltimateApiException ex)
{
    Log($"ERROR: {ex.Message}");
    Log("If send was attempted, the CPU may remain paused. Rerun send after resolving the error.");
    return 1;
}
catch (Exception ex) when (ex is ArgumentException or JsonException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
{
    Log($"CONFIGURATION ERROR: {ex.Message}");
    return 2;
}

static void Log(string message) => Console.Error.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
