namespace WowForever.C64.Core;

public interface IUltimateClient
{
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task<byte[]> ReadMemoryAsync(ushort address, int length, CancellationToken cancellationToken = default);
    Task WriteMemoryAsync(ushort address, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}
