namespace BeatInsight.Diagnostics.NativeLazer;

internal readonly record struct NativeLazerMemoryRegion(
    ulong BaseAddress,
    ulong Size,
    ulong AllocationBase,
    uint Protection,
    uint Type)
{
    internal bool IsPrivate => Type == NativeLazerInterop.MemoryPrivate;

    internal string TypeName => Type switch
    {
        NativeLazerInterop.MemoryPrivate => "Private",
        NativeLazerInterop.MemoryMapped => "Mapped",
        NativeLazerInterop.MemoryImage => "Image",
        _ => $"0x{Type:X}"
    };
}

internal sealed record NativeLazerMemoryRead(byte[] Data, bool Complete)
{
    internal static NativeLazerMemoryRead Failed { get; } = new([], false);
}
