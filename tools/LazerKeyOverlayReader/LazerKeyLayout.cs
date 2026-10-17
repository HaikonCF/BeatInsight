using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace BeatInsight.Diagnostics.NativeLazer;

/// <summary>
/// Offsets de la chaîne KeyOverlay mesurés sur les assemblies de la build installée (même méthode que
/// <c>NativeLazerFastLayoutDiscovery</c> : contexte collectible, <c>ldflda</c> sur un objet non initialisé).
/// Tous les offsets sont relatifs à l'adresse de l'objet (pointeur de MethodTable compris).
/// </summary>
internal sealed record LazerKeyLayout
{
    // Chaîne Player → triggers
    public required ulong PlayerHud { get; init; }
    public required ulong PlayerRuleset { get; init; }
    public required ulong PlayerRestartCount { get; init; }
    public required ulong HudInputCountController { get; init; }
    public required ulong ControllerTriggers { get; init; }
    public required ulong BindableListCollection { get; init; }
    public required ulong ListItems { get; init; }
    public required ulong ListSize { get; init; }
    public required ulong TriggerActivationCount { get; init; }
    public required ulong TriggerIsCounting { get; init; }
    public required ulong TriggerIsActive { get; init; }
    public required ulong TriggerAction { get; init; }
    public required ulong BindableIntValue { get; init; }
    public required ulong BindableBoolValue { get; init; }

    // Chaîne Player → états pressés et liaisons
    public required ulong RulesetInputManager { get; init; }
    public required ulong InputManagerContainer { get; init; }
    public required ulong ContainerPressedInputKeys { get; init; }
    public required ulong ContainerKeyBindings { get; init; }
    public required ulong HashSetEntries { get; init; }
    public required ulong HashSetCount { get; init; }
    public required int EntryStride { get; init; }
    public required int EntryNext { get; init; }
    public required int EntryValue { get; init; }

    // Liaisons : KeyBinding (défauts) et RealmKeyBinding détachée (base de données)
    public required ulong KeyBindingKeysArray { get; init; }
    public required ulong KeyBindingAction { get; init; }
    public ulong? RealmBindingKeyCombination { get; init; }
    public ulong? RealmBindingActionInt { get; init; }

    /// <summary>Types non génériques utilisés pour la validation par MethodTable : nom complet → (module, token TypeDef).</summary>
    public required IReadOnlyDictionary<string, (string Module, int Token)> Types { get; init; }

    public required IReadOnlyDictionary<int, string> InputKeys { get; init; }
    public required IReadOnlyDictionary<int, string> OsuActions { get; init; }

    /// <summary>Valeurs InputKey des boutons de souris (pour classer K contre M).</summary>
    public required IReadOnlySet<int> MouseKeys { get; init; }

    public const string OsuGameDesktop = "osu.Desktop.OsuGameDesktop";
    public const string OsuScreenStack = "osu.Game.Screens.OsuScreenStack";
    public const string Player = "osu.Game.Screens.Play.Player";
    public const string SoloPlayer = "osu.Game.Screens.Play.SoloPlayer";
    public const string PlayerLoader = "osu.Game.Screens.Play.PlayerLoader";
    public const string HudOverlay = "osu.Game.Screens.Play.HUDOverlay";
    public const string InputCountController = "osu.Game.Screens.Play.HUD.InputCountController";
    public const string InputTrigger = "osu.Game.Screens.Play.HUD.InputTrigger";
    public const string DrawableOsuRuleset = "osu.Game.Rulesets.Osu.UI.DrawableOsuRuleset";
    public const string OsuInputManager = "osu.Game.Rulesets.Osu.OsuInputManager";
    public const string OsuKeyBindingContainer = "osu.Game.Rulesets.Osu.OsuInputManager+OsuKeyBindingContainer";
    public const string KeyBinding = "osu.Framework.Input.Bindings.KeyBinding";
    public const string RealmKeyBinding = "osu.Game.Input.Bindings.RealmKeyBinding";

    public const int LeftButton = 0;
    public const int RightButton = 1;

    public static LazerKeyLayout Measure(string installDirectory)
    {
        var context = new Context(installDirectory);
        try
        {
            Assembly Load(string file) => context.LoadFromAssemblyPath(Path.Combine(installDirectory, file));
            Assembly desktop = Load("osu!.dll");
            Assembly game = Load("osu.Game.dll");
            Assembly framework = Load("osu.Framework.dll");
            Assembly osu = Load("osu.Game.Rulesets.Osu.dll");
            static Type T(Assembly a, string n) => a.GetType(n, throwOnError: true)!;

            Type soloPlayer = T(game, SoloPlayer);
            Type hud = T(game, HudOverlay);
            Type controller = T(game, InputCountController);
            Type inputTrigger = T(game, InputTrigger);
            Type osuAction = T(osu, "osu.Game.Rulesets.Osu.OsuAction");
            Type trigger = T(game, "osu.Game.Screens.Play.HUD.KeyCounterActionTrigger`1").MakeGenericType(osuAction);
            Type bindableList = T(framework, "osu.Framework.Bindables.BindableList`1").MakeGenericType(inputTrigger);
            Type inputKey = T(framework, "osu.Framework.Input.Bindings.InputKey");
            Type keyBinding = T(framework, KeyBinding);
            Type realmBinding = T(game, RealmKeyBinding);
            Type entry = typeof(HashSet<>).GetNestedType("Entry", BindingFlags.NonPublic)!.MakeGenericType(inputKey);
            Array two = Array.CreateInstance(entry, 2);
            GCHandle pin = GCHandle.Alloc(two, GCHandleType.Pinned);
            int stride;
            try { stride = (int)(Marshal.UnsafeAddrOfPinnedArrayElement(two, 1) - Marshal.UnsafeAddrOfPinnedArrayElement(two, 0)); }
            finally { pin.Free(); }

            // RealmKeyBinding détachée (Detach()) : objet non géré, valeurs dans les champs de stockage tissés par Realm.
            ulong? realmKeys = null, realmAction = null;
            if (AllFields(realmBinding).Any(f => f.Name == "<KeyCombinationString>k__BackingField") &&
                AllFields(realmBinding).Any(f => f.Name == "<ActionInt>k__BackingField"))
            {
                realmKeys = Offset(realmBinding, "<KeyCombinationString>k__BackingField");
                realmAction = Offset(realmBinding, "<ActionInt>k__BackingField");
            }

            var types = new Dictionary<string, (string, int)>();
            void Add(Assembly a, string module, string name) => types[name] = (module, T(a, name).MetadataToken);
            Add(desktop, "osu!.dll", OsuGameDesktop);
            Add(game, "osu.Game.dll", "osu.Game.OsuGame");
            Add(game, "osu.Game.dll", OsuScreenStack);
            Add(game, "osu.Game.dll", Player);
            Add(game, "osu.Game.dll", SoloPlayer);
            Add(game, "osu.Game.dll", PlayerLoader);
            Add(game, "osu.Game.dll", HudOverlay);
            Add(game, "osu.Game.dll", InputCountController);
            Add(game, "osu.Game.dll", InputTrigger);
            Add(game, "osu.Game.dll", RealmKeyBinding);
            Add(osu, "osu.Game.Rulesets.Osu.dll", DrawableOsuRuleset);
            Add(osu, "osu.Game.Rulesets.Osu.dll", OsuInputManager);
            Add(osu, "osu.Game.Rulesets.Osu.dll", OsuKeyBindingContainer);
            Add(framework, "osu.Framework.dll", KeyBinding);

            var keyNames = new Dictionary<int, string>();
            var mouse = new HashSet<int>();
            foreach (object value in Enum.GetValues(inputKey))
            {
                int v = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                string name = value.ToString()!;
                keyNames.TryAdd(v, name);
                if (name.StartsWith("Mouse", StringComparison.Ordinal) || name.StartsWith("ExtraMouseButton", StringComparison.Ordinal))
                    mouse.Add(v);
            }

            return new LazerKeyLayout
            {
                PlayerHud = Offset(soloPlayer, "<HUDOverlay>k__BackingField"),
                PlayerRuleset = Offset(soloPlayer, "<DrawableRuleset>k__BackingField"),
                PlayerRestartCount = Offset(soloPlayer, "RestartCount"),
                HudInputCountController = Offset(hud, "InputCountController"),
                ControllerTriggers = Offset(controller, "triggers"),
                BindableListCollection = Offset(bindableList, "collection"),
                ListItems = Offset(typeof(List<>).MakeGenericType(inputTrigger), "_items"),
                ListSize = Offset(typeof(List<>).MakeGenericType(inputTrigger), "_size"),
                TriggerActivationCount = Offset(trigger, "activationCount"),
                TriggerIsCounting = Offset(trigger, "isCounting"),
                TriggerIsActive = Offset(trigger, "<IsActive>k__BackingField"),
                TriggerAction = Offset(trigger, "<Action>k__BackingField"),
                BindableIntValue = Offset(T(framework, "osu.Framework.Bindables.BindableInt"), "value"),
                BindableBoolValue = Offset(T(framework, "osu.Framework.Bindables.BindableBool"), "value"),
                RulesetInputManager = Offset(T(osu, DrawableOsuRuleset), "<KeyBindingInputManager>k__BackingField"),
                InputManagerContainer = Offset(T(osu, OsuInputManager), "KeyBindingContainer"),
                ContainerPressedInputKeys = Offset(T(osu, OsuKeyBindingContainer), "pressedInputKeys"),
                ContainerKeyBindings = Offset(T(osu, OsuKeyBindingContainer), "KeyBindings"),
                HashSetEntries = Offset(typeof(HashSet<>).MakeGenericType(inputKey), "_entries"),
                HashSetCount = Offset(typeof(HashSet<>).MakeGenericType(inputKey), "_count"),
                EntryStride = stride,
                EntryNext = (int)Marshal.OffsetOf(entry, "Next"),
                EntryValue = (int)Marshal.OffsetOf(entry, "Value"),
                KeyBindingKeysArray = OffsetPath(keyBinding, "<KeyCombination>k__BackingField", "Keys", "array"),
                KeyBindingAction = Offset(keyBinding, "<Action>k__BackingField"),
                RealmBindingKeyCombination = realmKeys,
                RealmBindingActionInt = realmAction,
                Types = types,
                InputKeys = keyNames,
                OsuActions = Enum.GetValues(osuAction).Cast<object>().ToDictionary(v => Convert.ToInt32(v, CultureInfo.InvariantCulture), v => v.ToString()!),
                MouseKeys = mouse,
            };
        }
        finally
        {
            context.Unload();
        }
    }

    private static IEnumerable<FieldInfo> AllFields(Type type)
    {
        for (Type? t = type; t is not null; t = t.BaseType)
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                yield return f;
    }

    private static ulong Offset(Type concrete, string name) =>
        Offset(concrete, AllFields(concrete).FirstOrDefault(f => f.Name == name) ?? throw new MissingFieldException(concrete.FullName, name));

    private static ulong Offset(Type concrete, FieldInfo field) => OffsetPath(concrete, [field]);

    /// <summary>Offset d'un champ imbriqué dans des structs (ex. KeyBinding.KeyCombination.Keys.array).</summary>
    private static ulong OffsetPath(Type concrete, params string[] path)
    {
        var fields = new List<FieldInfo>();
        Type current = concrete;
        foreach (string name in path)
        {
            FieldInfo f = AllFields(current).FirstOrDefault(x => x.Name == name) ?? throw new MissingFieldException(current.FullName, name);
            fields.Add(f);
            current = f.FieldType;
        }
        return OffsetPath(concrete, fields);
    }

    private static ulong OffsetPath(Type concrete, IReadOnlyList<FieldInfo> path)
    {
        object instance = RuntimeHelpers.GetUninitializedObject(concrete);
        var method = new DynamicMethod("addr", typeof(nint), [typeof(object)], typeof(LazerKeyLayout).Module, skipVisibility: true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, path[0].DeclaringType!);
        foreach (FieldInfo f in path) il.Emit(OpCodes.Ldflda, f);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Ret);
        var accessor = method.CreateDelegate<Func<object, nint>>();
        bool noGc = GC.TryStartNoGCRegion(1024 * 1024);
        try
        {
            long offset = accessor(instance) - Unsafe.As<object, nint>(ref instance);
            if (offset is < 8 or > 0x4000)
                throw new InvalidOperationException($"Offset implausible pour {concrete.FullName}.{string.Join('.', path.Select(f => f.Name))}.");
            return (ulong)offset;
        }
        finally
        {
            GC.KeepAlive(instance);
            if (noGc) GC.EndNoGCRegion();
        }
    }

    private sealed class Context(string directory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name is null || name.Name.StartsWith("System.", StringComparison.Ordinal)) return null;
            string candidate = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }
}
