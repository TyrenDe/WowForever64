namespace WowForever.C64.Core;

public interface IPixelSource
{
    string Status { get; }
    PixelFrame? Capture();
}
