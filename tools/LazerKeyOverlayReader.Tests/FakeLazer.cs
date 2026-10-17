using System.Text;
using BeatInsight.Diagnostics.NativeLazer;

namespace LazerKeyOverlayReader.Tests;

/// <summary>Mémoire simulée octet par octet ; une adresse jamais écrite est illisible.</summary>
internal sealed class FakeMemory : INativeLazerGameplayClockMemory
{
    private readonly Dictionary<ulong, byte> bytes = [];

    public long BytesRead { get; private set; }
    public int ReadOperationCount { get; private set; }

    /// <summary>Action exécutée une fois, juste avant la N-ième lecture (simule une mutation pendant la lecture).</summary>
    public (int AtRead, Action Mutate)? Hook { get; set; }

    public void Write(ulong address, ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++) bytes[address + (ulong)i] = data[i];
    }

    public void WritePtr(ulong address, ulong value) => Write(address, BitConverter.GetBytes(value));
    public void WriteInt(ulong address, int value) => Write(address, BitConverter.GetBytes(value));
    public void WriteByte(ulong address, byte value) => bytes[address] = value;

    private byte[]? Read(ulong address, int count)
    {
        ReadOperationCount++;
        if (Hook is { } hook && ReadOperationCount == hook.AtRead)
        {
            Hook = null;
            hook.Mutate();
        }
        var data = new byte[count];
        for (int i = 0; i < count; i++)
            if (!bytes.TryGetValue(address + (ulong)i, out data[i])) return null;
        BytesRead += count;
        return data;
    }

    public byte? ReadByte(ulong address) => Read(address, 1) is { } d ? d[0] : null;
    public int? ReadInt32(ulong address) => Read(address, 4) is { } d ? BitConverter.ToInt32(d) : null;
    public double? ReadDouble(ulong address) => Read(address, 8) is { } d ? BitConverter.ToDouble(d) : null;
    public ulong? ReadPointer(ulong address) => Read(address, 8) is { } d ? BitConverter.ToUInt64(d) : null;
}

internal sealed class FakeTables : ILazerMethodTables
{
    public readonly Dictionary<string, ulong> Tables = [];
    public int RefreshRequests;
    public ulong? Get(string typeName) => Tables.TryGetValue(typeName, out ulong mt) ? mt : null;
    public void RequestRefresh() => RefreshRequests++;
}

/// <summary>
/// Graphe d'objets osu!lazer simulé : OsuGameDesktop → ScreenStack → SoloPlayer → HUD → triggers, et
/// DrawableOsuRuleset → OsuInputManager → OsuKeyBindingContainer → pressedInputKeys / KeyBindings.
/// Offsets arbitraires (le lecteur ne suppose rien d'autre que le layout fourni).
/// </summary>
internal sealed class FakeLazer
{
    public const int D = 100, F = 101, Space = 102, MouseLeft = 200, MouseRight = 201;

    public readonly FakeMemory Memory = new();
    public readonly FakeTables Tables = new();
    public readonly LazerKeyLayout Layout;
    public readonly NativeLazerGameplayClockLayout StackLayout;
    public readonly NativeLazerGenerationIdentity Generation = new(4242, new DateTime(2026, 10, 16, 5, 0, 0, DateTimeKind.Utc));
    private ulong next = 0x1000_0000;
    private ulong nextMt = 0x7000_0000;

    public ulong Root, ScreenStack, Stack, StackArray, Player, Hud, Controller, TriggerList, TriggerItems, Ruleset, Manager, Container,
        PressedSet, PressedEntries, BindingList, BindingItems;
    public ulong GenericMt, TriggerMt, PlayerLoaderMt, ReplayPlayerMt, SongSelectMt, OtherRulesetMt;
    public readonly List<ulong> Triggers = [];

    public FakeLazer()
    {
        Layout = new LazerKeyLayout
        {
            PlayerHud = 0x40, PlayerRuleset = 0x48, PlayerRestartCount = 0x50,
            HudInputCountController = 0x20, ControllerTriggers = 0x18, BindableListCollection = 0x18,
            ListItems = 0x8, ListSize = 0x10,
            TriggerActivationCount = 0x30, TriggerIsCounting = 0x38, TriggerIsActive = 0x28, TriggerAction = 0x40,
            BindableIntValue = 0x40, BindableBoolValue = 0x40,
            RulesetInputManager = 0x60, InputManagerContainer = 0x70, ContainerPressedInputKeys = 0x80, ContainerKeyBindings = 0x88,
            HashSetEntries = 0x10, HashSetCount = 0x28, EntryStride = 12, EntryNext = 4, EntryValue = 8,
            KeyBindingKeysArray = 0x10, KeyBindingAction = 0x18,
            RealmBindingKeyCombination = 0x28, RealmBindingActionInt = 0x30,
            Types = new Dictionary<string, (string, int)>(),
            InputKeys = new Dictionary<int, string> { [D] = "D", [F] = "F", [Space] = "Space", [MouseLeft] = "MouseLeft", [MouseRight] = "MouseRight" },
            OsuActions = new Dictionary<int, string> { [0] = "LeftButton", [1] = "RightButton", [2] = "Smoke" },
            MouseKeys = new HashSet<int> { MouseLeft, MouseRight },
        };
        StackLayout = new NativeLazerGameplayClockLayout(
            OsuGameScreenStack: 0x30, ScreenStackStack: 0x38, StackArray: 0x8, StackSize: 0x10, ArrayLength: 0x8, ArrayData: 0x10,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        // Hiérarchies de MethodTables (parent à MT + 0x10).
        ulong osuGame = Mt(null, "osu.Game.OsuGame");
        ulong desktop = Mt(osuGame, LazerKeyLayout.OsuGameDesktop);
        ulong screen = Mt(null, null);
        ulong player = Mt(screen, LazerKeyLayout.Player);
        ulong solo = Mt(player, LazerKeyLayout.SoloPlayer);
        ReplayPlayerMt = Mt(player, null);
        PlayerLoaderMt = Mt(screen, LazerKeyLayout.PlayerLoader);
        SongSelectMt = Mt(screen, null);
        ulong inputTrigger = Mt(null, LazerKeyLayout.InputTrigger);
        TriggerMt = Mt(inputTrigger, null); // KeyCounterActionTrigger<OsuAction> : générique, hors table
        ulong drawableRuleset = Mt(null, null);
        Mt(drawableRuleset, LazerKeyLayout.DrawableOsuRuleset);
        OtherRulesetMt = Mt(drawableRuleset, null);
        GenericMt = Mt(null, null);
        Mt(null, LazerKeyLayout.OsuScreenStack);
        Mt(null, LazerKeyLayout.HudOverlay);
        Mt(null, LazerKeyLayout.InputCountController);
        Mt(null, LazerKeyLayout.OsuInputManager);
        Mt(null, LazerKeyLayout.OsuKeyBindingContainer);
        Mt(null, LazerKeyLayout.KeyBinding);
        Mt(null, LazerKeyLayout.RealmKeyBinding);

        Root = Obj(desktop, 0x100);
        ScreenStack = Obj(Tables.Tables[LazerKeyLayout.OsuScreenStack], 0x80);
        Memory.WritePtr(Root + StackLayout.OsuGameScreenStack, ScreenStack);
        Stack = Obj(GenericMt, 0x20);
        Memory.WritePtr(ScreenStack + StackLayout.ScreenStackStack, Stack);
        StackArray = Array(4);
        Memory.WritePtr(Stack + StackLayout.StackArray, StackArray);

        Player = Obj(solo, 0x100);
        Memory.WriteInt(Player + Layout.PlayerRestartCount, 0);
        SetTop(SongSelect(), Player);

        Hud = Obj(Tables.Tables[LazerKeyLayout.HudOverlay], 0x40);
        Memory.WritePtr(Player + Layout.PlayerHud, Hud);
        Controller = Obj(Tables.Tables[LazerKeyLayout.InputCountController], 0x40);
        Memory.WritePtr(Hud + Layout.HudInputCountController, Controller);
        ulong bindable = Obj(GenericMt, 0x40);
        Memory.WritePtr(Controller + Layout.ControllerTriggers, bindable);
        TriggerList = Obj(GenericMt, 0x20);
        Memory.WritePtr(bindable + Layout.BindableListCollection, TriggerList);
        TriggerItems = Array(4);
        Memory.WritePtr(TriggerList + Layout.ListItems, TriggerItems);
        SetTriggers((0, 3, false), (1, 5, false), (2, 0, false));

        Ruleset = Obj(Tables.Tables[LazerKeyLayout.DrawableOsuRuleset], 0x80);
        Memory.WritePtr(Player + Layout.PlayerRuleset, Ruleset);
        Manager = Obj(Tables.Tables[LazerKeyLayout.OsuInputManager], 0x80);
        Memory.WritePtr(Ruleset + Layout.RulesetInputManager, Manager);
        Container = Obj(Tables.Tables[LazerKeyLayout.OsuKeyBindingContainer], 0x100);
        Memory.WritePtr(Manager + Layout.InputManagerContainer, Container);
        PressedSet = Obj(GenericMt, 0x30);
        Memory.WritePtr(Container + Layout.ContainerPressedInputKeys, PressedSet);
        PressedEntries = Obj(GenericMt, 16 + 12 * 8);
        Memory.WriteInt(PressedEntries + 8, 8);
        Memory.WritePtr(PressedSet + Layout.HashSetEntries, PressedEntries);
        SetPressed();
        SetRealmBindings((D, 0), (F, 1), (MouseLeft, 0), (MouseRight, 1));
    }

    public ulong SongSelect() => Obj(SongSelectMt, 0x40);

    private ulong Mt(ulong? parent, string? name)
    {
        ulong mt = nextMt;
        nextMt += 0x100;
        Memory.WritePtr(mt, 0);
        Memory.WritePtr(mt + 0x10, parent ?? 0);
        if (name is not null) Tables.Tables[name] = mt;
        return mt;
    }

    /// <summary>Objet zéroïsé de la taille donnée, MethodTable en tête.</summary>
    public ulong Obj(ulong mt, int size)
    {
        ulong address = next;
        next += (ulong)((size + 15) & ~7) + 0x100;
        Memory.Write(address, new byte[size]);
        Memory.WritePtr(address, mt);
        return address;
    }

    public ulong Array(int length)
    {
        ulong a = Obj(GenericMt, 16 + 8 * Math.Max(length, 1));
        Memory.WriteInt(a + 8, length);
        return a;
    }

    public void SetTop(params ulong[] screens)
    {
        for (int i = 0; i < screens.Length; i++) Memory.WritePtr(StackArray + 16 + (ulong)i * 8, screens[i]);
        Memory.WriteInt(Stack + StackLayout.StackSize, screens.Length);
    }

    /// <summary>Triggers (action, compte, actif) dans l'ordre donné.</summary>
    public void SetTriggers(params (int Action, int Count, bool Active)[] triggers)
    {
        Triggers.Clear();
        for (int i = 0; i < triggers.Length; i++)
        {
            ulong t = Obj(TriggerMt, 0x48);
            ulong count = Obj(GenericMt, 0x48);
            ulong counting = Obj(GenericMt, 0x48);
            Memory.WriteInt(count + Layout.BindableIntValue, triggers[i].Count);
            Memory.WriteByte(counting + Layout.BindableBoolValue, 1);
            Memory.WritePtr(t + Layout.TriggerActivationCount, count);
            Memory.WritePtr(t + Layout.TriggerIsCounting, counting);
            Memory.WriteByte(t + Layout.TriggerIsActive, triggers[i].Active ? (byte)1 : (byte)0);
            Memory.WriteInt(t + Layout.TriggerAction, triggers[i].Action);
            Memory.WritePtr(TriggerItems + 16 + (ulong)i * 8, t);
            Triggers.Add(t);
        }
        Memory.WriteInt(TriggerList + Layout.ListSize, triggers.Length);
    }

    public void SetCount(int index, int count) =>
        Memory.WriteInt(Memory.ReadPointer(Triggers[index] + Layout.TriggerActivationCount)!.Value + Layout.BindableIntValue, count);

    public void SetPressed(params int[] keys)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            ulong e = PressedEntries + 16 + (ulong)(i * 12);
            Memory.WriteInt(e, keys[i]);
            Memory.WriteInt(e + 4, -1);
            Memory.WriteInt(e + 8, keys[i]);
        }
        Memory.WriteInt(PressedSet + Layout.HashSetCount, keys.Length);
    }

    /// <summary>Ajoute une entrée libérée (Next &lt; -1) qui ne doit jamais être lue comme pressée.</summary>
    public void AddFreedEntry(int key)
    {
        int used = Memory.ReadInt32(PressedSet + Layout.HashSetCount)!.Value;
        ulong e = PressedEntries + 16 + (ulong)(used * 12);
        Memory.WriteInt(e + 4, -3);
        Memory.WriteInt(e + 8, key);
        Memory.WriteInt(PressedSet + Layout.HashSetCount, used + 1);
    }

    public void SetRealmBindings(params (int Key, int Action)[] bindings)
    {
        BindingList = Obj(GenericMt, 0x20);
        BindingItems = Array(bindings.Length);
        Memory.WritePtr(BindingList + Layout.ListItems, BindingItems);
        Memory.WriteInt(BindingList + Layout.ListSize, bindings.Length);
        Memory.WritePtr(Container + Layout.ContainerKeyBindings, BindingList);
        for (int i = 0; i < bindings.Length; i++)
        {
            ulong b = Obj(Tables.Tables[LazerKeyLayout.RealmKeyBinding], 0x40);
            Memory.WritePtr(b + Layout.RealmBindingKeyCombination!.Value, String(bindings[i].Key.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Memory.WriteInt(b + Layout.RealmBindingActionInt!.Value, bindings[i].Action);
            Memory.WritePtr(BindingItems + 16 + (ulong)i * 8, b);
        }
    }

    public void SetDefaultBindings(params (int Key, int Action)[] bindings)
    {
        BindingList = Obj(GenericMt, 0x20);
        BindingItems = Array(bindings.Length);
        Memory.WritePtr(BindingList + Layout.ListItems, BindingItems);
        Memory.WriteInt(BindingList + Layout.ListSize, bindings.Length);
        Memory.WritePtr(Container + Layout.ContainerKeyBindings, BindingList);
        for (int i = 0; i < bindings.Length; i++)
        {
            ulong b = Obj(Tables.Tables[LazerKeyLayout.KeyBinding], 0x40);
            ulong keys = Obj(GenericMt, 24);
            Memory.WriteInt(keys + 8, 1);
            Memory.WriteInt(keys + 16, bindings[i].Key);
            ulong boxed = Obj(GenericMt, 16);
            Memory.WriteInt(boxed + 8, bindings[i].Action);
            Memory.WritePtr(b + Layout.KeyBindingKeysArray, keys);
            Memory.WritePtr(b + Layout.KeyBindingAction, boxed);
            Memory.WritePtr(BindingItems + 16 + (ulong)i * 8, b);
        }
    }

    private ulong String(string text)
    {
        ulong s = Obj(GenericMt, 16 + text.Length * 2 + 2);
        Memory.WriteInt(s + 8, text.Length);
        Memory.Write(s + 12, Encoding.Unicode.GetBytes(text));
        return s;
    }

    /// <summary>Déplace le Player (copie) comme le ferait un GC compactant, et met à jour la pile.</summary>
    public void MovePlayer()
    {
        ulong moved = Obj(Memory.ReadPointer(Player)!.Value, 0x100);
        foreach (ulong offset in new[] { Layout.PlayerHud, Layout.PlayerRuleset })
            Memory.WritePtr(moved + offset, Memory.ReadPointer(Player + offset)!.Value);
        Memory.WriteInt(moved + Layout.PlayerRestartCount, Memory.ReadInt32(Player + Layout.PlayerRestartCount)!.Value);
        Player = moved;
        SetTop(SongSelect(), Player);
    }

    /// <summary>Nouveau Player (retry, nouvelle map) avec nouveaux triggers.</summary>
    public void NewPlayer(int restartCount, params (int Action, int Count, bool Active)[] triggers)
    {
        Player = Obj(Tables.Tables[LazerKeyLayout.SoloPlayer], 0x100);
        Memory.WriteInt(Player + Layout.PlayerRestartCount, restartCount);
        Memory.WritePtr(Player + Layout.PlayerHud, Hud);
        Memory.WritePtr(Player + Layout.PlayerRuleset, Ruleset);
        SetTriggers(triggers);
        SetTop(SongSelect(), Player);
    }

    public LazerKeyChainReader Reader() => new(Layout, StackLayout, Tables);

    public LazerChainRead Read(LazerKeyChainReader? reader = null) =>
        (reader ?? Reader()).Read(Memory, Generation, Root, _ => true);
}
