using BeatInsight.Diagnostics.NativeStable;

// Copies conformes (données seules) des types définis dans des fichiers BeatInsight
// trop couplés pour être liés (NativeStableGameProvider.cs, NativeStableRootCharacterization.cs).
// Aucune logique ici : la construction du contexte reproduit NativeStableGameRuntime.TryOpen.

namespace BeatInsight.Diagnostics.NativeStable
{
    internal sealed record NativeStableModuleRange(
        string Name, string Path, nuint BaseAddress, nuint Size)
    {
        internal bool Contains(nuint address) =>
            address >= BaseAddress && address - BaseAddress < Size;
    }
}

namespace BeatInsight.Integration.Game
{
    internal sealed class NativeStableGameProcessContext : IDisposable
    {
        internal NativeStableGameProcessContext(
            int processId,
            DateTimeOffset processStartIdentity,
            string executablePath,
            INativeStableMemoryReader memory,
            IReadOnlyList<NativeStableMemoryRegion> regions,
            IReadOnlyList<NativeStableModuleRange> modules,
            Action? dispose = null)
        {
            ProcessId = processId;
            ProcessStartIdentity = processStartIdentity;
            ExecutablePath = executablePath;
            Memory = memory;
            Regions = regions;
            Modules = modules;
            this.dispose = dispose;
        }

        private readonly Action? dispose;

        internal int ProcessId { get; }

        internal DateTimeOffset ProcessStartIdentity { get; }

        internal string ExecutablePath { get; }

        internal INativeStableMemoryReader Memory { get; }

        internal IReadOnlyList<NativeStableMemoryRegion> Regions { get; }

        internal IReadOnlyList<NativeStableModuleRange> Modules { get; }

        public void Dispose()
        {
            if (Memory is IDisposable disposable)
            {
                disposable.Dispose();
            }
            dispose?.Invoke();
        }
    }
}
