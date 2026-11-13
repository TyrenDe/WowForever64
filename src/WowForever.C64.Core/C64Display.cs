namespace WowForever.C64.Core;

public sealed class C64Display
{
    private readonly IUltimateClient client;
    private readonly VicConfiguration configuration;
    private readonly VicController vic;
    private byte[]? lastCharacters;
    private byte[]? lastColors;
    private byte[]? lastCharset;
    private (byte Border, byte Background)? lastPalette;
    private bool configured;

    public C64Display(IUltimateClient client, VicConfiguration? configuration = null)
    {
        this.client = client;
        this.configuration = configuration ?? new VicConfiguration();
        this.configuration.Validate();
        vic = new VicController(client, this.configuration);
    }

    public void Invalidate(bool resetHardware = false)
    {
        lastCharacters = null;
        lastColors = null;
        lastPalette = null;
        configured = false;
        if (resetHardware) lastCharset = null;
    }

    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        if (!configured || lastCharacters is null || lastCharset is null || lastPalette is null) return;
        try
        {
            await client.PauseAsync(cancellationToken);
            var bank = await client.ReadMemoryAsync(0xDD00, 1, cancellationToken);
            var mode = await client.ReadMemoryAsync(0xD011, 1, cancellationToken);
            var columns = await client.ReadMemoryAsync(0xD016, 1, cancellationToken);
            var pointer = await client.ReadMemoryAsync(0xD018, 1, cancellationToken);
            var palette = await client.ReadMemoryAsync(0xD020, 2, cancellationToken);
            var firstCell = await client.ReadMemoryAsync((ushort)configuration.ScreenAddress, 1, cancellationToken);
            var font = await client.ReadMemoryAsync((ushort)configuration.CharsetAddress, 8, cancellationToken);
            if ((bank[0] & 3) != 3 - configuration.Bank || (mode[0] & 0x7F) != 0x1B
                || (columns[0] & 0x1F) != 8 || (pointer[0] & 0xFE) != configuration.MemoryPointer
                || (palette[0] & 15) != lastPalette.Value.Border || (palette[1] & 15) != lastPalette.Value.Background
                || firstCell[0] != lastCharacters[0] || !font.AsSpan().SequenceEqual(lastCharset.AsSpan(0, 8)))
                Invalidate(resetHardware: true);
        }
        catch
        {
            Invalidate(resetHardware: true);
            throw;
        }
    }

    public async Task SendAsync(TextScreen screen, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var characters = screen.ToScreenCodes();
        var colors = screen.ToColorRam();
        var charset = screen.CharacterSet.ToArray();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(screen.BorderColor, (byte)15);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(screen.BackgroundColor, (byte)15);
        var uploadCharset = lastCharset is null || !charset.AsSpan().SequenceEqual(lastCharset);
        var initialize = !configured || uploadCharset;
        try
        {
            await client.PauseAsync(cancellationToken);
            if (initialize)
            {
                await vic.BeginAsync(cancellationToken);
                if (uploadCharset)
                    await client.WriteMemoryAsync((ushort)configuration.CharsetAddress, charset, cancellationToken);
                await vic.ConfigureAsync(screen.BorderColor, screen.BackgroundColor, cancellationToken);
                lastCharacters = null;
                lastColors = null;
            }
            else if (lastPalette != (screen.BorderColor, screen.BackgroundColor))
            {
                await client.WriteMemoryAsync(0xD020, new byte[] { screen.BorderColor, screen.BackgroundColor }, cancellationToken);
            }
            await WriteChangesAsync(0xD800, colors, lastColors, cancellationToken);
            await WriteChangesAsync((ushort)configuration.ScreenAddress, characters, lastCharacters, cancellationToken);
            if (initialize) await vic.ShowAsync(cancellationToken);
            lastCharacters = characters;
            lastColors = colors;
            lastCharset = charset;
            lastPalette = (screen.BorderColor, screen.BackgroundColor);
            configured = true;
        }
        catch
        {
            Invalidate(resetHardware: true);
            throw;
        }
    }

    private async Task WriteChangesAsync(ushort address, byte[] data, byte[]? previous, CancellationToken cancellationToken)
    {
        if (previous is null)
        {
            await client.WriteMemoryAsync(address, data, cancellationToken);
            return;
        }
        var runs = new List<(int Offset, int Count)>();
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] == previous[i]) continue;
            if (runs.Count > 0 && i - (runs[^1].Offset + runs[^1].Count) <= 4)
                runs[^1] = (runs[^1].Offset, i - runs[^1].Offset + 1);
            else runs.Add((i, 1));
        }
        // Coalesce small gaps; a full buffer is cheaper than many fragmented requests.
        if (runs.Count > 12 || runs.Sum(run => run.Count) > 600) runs = [(0, data.Length)];
        foreach (var run in runs)
            await client.WriteMemoryAsync((ushort)(address + run.Offset), data.AsMemory(run.Offset, run.Count), cancellationToken);
    }
}
