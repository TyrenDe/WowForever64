namespace WowForever.C64.Core;

public sealed record VicConfiguration
{
    public int ScreenAddress { get; init; } = 0x0400;
    public int CharsetAddress { get; init; } = 0x2000;
    public int Bank => ScreenAddress / 0x4000;
    public byte MemoryPointer => (byte)(((ScreenAddress % 0x4000) / 0x0400 << 4)
        | ((CharsetAddress % 0x4000) / 0x0800 << 1));

    public void Validate()
    {
        if (ScreenAddress < 0 || ScreenAddress > 0xFC00 || ScreenAddress % 0x0400 != 0
            || CharsetAddress < 0 || CharsetAddress > 0xF800 || CharsetAddress % 0x0800 != 0)
            throw new ArgumentException("Screen RAM must be 1 KB aligned and charset RAM 2 KB aligned within 64 KB.");
        if (Bank != CharsetAddress / 0x4000)
            throw new ArgumentException("Screen and charset must occupy the same 16 KB VIC bank.");
        if (!WritableRam(ScreenAddress, 1024) || !WritableRam(CharsetAddress, 2048))
            throw new ArgumentException("Display RAM must be in $0400-$9FFF or $C000-$CFFF, outside CPU ROM/I/O and system pages.");
        if (ScreenAddress < CharsetAddress + 2048 && CharsetAddress < ScreenAddress + 1024)
            throw new ArgumentException("Screen RAM and charset RAM cannot overlap.");
        if (Bank is 0 or 2)
        {
            var shadow = Bank * 0x4000 + 0x1000;
            if (Overlaps(ScreenAddress, 1024, shadow, 4096) || Overlaps(CharsetAddress, 2048, shadow, 4096))
                throw new ArgumentException("Avoid VIC character-ROM shadows at $1000-$1FFF and $9000-$9FFF.");
        }
    }

    private static bool WritableRam(int address, int length) =>
        address >= 0x0400 && address + length <= 0xA000 || address >= 0xC000 && address + length <= 0xD000;

    private static bool Overlaps(int a, int lengthA, int b, int lengthB) => a < b + lengthB && b < a + lengthA;
}

public sealed class VicController(IUltimateClient client, VicConfiguration configuration)
{
    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        var port = await client.ReadMemoryAsync(0xDD00, 1, cancellationToken);
        var direction = await client.ReadMemoryAsync(0xDD02, 1, cancellationToken);
        // Hide the old display before changing its bank or character pointers.
        await client.WriteMemoryAsync(0xD011, new byte[] { 0x0B }, cancellationToken);
        // CIA2 bank bits are inverted. Preserve unrelated serial/user-port bits.
        await client.WriteMemoryAsync(0xDD00, new byte[] { (byte)((port[0] & 0xFC) | (3 - configuration.Bank)) }, cancellationToken);
        await client.WriteMemoryAsync(0xDD02, new byte[] { (byte)(direction[0] | 3) }, cancellationToken);
    }

    public async Task ConfigureAsync(byte border, byte background, CancellationToken cancellationToken)
    {
        await client.WriteMemoryAsync(0xD016, new byte[] { 0x08 }, cancellationToken);
        await client.WriteMemoryAsync(0xD015, new byte[] { 0x00 }, cancellationToken);
        await client.WriteMemoryAsync(0xD018, new byte[] { configuration.MemoryPointer }, cancellationToken);
        await client.WriteMemoryAsync(0xD020, new byte[] { border, background }, cancellationToken);
    }

    public Task ShowAsync(CancellationToken cancellationToken) =>
        client.WriteMemoryAsync(0xD011, new byte[] { 0x1B }, cancellationToken);
}
