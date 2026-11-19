using System.Diagnostics;
using WowForever.C64.Core;

namespace WowForever.C64.ConsoleApp;

public sealed class LiveDashboard(IUltimateClient client, IPixelSource source, WatchOptions options,
    VicConfiguration vic, CharacterSet charset, Action<string> log)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var state = new DashboardState(TimeSpan.FromSeconds(options.StaleSeconds), options.ContextNotifications,
            TimeSpan.FromSeconds(options.LevelUpSeconds), TimeSpan.FromSeconds(options.QuestSeconds),
            TimeSpan.FromSeconds(options.ZoneSeconds));
        var clock = Stopwatch.StartNew();
        var stateLock = new object();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var capture = CaptureAsync(state, stateLock, clock, stop.Token);
        var present = PresentAsync(state, stateLock, clock, stop.Token);
        await Task.WhenAny(capture, present);
        await stop.CancelAsync();
        await Task.WhenAll(capture, present);
    }

    private async Task CaptureAsync(DashboardState state, object stateLock, Stopwatch clock, CancellationToken cancellationToken)
    {
        var decoder = new PixelProtocol();
        TimeSpan? lastCapture = null;
        string? lastSourceStatus = null;
        BridgeFrame? previous = null;
        var contextSeen = false;
        var reportStart = clock.Elapsed;
        int frames = 0, contexts = 0, bytes = 0, gaps = 0, duplicates = 0, rejected = 0, unavailable = 0;
        using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(options.PollMilliseconds));
        do
        {
            var pixels = source.Capture();
            var packet = pixels is null ? null : decoder.Decode(pixels);
            var now = clock.Elapsed;
            var sourceStatus = packet is not null || pixels is not null && lastCapture is { } recentCapture
                && now - recentCapture < TimeSpan.FromSeconds(1)
                ? "Receiving status stream" : pixels is null ? source.Status : decoder.Status;
            if (sourceStatus != lastSourceStatus)
            {
                log(sourceStatus);
                lastSourceStatus = sourceStatus;
            }
            if (packet is not null)
            {
                lastCapture = now;
                var newSession = previous is null || previous.Session != packet.Session || previous.Version != packet.Version;
                if (newSession)
                {
                    log($"WOW BRIDGE v{packet.Version}. Expected v{PixelProtocol.CurrentVersion}.");
                    if (packet.Version < PixelProtocol.CurrentVersion)
                        log("WOW ADDON UPDATE NEEDED: run scripts/Install-Addon.ps1, then type /reload in WoW.");
                    if (packet.Version >= 4)
                        log(options.ContextNotifications ? "Context notifications enabled." : "Context notifications disabled in watch settings.");
                    contextSeen = false;
                }
                if (newSession || previous!.Sequence != packet.Sequence)
                {
                    if (!newSession)
                        gaps += unchecked((ushort)(packet.Sequence - previous!.Sequence - 1));
                    previous = packet;
                    frames++;
                    bytes += packet.PayloadLength;
                    if (packet.Context is not null)
                    {
                        contexts++;
                        if (!contextSeen) log("WOW CONTEXT PACKET RECEIVED.");
                        contextSeen = true;
                    }
                    int? count;
                    lock (stateLock) count = state.Accept(packet, now);
                    if (count is { } effects) log($"STATUS SYNCED: {effects} effects.");
                }
                else duplicates++;
            }
            else if (pixels is null) unavailable++;
            else rejected++;

            if (options.Diagnostics && now - reportStart >= TimeSpan.FromSeconds(10))
            {
                log($"BRIDGE over {(now - reportStart).TotalSeconds:F1}s: {frames} advancing frames, {contexts} context, "
                    + $"{bytes} payload bytes, {gaps} skipped, {duplicates} repeated, {rejected} rejected, {unavailable} unavailable.");
                reportStart = now;
                frames = contexts = bytes = gaps = duplicates = rejected = unavailable = 0;
            }
        } while (await ticker.WaitForNextTickAsync(cancellationToken));
    }

    private async Task PresentAsync(DashboardState state, object stateLock, Stopwatch clock, CancellationToken cancellationToken)
    {
        var display = new C64Display(client, vic);
        var retryAt = TimeSpan.Zero;
        var verifyAt = TimeSpan.Zero;
        var online = false;
        byte[]? lastScreen = null;
        byte[]? lastColors = null;
        DashboardNotice? lastNotice = null;
        using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(options.PollMilliseconds));
        do
        {
            var now = clock.Elapsed;
            DashboardView view;
            lock (stateLock) view = state.View(now);
            var page = (int)(now.TotalSeconds / options.PageSeconds);
            var screen = DashboardRenderer.RenderView(view, page, charset);
            var characters = screen.ToScreenCodes();
            var colors = screen.ToColorRam();
            var verifyDue = clock.Elapsed >= verifyAt;
            if (clock.Elapsed >= retryAt && (!online || verifyDue || lastScreen is null
                || !characters.AsSpan().SequenceEqual(lastScreen) || !colors.AsSpan().SequenceEqual(lastColors)))
            {
                try
                {
                    string? version = null;
                    if (!online)
                    {
                        version = await client.GetVersionAsync(cancellationToken);
                    }
                    if (!online) display.Invalidate(resetHardware: true);
                    else if (verifyDue) await display.VerifyAsync(cancellationToken);
                    await display.SendAsync(screen, cancellationToken);
                    if (!online) log($"C64 DISPLAY ONLINE: REST API {version}");
                    if (verifyDue) verifyAt = clock.Elapsed + TimeSpan.FromSeconds(15);
                    online = true;
                    lastScreen = characters;
                    lastColors = colors;
                }
                catch (Exception ex) when (ex is UltimateApiException or HttpRequestException
                    || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    online = false;
                    log("C64 UPDATE FAILED: " + (ex is HttpRequestException network ? network.HttpRequestError.ToString() : ex.Message).TrimEnd('.')
                        + ". Retrying in five seconds.");
                    retryAt = clock.Elapsed + TimeSpan.FromSeconds(5);
                }
            }

            if (online && !ReferenceEquals(lastNotice, view.Notice))
            {
                lock (stateLock) state.Presented(view.Notice, clock.Elapsed);
                if (view.Notice is { } notice) log($"C64 NOTICE SHOWN: {notice.Kind}.");
                else if (lastNotice is not null) log(view.Auras is not null ? "C64 AURA DASHBOARD RESTORED." : "C64 NOTICE CLEARED.");
                lastNotice = view.Notice;
            }
        } while (await ticker.WaitForNextTickAsync(cancellationToken));
    }
}
