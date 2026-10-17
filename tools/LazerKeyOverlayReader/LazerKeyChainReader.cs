using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace BeatInsight.Diagnostics.NativeLazer;

/// <summary>MethodTables des types non génériques de la build en cours (table TypeDef → MethodTable, sans tas).</summary>
internal interface ILazerMethodTables
{
    /// <summary>MethodTable du type, ou null s'il n'est pas (encore) chargé dans le processus.</summary>
    ulong? Get(string typeName);

    /// <summary>Demande une relecture (asynchrone, bornée) quand un type attendu manque.</summary>
    void RequestRefresh();
}

/// <summary>Résultat d'une lecture de la chaîne, avant l'identification de la partie.</summary>
internal sealed record LazerChainRead(
    LazerKeyValidity Validity,
    LazerKeyFailure Failure,
    ulong? Player,
    int? RestartCount,
    string? PlayerType,
    LazerActionCounter Left,
    LazerActionCounter Right,
    LazerPressedStates? Pressed,
    int Reads,
    double ReadMs);

/// <summary>
/// V3.14-COM-LAZER-KEY-02 : une lecture complète, fail-closed, de la chaîne KeyOverlay d'osu!lazer. Aucune écriture.
/// <code>
/// OsuGameDesktop (racine validée par NativeLazer) → ScreenStack (NativeLazerScreenStackReader) → SoloPlayer
///   → HUDOverlay → InputCountController → triggers → KeyCounterActionTrigger&lt;OsuAction&gt; (identifiés par leur Action)
///   → DrawableOsuRuleset → OsuInputManager → OsuKeyBindingContainer → pressedInputKeys + KeyBindings
/// </code>
/// Chaque objet est validé par sa hiérarchie de MethodTables ; collections et compteurs bornés ; la chaîne est relue à la
/// fin pour vérifier qu'elle n'a pas changé pendant la lecture. Toute incohérence donne Unknown, jamais une valeur.
/// </summary>
internal sealed class LazerKeyChainReader(
    LazerKeyLayout layout,
    NativeLazerGameplayClockLayout stackLayout,
    ILazerMethodTables methodTables)
{
    public const int MaximumTriggers = 8;
    public const int MaximumCount = 1_000_000;
    public const int MaximumPressedKeys = 64;
    public const int MaximumBindings = 64;
    private const ulong ParentOffset = 0x10; // MethodTable.m_pParentMethodTable (CoreCLR x64)

    private readonly NativeLazerScreenStackReader stackReader = new();
    private BindingCache? bindings;

    /// <summary>Diagnostic : pourquoi les liaisons n'ont pas pu être lues (null si elles l'ont été).</summary>
    public string? BindingDiagnostic { get; private set; }

    public LazerChainRead Read(
        INativeLazerGameplayClockMemory memory,
        NativeLazerGenerationIdentity generation,
        ulong root,
        Func<NativeLazerGenerationIdentity, bool> stillCurrent)
    {
        long started = Stopwatch.GetTimestamp();
        int startReads = memory.ReadOperationCount;
        ulong? player = null;
        int? restart = null;
        string? playerType = null;
        LazerChainRead Done(LazerKeyValidity validity, LazerKeyFailure failure,
            LazerActionCounter? left = null, LazerActionCounter? right = null, LazerPressedStates? pressed = null) =>
            new(validity, failure, player, restart, playerType,
                left ?? LazerKeyOverlaySample.UnknownCounter("LeftButton"),
                right ?? LazerKeyOverlaySample.UnknownCounter("RightButton"),
                pressed, memory.ReadOperationCount - startReads, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        if (methodTables.Get(LazerKeyLayout.OsuGameDesktop) is not { } gameMt)
            return Missing(LazerKeyFailure.MethodTablesUnavailable);
        if (memory.ReadPointer(root) != gameMt)
            return Done(LazerKeyValidity.Unknown, LazerKeyFailure.RootMoved);

        NativeLazerScreenStackSample stack = stackReader.Read(generation, generation, root, stackLayout, memory, stillCurrent);
        if (stack.Outcome != NativeLazerScreenStackOutcome.Unique || stack.Entries.LastOrDefault(e => e.IsTop) is not { } top)
            return Done(LazerKeyValidity.Unknown, LazerKeyFailure.ScreenStackUnavailable);

        playerType = $"MT 0x{top.MethodTable:X}";
        switch (Is(memory, top.ObjectAddress, LazerKeyLayout.Player))
        {
            case null: return Missing(LazerKeyFailure.MethodTablesUnavailable);
            case false:
                return Done(LazerKeyValidity.NotGameplay,
                    Is(memory, top.ObjectAddress, LazerKeyLayout.PlayerLoader) == true ? LazerKeyFailure.PlayerLoading : LazerKeyFailure.NotInGameplay);
        }
        player = top.ObjectAddress;
        // Replay, autoplay, spectate, multijoueur : autres types de Player, non pris en charge dans cette passe.
        switch (Is(memory, top.ObjectAddress, LazerKeyLayout.SoloPlayer))
        {
            case null: return Missing(LazerKeyFailure.MethodTablesUnavailable);
            case false: return Done(LazerKeyValidity.Unsupported, LazerKeyFailure.UnsupportedPlayer);
        }
        playerType = "SoloPlayer";
        restart = memory.ReadInt32(top.ObjectAddress + layout.PlayerRestartCount);
        if (restart is null or < 0 or > MaximumCount)
            return Done(LazerKeyValidity.Unknown, LazerKeyFailure.HudUnavailable);

        // Ruleset osu!standard uniquement.
        ulong? ruleset = memory.ReadPointer(top.ObjectAddress + layout.PlayerRuleset);
        if (ruleset is not >= 0x10000)
            return Done(LazerKeyValidity.NotGameplay, LazerKeyFailure.PlayerLoading);
        switch (Is(memory, ruleset.Value, LazerKeyLayout.DrawableOsuRuleset))
        {
            case null: return Missing(LazerKeyFailure.MethodTablesUnavailable);
            case false: return Done(LazerKeyValidity.Unsupported, LazerKeyFailure.UnsupportedRuleset);
        }

        ulong? hud = memory.ReadPointer(top.ObjectAddress + layout.PlayerHud);
        if (hud is not >= 0x10000)
            return Done(LazerKeyValidity.NotGameplay, LazerKeyFailure.PlayerLoading);
        if (Is(memory, hud.Value, LazerKeyLayout.HudOverlay) is not true)
            return Fail(memory, hud.Value, LazerKeyLayout.HudOverlay, LazerKeyFailure.HudUnavailable);
        ulong? controller = memory.ReadPointer(hud.Value + layout.HudInputCountController);
        if (controller is not >= 0x10000 || Is(memory, controller.Value, LazerKeyLayout.InputCountController) is not true)
            return controller is >= 0x10000 && Is(memory, controller.Value, LazerKeyLayout.InputCountController) is null
                ? Missing(LazerKeyFailure.MethodTablesUnavailable)
                : Done(LazerKeyValidity.Unknown, LazerKeyFailure.HudUnavailable);

        // Liste des triggers.
        ulong? bindable = memory.ReadPointer(controller.Value + layout.ControllerTriggers);
        ulong? list = bindable is >= 0x10000 ? memory.ReadPointer(bindable.Value + layout.BindableListCollection) : null;
        ulong? items = list is >= 0x10000 ? memory.ReadPointer(list.Value + layout.ListItems) : null;
        int? size = list is >= 0x10000 ? memory.ReadInt32(list.Value + layout.ListSize) : null;
        int? capacity = items is >= 0x10000 ? memory.ReadInt32(items.Value + 8) : null;
        if (!IsObject(memory, bindable) || !IsObject(memory, list) || !IsObject(memory, items) ||
            size is null || capacity is null || size < 0 || size > MaximumTriggers || capacity < size || capacity > 1024)
            return Done(LazerKeyValidity.Unknown, LazerKeyFailure.TriggerCollectionInvalid);
        if (size == 0)
            return Done(LazerKeyValidity.Unknown, LazerKeyFailure.TriggersNotReady);

        // Triggers identifiés par leur Action, jamais par leur position.
        ulong? inputTriggerMt = methodTables.Get(LazerKeyLayout.InputTrigger);
        if (inputTriggerMt is null) return Missing(LazerKeyFailure.MethodTablesUnavailable);
        ulong? triggerMt = null;
        LazerActionCounter? left = null, right = null;
        for (int i = 0; i < size; i++)
        {
            ulong? trigger = memory.ReadPointer(items!.Value + 16 + (ulong)i * 8);
            ulong? mt = trigger is >= 0x10000 ? memory.ReadPointer(trigger.Value) : null;
            // KeyCounterActionTrigger<OsuAction> dérive directement d'InputTrigger ; tous les triggers ont le même type fermé.
            ulong? parent = mt is >= 0x10000 ? memory.ReadPointer(mt.Value + ParentOffset) : null;
            if (mt is not >= 0x10000 || parent != inputTriggerMt || (triggerMt is not null && mt != triggerMt))
                return Done(LazerKeyValidity.Unknown, LazerKeyFailure.TriggerTypeInvalid);
            triggerMt = mt;

            int? action = memory.ReadInt32(trigger!.Value + layout.TriggerAction);
            if (action is null || !layout.OsuActions.ContainsKey(action.Value))
                return Done(LazerKeyValidity.Unknown, LazerKeyFailure.TriggerActionInvalid);
            if (action is not (LazerKeyLayout.LeftButton or LazerKeyLayout.RightButton))
                continue; // Smoke : hors KeyOverlay

            LazerActionCounter? counter = ReadCounter(memory, trigger.Value, layout.OsuActions[action.Value]);
            if (counter is null)
                return Done(LazerKeyValidity.Unknown, LazerKeyFailure.CountOutOfRange);
            if (action == LazerKeyLayout.LeftButton)
            {
                if (left is not null) return Done(LazerKeyValidity.Unknown, LazerKeyFailure.TriggerActionDuplicated);
                left = counter;
            }
            else
            {
                if (right is not null) return Done(LazerKeyValidity.Unknown, LazerKeyFailure.TriggerActionDuplicated);
                right = counter;
            }
        }
        if (left is null || right is null)
            return Done(LazerKeyValidity.Unknown, LazerKeyFailure.TriggerActionMissing);

        LazerPressedStates? pressed = ReadPressed(memory, ruleset.Value);

        // Cohérence : la chaîne n'a pas changé pendant la lecture (même écran, même HUD, même liste, même taille).
        NativeLazerScreenStackSample after = stackReader.Read(generation, generation, root, stackLayout, memory, stillCurrent);
        if (after.Outcome != NativeLazerScreenStackOutcome.Unique ||
            after.Entries.LastOrDefault(e => e.IsTop)?.ObjectAddress != top.ObjectAddress ||
            memory.ReadPointer(top.ObjectAddress + layout.PlayerHud) != hud ||
            memory.ReadPointer(hud.Value + layout.HudInputCountController) != controller ||
            memory.ReadPointer(list!.Value + layout.ListItems) != items ||
            memory.ReadInt32(list.Value + layout.ListSize) != size ||
            memory.ReadPointer(root) != gameMt)
            return Done(LazerKeyValidity.Unknown, LazerKeyFailure.ChainChangedDuringRead);

        return Done(LazerKeyValidity.Valid, LazerKeyFailure.None, left, right, pressed);

        LazerChainRead Missing(LazerKeyFailure failure)
        {
            methodTables.RequestRefresh();
            return Done(LazerKeyValidity.Unknown, failure);
        }

        LazerChainRead Fail(INativeLazerGameplayClockMemory m, ulong obj, string type, LazerKeyFailure failure) =>
            Is(m, obj, type) is null ? Missing(LazerKeyFailure.MethodTablesUnavailable) : Done(LazerKeyValidity.Unknown, failure);
    }

    private LazerActionCounter? ReadCounter(INativeLazerGameplayClockMemory memory, ulong trigger, string action)
    {
        ulong? activation = memory.ReadPointer(trigger + layout.TriggerActivationCount);
        ulong? counting = memory.ReadPointer(trigger + layout.TriggerIsCounting);
        if (!IsObject(memory, activation) || !IsObject(memory, counting)) return null;
        int? count = memory.ReadInt32(activation!.Value + layout.BindableIntValue);
        byte? active = memory.ReadByte(trigger + layout.TriggerIsActive);
        byte? isCounting = memory.ReadByte(counting!.Value + layout.BindableBoolValue);
        if (count is null or < 0 or > MaximumCount || active is not (0 or 1) || isCounting is not (0 or 1)) return null;
        return new LazerActionCounter(action, LazerCounterAvailability.Native, count, active == 1, isCounting == 1);
    }

    /// <summary>
    /// États pressés : touches de <c>pressedInputKeys</c>, classées par les liaisons réelles du conteneur (une touche seule
    /// liée à LeftButton/RightButton). Sans liaison lisible, K1..M2 restent inconnus (null), seules les touches brutes sont rendues.
    /// </summary>
    private LazerPressedStates? ReadPressed(INativeLazerGameplayClockMemory memory, ulong ruleset)
    {
        ulong? manager = memory.ReadPointer(ruleset + layout.RulesetInputManager);
        if (manager is not >= 0x10000 || Is(memory, manager.Value, LazerKeyLayout.OsuInputManager) is not true) return null;
        ulong? container = memory.ReadPointer(manager.Value + layout.InputManagerContainer);
        if (container is not >= 0x10000 || Is(memory, container.Value, LazerKeyLayout.OsuKeyBindingContainer) is not true) return null;

        ulong? set = memory.ReadPointer(container.Value + layout.ContainerPressedInputKeys);
        if (!IsObject(memory, set)) return null;
        int? used = memory.ReadInt32(set!.Value + layout.HashSetCount);
        if (used is null or < 0 or > MaximumPressedKeys) return null;
        var keys = new List<int>();
        if (used > 0)
        {
            ulong? entries = memory.ReadPointer(set.Value + layout.HashSetEntries);
            int? length = entries is >= 0x10000 ? memory.ReadInt32(entries.Value + 8) : null;
            if (length is null || length < used) return null;
            for (int i = 0; i < used; i++)
            {
                ulong e = entries!.Value + 16 + (ulong)(i * layout.EntryStride);
                int? next = memory.ReadInt32(e + (ulong)layout.EntryNext);
                if (next is null) return null;
                if (next < -1) continue; // entrée libérée
                if (memory.ReadInt32(e + (ulong)layout.EntryValue) is not { } key) return null;
                keys.Add(key);
            }
        }
        keys.Sort();
        IReadOnlyList<string> raw = keys.Select(k => layout.InputKeys.TryGetValue(k, out string? n) ? n : k.ToString(CultureInfo.InvariantCulture)).ToArray();

        IReadOnlyDictionary<int, int>? map = ReadBindings(memory, container.Value);
        if (map is null) return new LazerPressedStates(null, null, null, null, raw);
        bool Pressed(int action, bool mouse) =>
            keys.Any(k => layout.MouseKeys.Contains(k) == mouse && map.TryGetValue(k, out int a) && a == action);
        return new LazerPressedStates(
            Pressed(LazerKeyLayout.LeftButton, mouse: false),
            Pressed(LazerKeyLayout.RightButton, mouse: false),
            Pressed(LazerKeyLayout.LeftButton, mouse: true),
            Pressed(LazerKeyLayout.RightButton, mouse: true),
            raw);
    }

    /// <summary>Touche seule → action, depuis <c>KeyBindings</c>. Relu seulement si la liste change d'adresse ou de taille.</summary>
    private IReadOnlyDictionary<int, int>? ReadBindings(INativeLazerGameplayClockMemory memory, ulong container)
    {
        ulong? list = memory.ReadPointer(container + layout.ContainerKeyBindings);
        if (!IsObject(memory, list)) return Why("liste KeyBindings illisible");
        ulong? items = memory.ReadPointer(list!.Value + layout.ListItems);
        int? size = memory.ReadInt32(list.Value + layout.ListSize);
        if (!IsObject(memory, items) || size is null or < 0 or > MaximumBindings) return Why($"items/size invalides ({size})");
        if (bindings is { } cached && cached.List == list && cached.Items == items && cached.Size == size)
            return cached.Map;

        var map = new Dictionary<int, int>();
        ulong? keyBindingMt = methodTables.Get(LazerKeyLayout.KeyBinding);
        ulong? realmMt = methodTables.Get(LazerKeyLayout.RealmKeyBinding);
        for (int i = 0; i < size; i++)
        {
            ulong? binding = memory.ReadPointer(items!.Value + 16 + (ulong)i * 8);
            ulong? mt = binding is >= 0x10000 ? memory.ReadPointer(binding.Value) : null;
            if (mt is null) return Why($"liaison {i} illisible");
            int? action;
            int[]? combo;
            if (mt == keyBindingMt)
            {
                ulong? boxed = memory.ReadPointer(binding!.Value + layout.KeyBindingAction);
                action = boxed is >= 0x10000 ? memory.ReadInt32(boxed.Value + 8) : null;
                combo = ReadIntArray(memory, memory.ReadPointer(binding.Value + layout.KeyBindingKeysArray));
            }
            else if (mt == realmMt && layout.RealmBindingKeyCombination is { } keysOffset && layout.RealmBindingActionInt is { } actionOffset)
            {
                action = memory.ReadInt32(binding!.Value + actionOffset);
                combo = ParseCombination(ReadString(memory, memory.ReadPointer(binding.Value + keysOffset)));
            }
            else
            {
                return Why($"type de liaison inconnu MT 0x{mt:X} (KeyBinding 0x{keyBindingMt:X}, Realm 0x{realmMt:X}, champs Realm {(layout.RealmBindingKeyCombination is null ? "absents" : "présents")})"); // pas de classification
            }
            if (action is null || combo is null) return Why($"liaison {i} : action {action?.ToString() ?? "?"}, touches {(combo is null ? "?" : string.Join('+', combo))}");
            if (combo.Length == 1 && action is LazerKeyLayout.LeftButton or LazerKeyLayout.RightButton)
                map.TryAdd(combo[0], action.Value);
        }
        bindings = new BindingCache(list, items, size, map);
        BindingDiagnostic = null;
        return map;

        IReadOnlyDictionary<int, int>? Why(string reason)
        {
            BindingDiagnostic = reason;
            return null;
        }
    }

    private static int[]? ReadIntArray(INativeLazerGameplayClockMemory memory, ulong? array)
    {
        if (!IsObject(memory, array)) return null;
        int? length = memory.ReadInt32(array!.Value + 8);
        if (length is null or < 0 or > 8) return null;
        var values = new int[length.Value];
        for (int i = 0; i < length; i++)
        {
            if (memory.ReadInt32(array.Value + 16 + (ulong)i * 4) is not { } v) return null;
            values[i] = v;
        }
        return values;
    }

    private static string? ReadString(INativeLazerGameplayClockMemory memory, ulong? address)
    {
        if (!IsObject(memory, address)) return null;
        int? length = memory.ReadInt32(address!.Value + 8);
        if (length is null or < 0 or > 64) return null;
        var chars = new StringBuilder(length.Value);
        for (int i = 0; i < length; i += 2)
        {
            if (memory.ReadInt32(address.Value + 12 + (ulong)i * 2) is not { } pair) return null;
            chars.Append((char)(pair & 0xFFFF));
            if (i + 1 < length) chars.Append((char)((pair >> 16) & 0xFFFF));
        }
        return chars.ToString();
    }

    /// <summary>Format de <c>KeyCombination.ToString()</c> : valeurs InputKey séparées par des virgules.</summary>
    internal static int[]? ParseCombination(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        string[] parts = text.Split(',');
        var values = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i])) return null;
        return values;
    }

    /// <summary>true/false selon la hiérarchie de MethodTables ; null si le MethodTable attendu n'est pas encore connu.</summary>
    private bool? Is(INativeLazerGameplayClockMemory memory, ulong obj, string typeName)
    {
        if (methodTables.Get(typeName) is not { } wanted) return null;
        if (obj < 0x10000 || (obj & 7) != 0) return false;
        ulong? mt = memory.ReadPointer(obj);
        for (int depth = 0; mt is >= 0x10000 && depth < 32; depth++)
        {
            if (mt == wanted) return true;
            mt = memory.ReadPointer(mt.Value + ParentOffset);
        }
        return false;
    }

    private static bool IsObject(INativeLazerGameplayClockMemory memory, ulong? address) =>
        address is >= 0x10000 && (address & 7) == 0 && memory.ReadPointer(address.Value) is >= 0x10000;

    private sealed record BindingCache(ulong? List, ulong? Items, int? Size, IReadOnlyDictionary<int, int> Map);
}
