using System.Diagnostics;
using WowForever.C64.Core;

namespace WowForever.C64.ConsoleApp;

public sealed class LiveDashboard(IUltimateClient client, IPixelSource source, WatchOptions options,
    VicConfiguration vic, CharacterSet charset, Action<string> log)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var decoder = new PixelProtocol();
        var assembler = new SnapshotAssembler();
        var display = new C64Display(client, vic);
        var clock = Stopwatch.StartNew();
        StatusSnapshot? snapshot = null;
        BridgeFrame? lastFrame = null;
        TimeSpan? lastHeartbeat = null;
        TimeSpan? lastCapture = null;
        var retryAt = TimeSpan.Zero;
        var verifyAt = TimeSpan.Zero;
        var online = false;
        string? lastSourceStatus = null;
        byte[]? lastScreen = null;
        byte[]? lastColors = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var pixels = source.Capture();
            var packet = pixels is null ? null : decoder.Decode(pixels);
            var sourceStatus = packet is not null || pixels is not null && lastCapture is { } recentCapture
                && clock.Elapsed - recentCapture < TimeSpan.FromSeconds(1)
                ? "Receiving status stream" : pixels is null ? source.Status : decoder.Status;
            if (sourceStatus != lastSourceStatus)
            {
                log(sourceStatus);
                lastSourceStatus = sourceStatus;
            }
            if (packet is not null)
            {
                lastCapture = clock.Elapsed;
                if (lastFrame is null || packet.Session != lastFrame.Session || packet.Sequence != lastFrame.Sequence)
                {
                    lastHeartbeat = clock.Elapsed;
                    lastFrame = packet;
                    var updated = assembler.Accept(packet);
                    if (updated is not null)
                    {
                        if (snapshot is null) log($"STATUS SYNCED: {updated.Auras.Count} effects.");
                        snapshot = updated;
                    }
                    else if (snapshot is not null)
                        snapshot = snapshot with { Live = false, InCombat = (packet.Flags & 1) != 0, Status = "SYNCING EFFECTS" };
                }
            }
            var fresh = lastFrame is not null
                && lastHeartbeat is { } heartbeat && lastCapture is { } capture
                && clock.Elapsed - heartbeat <= TimeSpan.FromSeconds(options.StaleSeconds)
                && clock.Elapsed - capture <= TimeSpan.FromSeconds(options.StaleSeconds);
            var elapsed = lastHeartbeat is { } receivedAt ? (clock.Elapsed - receivedAt).TotalSeconds : 0;
            var page = (int)(clock.Elapsed.TotalSeconds / options.PageSeconds);
            var screen = DashboardRenderer.Render(snapshot, fresh, elapsed, page, lastFrame?.ClockMilliseconds, charset);
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
            await Task.Delay(options.PollMilliseconds, cancellationToken);
        }
    }
}
