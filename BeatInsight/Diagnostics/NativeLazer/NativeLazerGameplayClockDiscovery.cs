using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace BeatInsight.Diagnostics.NativeLazer;

internal enum NativeLazerGameplayClockOutcome
{
    None,
    Unique,
    Ambiguous,
    Unknown
}

internal sealed record NativeLazerGameplayClockCandidate(
    string Name,
    double? ValueMilliseconds,
    string Source);

internal sealed record NativeLazerGameplayClockMetrics(
    double DurationMs,
    long BytesRead,
    int ReadOperations,
    int PointerDereferences,
    int ChainLength);

internal sealed record NativeLazerGameplayClockResult
{
    internal required NativeLazerGenerationIdentity Generation { get; init; }
    internal NativeLazerGameplayClockOutcome Outcome { get; init; }
    internal double? CurrentTimeMilliseconds { get; init; }
    internal double? BeatmapDurationMilliseconds { get; init; }
    internal bool? IsPaused { get; init; }
    internal int? PlayingState { get; init; }
    internal ulong? PlayerAddress { get; init; }
    internal ulong? ClockContainerAddress { get; init; }
    internal ulong? GameplayClockAddress { get; init; }
    internal string? AuthoritativeSource { get; init; }
    internal string? FailureReason { get; init; }
    internal IReadOnlyList<NativeLazerGameplayClockCandidate> Candidates { get; init; } = [];
    internal required NativeLazerGameplayClockMetrics Metrics { get; init; }
}

internal interface INativeLazerGameplayClockMemory
{
    long BytesRead { get; }
    int ReadOperationCount { get; }
    byte? ReadByte(ulong address);
    int? ReadInt32(ulong address);
    long? ReadInt64(ulong address) => null;
    double? ReadDouble(ulong address);
    ulong? ReadPointer(ulong address);
}

internal sealed record NativeLazerGameplayClockLayout(
    ulong OsuGameScreenStack,
    ulong ScreenStackStack,
    ulong StackArray,
    ulong StackSize,
    ulong ArrayLength,
    ulong ArrayData,
    ulong PlayerGame,
    ulong PlayerGameplayState,
    ulong PlayerGameplayClockContainer,
    ulong PlayerPlayingState,
    ulong GameplayClockContainerClock,
    ulong GameplayClockContainerPaused,
    ulong GameplayClockContainerStartTime,
    ulong GameplayClockContainerGameplayStartTime,
    ulong FramedBeatmapClockInitialSeek,
    ulong FramedBeatmapClockDecoupledTrack,
    ulong FramedBeatmapClockInterpolatedTrack,
    ulong FramedBeatmapClockUserBeatmapOffsetClock,
    ulong FramedBeatmapClockFinalClockSource,
    ulong InterpolatingClockCurrentTime,
    ulong DecouplingClockCurrentTime,
    ulong FramedClockCurrentTime,
    ulong FramedOffsetClockOffset,
    ulong BindableBooleanValue,
    ulong BindablePlayingStateValue)
{
    internal const int PointerSize = 8;
    internal const int NullableDoubleValueOffset = 8;
}

internal sealed record NativeLazerGameplayClockStaticRelation(
    string SourceType,
    string Member,
    string TargetType,
    string Visibility,
    string? BackingField,
    string Relation,
    string Unit);

internal sealed record NativeLazerGameplayClockStaticAudit(
    NativeLazerGameplayClockLayout Layout,
    IReadOnlyList<NativeLazerGameplayClockStaticRelation> Relations,
    string AuthoritativeGetter,
    string UnitEvidence);

internal static class NativeLazerGameplayClockAssemblyInspector
{
    internal static NativeLazerGameplayClockStaticAudit Inspect(string installDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        string gamePath = Path.Combine(installDirectory, "osu.Game.dll");
        string frameworkPath = Path.Combine(installDirectory, "osu.Framework.dll");
        var context = new GameplayClockLoadContext(gamePath);
        try
        {
            Assembly game = context.LoadFromAssemblyPath(gamePath);
            Assembly framework = context.LoadFromAssemblyPath(frameworkPath);
            Type osuGame = RequireType(game, "osu.Game.OsuGame");
            Type osuScreenStack = RequireType(game, "osu.Game.Screens.OsuScreenStack");
            Type screenStack = RequireType(framework, "osu.Framework.Screens.ScreenStack");
            Type screen = RequireType(framework, "osu.Framework.Screens.IScreen");
            Type player = RequireType(game, "osu.Game.Screens.Play.Player");
            Type concretePlayer = RequireType(game, "osu.Game.Screens.Play.SoloPlayer");
            Type clockContainer = RequireType(game, "osu.Game.Screens.Play.GameplayClockContainer");
            Type concreteClockContainer = RequireType(game, "osu.Game.Screens.Play.MasterGameplayClockContainer");
            Type framedBeatmapClock = RequireType(game, "osu.Game.Beatmaps.FramedBeatmapClock");
            Type interpolatingClock = RequireType(framework, "osu.Framework.Timing.InterpolatingFramedClock");
            Type decouplingClock = RequireType(framework, "osu.Framework.Timing.DecouplingFramedClock");
            Type framedOffsetClock = RequireType(framework, "osu.Framework.Timing.FramedOffsetClock");
            Type framedClock = RequireType(framework, "osu.Framework.Timing.FramedClock");
            Type bindableBoolean = RequireType(framework, "osu.Framework.Bindables.Bindable`1")
                .MakeGenericType(typeof(bool));
            Type playingState = RequireType(game, "osu.Game.Screens.Play.LocalUserPlayingState");
            Type bindablePlayingState = RequireType(framework, "osu.Framework.Bindables.Bindable`1")
                .MakeGenericType(playingState);
            Type stackType = typeof(Stack<>).MakeGenericType(screen);

            (ulong arrayLength, ulong arrayData) = MeasureArrayLayout();
            var layout = new NativeLazerGameplayClockLayout(
                MeasureField(osuGame, "<ScreenStack>k__BackingField"),
                MeasureField(osuScreenStack, "stack"),
                MeasureField(stackType, "_array"),
                MeasureField(stackType, "_size"),
                arrayLength,
                arrayData,
                MeasureField(concretePlayer, "<game>k__BackingField"),
                MeasureField(concretePlayer, "<GameplayState>k__BackingField"),
                MeasureField(concretePlayer, "<GameplayClockContainer>k__BackingField"),
                MeasureField(concretePlayer, "playingState"),
                MeasureField(concreteClockContainer, "GameplayClock"),
                MeasureField(concreteClockContainer, "isPaused"),
                MeasureField(concreteClockContainer, "<StartTime>k__BackingField"),
                MeasureField(concreteClockContainer, "<GameplayStartTime>k__BackingField"),
                MeasureField(framedBeatmapClock, "initialSeek"),
                MeasureField(framedBeatmapClock, "decoupledTrack"),
                MeasureField(framedBeatmapClock, "interpolatedTrack"),
                MeasureField(framedBeatmapClock, "userBeatmapOffsetClock"),
                MeasureField(framedBeatmapClock, "finalClockSource"),
                MeasureField(interpolatingClock, "<CurrentTime>k__BackingField"),
                MeasureField(decouplingClock, "<CurrentTime>k__BackingField"),
                MeasureField(framedOffsetClock, "<CurrentTime>k__BackingField"),
                MeasureField(framedOffsetClock, "offset"),
                MeasureField(bindableBoolean, "value"),
                MeasureField(bindablePlayingState, "value"));

            return new(
                layout,
                BuildRelations(osuGame, screenStack, player, clockContainer, framedBeatmapClock),
                "GameplayClockContainer.CurrentTime -> FramedBeatmapClock.CurrentTime -> " +
                "initialSeek ?? finalClockSource.CurrentTime",
                "All involved osu.Framework timing APIs expose System.Double CurrentTime in milliseconds; " +
                "the live progression ratio remains required as runtime confirmation.");
        }
        finally
        {
            context.Unload();
        }
    }

    private static IReadOnlyList<NativeLazerGameplayClockStaticRelation> BuildRelations(
        Type osuGame,
        Type screenStack,
        Type player,
        Type clockContainer,
        Type framedBeatmapClock) =>
    [
        Relation(osuGame, "ScreenStack", "<ScreenStack>k__BackingField", "Direct", "n/a"),
        Relation(screenStack, "CurrentScreen", "stack", "Indirect through Stack<IScreen> top", "n/a"),
        Relation(player, "GameplayState", "<GameplayState>k__BackingField", "Direct", "n/a"),
        Relation(player, "GameplayClockContainer", "<GameplayClockContainer>k__BackingField", "Direct", "n/a"),
        Relation(clockContainer, "CurrentTime", "GameplayClock", "Getter delegates to GameplayClock", "milliseconds"),
        Relation(clockContainer, "IsPaused", "isPaused", "BindableBoolean", "boolean"),
        Relation(framedBeatmapClock, "CurrentTime", "finalClockSource", "Getter delegates to selected source", "milliseconds")
    ];

    private static NativeLazerGameplayClockStaticRelation Relation(
        Type source,
        string propertyName,
        string backingField,
        string relation,
        string unit)
    {
        PropertyInfo property = FindProperty(source, propertyName);
        FieldInfo field = FindField(source, backingField);
        return new(
            source.FullName!,
            property.Name,
            property.PropertyType.FullName ?? property.PropertyType.Name,
            Visibility(property.GetMethod),
            $"{field.DeclaringType?.FullName}.{field.Name}",
            relation,
            unit);
    }

    private static string Visibility(MethodInfo? getter) => getter switch
    {
        null => "none",
        { IsPublic: true } => "public",
        { IsFamily: true } => "protected",
        { IsAssembly: true } => "internal",
        { IsFamilyOrAssembly: true } => "protected internal",
        _ => "private"
    };

    private static Type RequireType(Assembly assembly, string name) =>
        assembly.GetType(name, throwOnError: true)!;

    private static PropertyInfo FindProperty(Type type, string name)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            PropertyInfo? property = current.GetProperty(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property is not null)
                return property;
        }
        throw new MissingMemberException(type.FullName, name);
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null)
                return field;
        }
        throw new MissingFieldException(type.FullName, name);
    }

    private static ulong MeasureField(Type concreteType, string name)
    {
        object instance = RuntimeHelpers.GetUninitializedObject(concreteType);
        FieldInfo field = FindField(concreteType, name);
        Func<object, nint> accessor = CreateFieldAddressAccessor(field);
        bool noGc = GC.TryStartNoGCRegion(1024 * 1024);
        try
        {
            nint objectAddress = Unsafe.As<object, nint>(ref instance);
            long offset = checked((long)(accessor(instance) - objectAddress));
            if (offset < 8 || offset > 0x10000)
                throw new InvalidOperationException($"Implausible field offset for {concreteType.FullName}.{name}.");
            return checked((ulong)offset);
        }
        finally
        {
            GC.KeepAlive(instance);
            if (noGc)
                GC.EndNoGCRegion();
        }
    }

    private static Func<object, nint> CreateFieldAddressAccessor(FieldInfo field)
    {
        var method = new DynamicMethod(
            $"AddressOf_{field.DeclaringType?.Name}_{field.Name}",
            typeof(nint),
            [typeof(object)],
            typeof(NativeLazerGameplayClockAssemblyInspector).Module,
            skipVisibility: true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, field.DeclaringType!);
        il.Emit(OpCodes.Ldflda, field);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<object, nint>>();
    }

    private static (ulong LengthOffset, ulong DataOffset) MeasureArrayLayout()
    {
        object? marker = new();
        object?[] one = [marker];
        object?[] seven = new object?[7];
        object oneObject = one;
        object sevenObject = seven;
        nint oneAddress = Unsafe.As<object, nint>(ref oneObject);
        nint sevenAddress = Unsafe.As<object, nint>(ref sevenObject);
        var dataAccessor = new DynamicMethod(
            "AddressOfObjectArrayData",
            typeof(nint),
            [typeof(object?[])],
            typeof(NativeLazerGameplayClockAssemblyInspector).Module,
            skipVisibility: true);
        ILGenerator il = dataAccessor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelema, typeof(object));
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Ret);
        nint firstAddress = dataAccessor.CreateDelegate<Func<object?[], nint>>()(one);
        long dataOffset = firstAddress - oneAddress;
        if (dataOffset < 12 || dataOffset > 64)
            throw new InvalidOperationException("Implausible CLR object-array data offset.");

        for (long offset = 8; offset <= dataOffset - sizeof(int); offset += sizeof(int))
        {
            int firstLength = Marshal.ReadInt32(oneAddress + checked((int)offset));
            int secondLength = Marshal.ReadInt32(sevenAddress + checked((int)offset));
            if (firstLength == one.Length && secondLength == seven.Length)
                return (checked((ulong)offset), checked((ulong)dataOffset));
        }
        throw new InvalidOperationException("CLR object-array length offset could not be measured.");
    }

    private sealed class GameplayClockLoadContext(string mainAssemblyPath)
        : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver resolver = new(mainAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? path = resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}

internal sealed class NativeLazerGameplayClockDiscovery
{
    internal NativeLazerGameplayClockResult Discover(
        NativeLazerGenerationIdentity generation,
        NativeLazerGenerationIdentity expectedGeneration,
        ulong rootAddress,
        NativeLazerGameplayClockLayout layout,
        INativeLazerGameplayClockMemory memory,
        double? beatmapDurationMilliseconds,
        Func<NativeLazerGenerationIdentity, bool> generationStillCurrent)
    {
        long started = Stopwatch.GetTimestamp();
        long initialBytes = memory.BytesRead;
        int initialOperations = memory.ReadOperationCount;
        int pointers = 0;

        NativeLazerGameplayClockResult Finish(
            NativeLazerGameplayClockOutcome outcome,
            string? reason = null,
            double? currentTime = null,
            bool? isPaused = null,
            int? playingState = null,
            ulong? player = null,
            ulong? container = null,
            ulong? clock = null,
            string? source = null,
            IReadOnlyList<NativeLazerGameplayClockCandidate>? candidates = null,
            int chainLength = 0) => new()
        {
            Generation = generation,
            Outcome = outcome,
            CurrentTimeMilliseconds = outcome == NativeLazerGameplayClockOutcome.Unique ? currentTime : null,
            BeatmapDurationMilliseconds = beatmapDurationMilliseconds,
            IsPaused = isPaused,
            PlayingState = playingState,
            PlayerAddress = player,
            ClockContainerAddress = container,
            GameplayClockAddress = clock,
            AuthoritativeSource = source,
            FailureReason = reason,
            Candidates = candidates ?? [],
            Metrics = new(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                memory.BytesRead - initialBytes,
                memory.ReadOperationCount - initialOperations,
                pointers,
                chainLength)
        };

        ulong? Pointer(ulong address)
        {
            pointers++;
            return memory.ReadPointer(address);
        }

        if (generation != expectedGeneration || !generationStillCurrent(generation))
            return Finish(NativeLazerGameplayClockOutcome.Unknown, "Process generation is not current.");
        if (rootAddress < 0x10000 || !IsObject(rootAddress, memory))
            return Finish(NativeLazerGameplayClockOutcome.Unknown, "Known runtime root is unavailable or incoherent.");
        if (beatmapDurationMilliseconds.HasValue &&
            (!double.IsFinite(beatmapDurationMilliseconds.Value) || beatmapDurationMilliseconds.Value <= 0))
            return Finish(NativeLazerGameplayClockOutcome.Unknown, "Beatmap duration is unavailable or invalid.");

        ulong? screenStack = Pointer(rootAddress + layout.OsuGameScreenStack);
        ulong? stack = screenStack is >= 0x10000 ? Pointer(screenStack.Value + layout.ScreenStackStack) : null;
        ulong? array = stack is >= 0x10000 ? Pointer(stack.Value + layout.StackArray) : null;
        int? size = stack is >= 0x10000 ? memory.ReadInt32(stack.Value + layout.StackSize) : null;
        int? capacity = array is >= 0x10000 ? memory.ReadInt32(array.Value + layout.ArrayLength) : null;
        if (!IsObject(screenStack, memory) || !IsObject(stack, memory) || !IsObject(array, memory) ||
            size is null or <= 0 or > 64 || capacity is null || capacity < size || capacity > 256)
            return Finish(NativeLazerGameplayClockOutcome.None, "No coherent active screen is present.", chainLength: 3);

        ulong topAddress = array!.Value + layout.ArrayData + checked((ulong)(size.Value - 1) * NativeLazerGameplayClockLayout.PointerSize);
        ulong? currentScreen = Pointer(topAddress);
        if (!IsObject(currentScreen, memory))
            return Finish(NativeLazerGameplayClockOutcome.None, "The active screen is unavailable.", chainLength: 4);

        ulong? gameBackReference = Pointer(currentScreen!.Value + layout.PlayerGame);
        if (gameBackReference != rootAddress)
            return Finish(NativeLazerGameplayClockOutcome.None, "The active screen is not a structurally valid Player.", player: currentScreen, chainLength: 5);

        ulong? gameplayState = Pointer(currentScreen.Value + layout.PlayerGameplayState);
        ulong? containerAddress = Pointer(currentScreen.Value + layout.PlayerGameplayClockContainer);
        ulong? playingStateBindable = Pointer(currentScreen.Value + layout.PlayerPlayingState);
        if (!IsObject(gameplayState, memory) || !IsObject(containerAddress, memory) ||
            !IsObject(playingStateBindable, memory))
            return Finish(NativeLazerGameplayClockOutcome.Unknown,
                "Player is active but its gameplay state or clock owner is incomplete.",
                player: currentScreen, container: containerAddress, chainLength: 8);

        int? playingState = memory.ReadInt32(playingStateBindable!.Value + layout.BindablePlayingStateValue);
        ulong? pauseBindable = Pointer(containerAddress!.Value + layout.GameplayClockContainerPaused);
        byte? pausedValue = IsObject(pauseBindable, memory)
            ? memory.ReadByte(pauseBindable!.Value + layout.BindableBooleanValue)
            : null;
        bool? isPaused = pausedValue is 0 or 1 ? pausedValue == 1 : null;
        if (playingState is null || isPaused is null)
            return Finish(NativeLazerGameplayClockOutcome.Unknown,
                "Gameplay state bindables are unreadable.",
                playingState: playingState,
                player: currentScreen,
                container: containerAddress,
                chainLength: 9);

        ulong? clockAddress = Pointer(containerAddress.Value + layout.GameplayClockContainerClock);
        if (!IsObject(clockAddress, memory))
            return Finish(NativeLazerGameplayClockOutcome.Unknown,
                "GameplayClockContainer has no coherent GameplayClock.",
                isPaused: isPaused,
                playingState: playingState,
                player: currentScreen,
                container: containerAddress,
                clock: clockAddress,
                chainLength: 10);

        byte? hasInitialSeek = memory.ReadByte(clockAddress!.Value + layout.FramedBeatmapClockInitialSeek);
        if (hasInitialSeek is not (0 or 1))
            return Finish(NativeLazerGameplayClockOutcome.Unknown, "Initial seek state is unreadable.",
                isPaused: isPaused, playingState: playingState, player: currentScreen,
                container: containerAddress, clock: clockAddress, chainLength: 10);
        if (hasInitialSeek == 1)
        {
            double? initialSeek = memory.ReadDouble(
                clockAddress.Value + layout.FramedBeatmapClockInitialSeek +
                NativeLazerGameplayClockLayout.NullableDoubleValueOffset);
            return Finish(NativeLazerGameplayClockOutcome.Unknown,
                $"GameplayClock is in its transient initial-seek state ({initialSeek?.ToString("F3") ?? "unreadable"} ms).",
                isPaused: isPaused, playingState: playingState, player: currentScreen,
                container: containerAddress, clock: clockAddress, chainLength: 10);
        }

        ulong? decoupled = Pointer(clockAddress.Value + layout.FramedBeatmapClockDecoupledTrack);
        ulong? interpolated = Pointer(clockAddress.Value + layout.FramedBeatmapClockInterpolatedTrack);
        ulong? offsetClock = Pointer(clockAddress.Value + layout.FramedBeatmapClockUserBeatmapOffsetClock);
        ulong? finalSource = Pointer(clockAddress.Value + layout.FramedBeatmapClockFinalClockSource);
        if (!IsObject(decoupled, memory) || !IsObject(interpolated, memory) || !IsObject(finalSource, memory))
            return Finish(NativeLazerGameplayClockOutcome.Unknown, "Clock source chain is incomplete.",
                isPaused: isPaused, playingState: playingState, player: currentScreen,
                container: containerAddress, clock: clockAddress, chainLength: 14);

        double? decoupledTime = memory.ReadDouble(decoupled!.Value + layout.DecouplingClockCurrentTime);
        double? interpolatedTime = memory.ReadDouble(interpolated!.Value + layout.InterpolatingClockCurrentTime);
        double? offsetBaseTime = IsObject(offsetClock, memory)
            ? memory.ReadDouble(offsetClock!.Value + layout.FramedClockCurrentTime)
            : null;
        double? appliedOffset = IsObject(offsetClock, memory)
            ? memory.ReadDouble(offsetClock!.Value + layout.FramedOffsetClockOffset)
            : null;
        double? offsetTime = offsetBaseTime.HasValue && appliedOffset.HasValue
            ? offsetBaseTime.Value + appliedOffset.Value
            : null;
        var candidates = new List<NativeLazerGameplayClockCandidate>
        {
            new("DecouplingFramedClock.CurrentTime", decoupledTime,
                "FramedBeatmapClock.decoupledTrack.<CurrentTime>k__BackingField"),
            new("InterpolatingFramedClock.CurrentTime", interpolatedTime,
                "FramedBeatmapClock.interpolatedTrack.<CurrentTime>k__BackingField"),
            new("FramedOffsetClock.CurrentTime", offsetTime,
                "FramedClock.<CurrentTime>k__BackingField + FramedOffsetClock.offset")
        };

        bool finalIsInterpolated = finalSource == interpolated;
        bool finalIsOffset = finalSource == offsetClock && IsObject(offsetClock, memory);
        if (finalIsInterpolated == finalIsOffset)
            return Finish(NativeLazerGameplayClockOutcome.Ambiguous,
                "finalClockSource could not be mapped to exactly one statically validated clock implementation.",
                isPaused: isPaused, playingState: playingState, player: currentScreen,
                container: containerAddress, clock: clockAddress, candidates: candidates, chainLength: 14);

        double? currentTime = finalIsInterpolated ? interpolatedTime : offsetTime;
        string source = finalIsInterpolated
            ? "GameplayClockContainer.GameplayClock.finalClockSource == interpolatedTrack"
            : "GameplayClockContainer.GameplayClock.finalClockSource == userBeatmapOffsetClock";
        if (!currentTime.HasValue || !double.IsFinite(currentTime.Value))
            return Finish(NativeLazerGameplayClockOutcome.Unknown, "Authoritative clock value is unreadable or non-finite.",
                isPaused: isPaused, playingState: playingState, player: currentScreen,
                container: containerAddress, clock: clockAddress, source: source,
                candidates: candidates, chainLength: 14);
        if (currentTime.Value < 0 ||
            (beatmapDurationMilliseconds.HasValue && currentTime.Value > beatmapDurationMilliseconds.Value))
            return Finish(NativeLazerGameplayClockOutcome.Unknown,
                beatmapDurationMilliseconds.HasValue
                    ? $"Clock value {currentTime.Value:F3} ms is outside [0, {beatmapDurationMilliseconds.Value:F3}] ms."
                    : $"Clock value {currentTime.Value:F3} ms is negative.",
                isPaused: isPaused, playingState: playingState, player: currentScreen,
                container: containerAddress, clock: clockAddress, source: source,
                candidates: candidates, chainLength: 14);
        if (!generationStillCurrent(generation))
            return Finish(NativeLazerGameplayClockOutcome.Unknown, "Process generation changed during the clock read.",
                isPaused: isPaused, playingState: playingState, player: currentScreen,
                container: containerAddress, clock: clockAddress, source: source,
                candidates: candidates, chainLength: 14);

        ulong? endingScreen = Pointer(topAddress);
        ulong? endingContainer = Pointer(currentScreen.Value + layout.PlayerGameplayClockContainer);
        ulong? endingClock = Pointer(containerAddress.Value + layout.GameplayClockContainerClock);
        if (endingScreen != currentScreen || endingContainer != containerAddress || endingClock != clockAddress)
            return Finish(NativeLazerGameplayClockOutcome.Unknown,
                "Gameplay owner or clock identity changed during the read.",
                isPaused: isPaused, playingState: playingState, player: currentScreen,
                container: containerAddress, clock: clockAddress, source: source,
                candidates: candidates, chainLength: 17);

        return Finish(NativeLazerGameplayClockOutcome.Unique,
            currentTime: currentTime,
            isPaused: isPaused,
            playingState: playingState,
            player: currentScreen,
            container: containerAddress,
            clock: clockAddress,
            source: source,
            candidates: candidates,
            chainLength: 17);
    }

    private static bool IsObject(ulong? address, INativeLazerGameplayClockMemory memory) =>
        address is >= 0x10000 && memory.ReadPointer(address.Value) is >= 0x10000;
}
