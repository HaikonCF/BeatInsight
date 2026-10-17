using BeatInsight.Diagnostics.NativeLazer;
using Xunit;
using static LazerKeyOverlayReader.Tests.FakeLazer;

namespace LazerKeyOverlayReader.Tests;

/// <summary>V3.14-COM-LAZER-KEY-02 : lecture de la chaîne (mémoire simulée, déterministe).</summary>
public class LazerKeyChainReaderTests
{
    [Fact]
    public void Valid_chain_reads_native_left_and_right_counters()
    {
        var lazer = new FakeLazer();
        LazerChainRead r = lazer.Read();
        Assert.Equal(LazerKeyValidity.Valid, r.Validity);
        Assert.Equal(3, r.Left.Count);
        Assert.Equal(5, r.Right.Count);
        Assert.Equal(LazerCounterAvailability.Native, r.Left.Availability);
        Assert.Equal("LeftButton", r.Left.Action);
        Assert.Equal(0, r.RestartCount);
        Assert.Equal("SoloPlayer", r.PlayerType);
    }

    [Fact]
    public void Triggers_are_identified_by_action_not_by_position()
    {
        var lazer = new FakeLazer();
        lazer.SetTriggers((2, 9, false), (1, 7, true), (0, 4, false)); // Smoke, droite, gauche
        LazerChainRead r = lazer.Read();
        Assert.Equal(4, r.Left.Count);
        Assert.Equal(7, r.Right.Count);
        Assert.True(r.Right.IsActive);
        Assert.False(r.Left.IsActive);
    }

    [Theory]
    [InlineData(new[] { 0, 0, 1 }, LazerKeyFailure.TriggerActionDuplicated)]
    [InlineData(new[] { 0, 2 }, LazerKeyFailure.TriggerActionMissing)]
    [InlineData(new[] { 0, 7 }, LazerKeyFailure.TriggerActionInvalid)]
    public void Ambiguous_or_incomplete_actions_are_unknown(int[] actions, LazerKeyFailure expected)
    {
        var lazer = new FakeLazer();
        lazer.SetTriggers(actions.Select(a => (a, 1, false)).ToArray());
        LazerChainRead r = lazer.Read();
        Assert.Equal(LazerKeyValidity.Unknown, r.Validity);
        Assert.Equal(expected, r.Failure);
        Assert.Null(r.Left.Count);
    }

    [Fact]
    public void Trigger_of_another_type_or_mixed_types_is_unknown()
    {
        var lazer = new FakeLazer();
        lazer.Memory.WritePtr(lazer.Triggers[1], lazer.GenericMt); // parent ≠ InputTrigger
        Assert.Equal(LazerKeyFailure.TriggerTypeInvalid, lazer.Read().Failure);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void Counters_out_of_bounds_are_unknown(int count)
    {
        var lazer = new FakeLazer();
        lazer.SetCount(0, count);
        LazerChainRead r = lazer.Read();
        Assert.Equal(LazerKeyValidity.Unknown, r.Validity);
        Assert.Equal(LazerKeyFailure.CountOutOfRange, r.Failure);
    }

    [Fact]
    public void Collection_bounds_are_checked()
    {
        var lazer = new FakeLazer();
        lazer.Memory.WriteInt(lazer.TriggerList + lazer.Layout.ListSize, 0);
        Assert.Equal(LazerKeyFailure.TriggersNotReady, lazer.Read().Failure);
        lazer.Memory.WriteInt(lazer.TriggerList + lazer.Layout.ListSize, 9);
        Assert.Equal(LazerKeyFailure.TriggerCollectionInvalid, lazer.Read().Failure);
        lazer.Memory.WriteInt(lazer.TriggerList + lazer.Layout.ListSize, 3);
        lazer.Memory.WriteInt(lazer.TriggerItems + 8, 2); // capacité < taille
        Assert.Equal(LazerKeyFailure.TriggerCollectionInvalid, lazer.Read().Failure);
    }

    [Fact]
    public void Other_screens_are_not_gameplay()
    {
        var lazer = new FakeLazer();
        lazer.SetTop(lazer.SongSelect());
        LazerChainRead select = lazer.Read();
        Assert.Equal(LazerKeyValidity.NotGameplay, select.Validity);
        Assert.Equal(LazerKeyFailure.NotInGameplay, select.Failure);
        Assert.Null(select.Left.Count);

        lazer.SetTop(lazer.SongSelect(), lazer.Obj(lazer.PlayerLoaderMt, 0x40));
        Assert.Equal(LazerKeyFailure.PlayerLoading, lazer.Read().Failure);
    }

    [Fact]
    public void Replay_spectate_or_other_ruleset_is_unsupported()
    {
        var lazer = new FakeLazer();
        lazer.Memory.WritePtr(lazer.Player, lazer.ReplayPlayerMt);
        LazerChainRead replay = lazer.Read();
        Assert.Equal(LazerKeyValidity.Unsupported, replay.Validity);
        Assert.Equal(LazerKeyFailure.UnsupportedPlayer, replay.Failure);

        var taiko = new FakeLazer();
        taiko.Memory.WritePtr(taiko.Ruleset, taiko.OtherRulesetMt);
        Assert.Equal(LazerKeyFailure.UnsupportedRuleset, taiko.Read().Failure);
    }

    [Fact]
    public void Root_with_another_method_table_is_unknown()
    {
        var lazer = new FakeLazer();
        lazer.Memory.WritePtr(lazer.Root, lazer.GenericMt);
        Assert.Equal(LazerKeyFailure.RootMoved, lazer.Read().Failure);
    }

    [Fact]
    public void Missing_method_table_requests_a_refresh_and_is_unknown()
    {
        var lazer = new FakeLazer();
        lazer.Tables.Tables.Remove(LazerKeyLayout.SoloPlayer);
        LazerChainRead r = lazer.Read();
        Assert.Equal(LazerKeyFailure.MethodTablesUnavailable, r.Failure);
        Assert.Equal(1, lazer.Tables.RefreshRequests);
    }

    [Fact]
    public void Chain_change_during_the_read_is_unknown()
    {
        var lazer = new FakeLazer();
        LazerKeyChainReader reader = lazer.Reader();
        lazer.Read(reader); // liaisons mises en cache
        int reads = lazer.Memory.ReadOperationCount;
        lazer.Read(reader);
        int perRead = lazer.Memory.ReadOperationCount - reads;
        // Pendant la lecture des états pressés (avant la relecture finale), la pile change d'écran (retry instantané).
        lazer.Memory.Hook = (lazer.Memory.ReadOperationCount + perRead - 25, () => lazer.SetTop(lazer.SongSelect(), lazer.Obj(lazer.PlayerLoaderMt, 0x40)));
        LazerChainRead r = lazer.Read(reader);
        Assert.Equal(LazerKeyValidity.Unknown, r.Validity);
        Assert.Equal(LazerKeyFailure.ChainChangedDuringRead, r.Failure);
    }

    [Fact]
    public void Unreadable_pointer_is_unknown_never_zero()
    {
        var lazer = new FakeLazer();
        lazer.Memory.WritePtr(lazer.Triggers[0] + lazer.Layout.TriggerActivationCount, 0x5000_0000); // non lisible
        LazerChainRead r = lazer.Read();
        Assert.Equal(LazerKeyValidity.Unknown, r.Validity);
        Assert.Null(r.Left.Count);
    }

    [Fact]
    public void Pressed_states_are_classified_with_realm_bindings()
    {
        var lazer = new FakeLazer();
        lazer.SetPressed(D, MouseLeft);
        LazerPressedStates p = lazer.Read().Pressed!;
        Assert.True(p.K1);
        Assert.False(p.K2);
        Assert.True(p.M1);
        Assert.False(p.M2);
        Assert.Equal(new[] { "D", "MouseLeft" }, p.RawKeys);

        lazer.SetPressed(F, MouseRight);
        p = lazer.Read().Pressed!;
        Assert.Equal((false, true, false, true), (p.K1!.Value, p.K2!.Value, p.M1!.Value, p.M2!.Value));
    }

    [Fact]
    public void Pressed_states_work_with_default_bindings_and_ignore_freed_entries_and_unbound_keys()
    {
        var lazer = new FakeLazer();
        lazer.SetDefaultBindings((D, 0), (F, 1), (MouseLeft, 0), (MouseRight, 1));
        lazer.SetPressed(Space);
        lazer.AddFreedEntry(D);
        LazerPressedStates p = lazer.Read().Pressed!;
        Assert.Equal((false, false, false, false), (p.K1!.Value, p.K2!.Value, p.M1!.Value, p.M2!.Value));
        Assert.Equal(new[] { "Space" }, p.RawKeys);
    }

    [Fact]
    public void Unknown_binding_type_leaves_pressed_states_unknown_but_counters_valid()
    {
        var lazer = new FakeLazer();
        lazer.SetPressed(D);
        lazer.Memory.WritePtr(lazer.Memory.ReadPointer(lazer.BindingItems + 16)!.Value, lazer.GenericMt);
        LazerKeyChainReader reader = lazer.Reader();
        LazerChainRead r = lazer.Read(reader);
        Assert.Equal(LazerKeyValidity.Valid, r.Validity);
        Assert.Null(r.Pressed!.K1);
        Assert.Equal(new[] { "D" }, r.Pressed.RawKeys);
        Assert.NotNull(reader.BindingDiagnostic);
    }

    [Theory]
    [InlineData("88", new[] { 88 })]
    [InlineData("1,2", new[] { 1, 2 })]
    public void Combination_strings_are_parsed(string text, int[] expected) =>
        Assert.Equal(expected, LazerKeyChainReader.ParseCombination(text));

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData(null)]
    public void Invalid_combination_strings_are_rejected(string? text) => Assert.Null(LazerKeyChainReader.ParseCombination(text));
}

/// <summary>V3.14-COM-LAZER-KEY-02 : identité de partie et remises à zéro.</summary>
public class LazerPlayTrackerTests
{
    private const string A = "1@a", B = "2@b";

    private static LazerChainRead Read(int left, int right, int restart = 0, ulong player = 0x1000,
        LazerKeyValidity validity = LazerKeyValidity.Valid) =>
        new(validity, LazerKeyFailure.None, player, restart, "SoloPlayer",
            new("LeftButton", LazerCounterAvailability.Native, left, false, true),
            new("RightButton", LazerCounterAvailability.Native, right, false, true), null, 0, 0);

    private static readonly LazerChainRead SongSelect = new(LazerKeyValidity.NotGameplay, LazerKeyFailure.NotInGameplay, null, null, null,
        LazerKeyOverlaySample.UnknownCounter("LeftButton"), LazerKeyOverlaySample.UnknownCounter("RightButton"), null, 0, 0);

    private static readonly LazerChainRead Doubt = SongSelect with { Validity = LazerKeyValidity.Unknown, Failure = LazerKeyFailure.ChainChangedDuringRead };

    [Fact]
    public void First_play_then_same_play_while_counting()
    {
        var t = new LazerPlayTracker();
        (string? id, LazerPlayTransition tr) = t.Observe(A, Read(0, 0));
        Assert.Equal(LazerPlayTransition.FirstPlay, tr);
        Assert.Equal(id, t.Observe(A, Read(3, 1)).PlayIdentity);
        Assert.Equal(LazerPlayTransition.FirstPlay, t.Observe(A, Read(4, 1)).Transition); // répétée sur toute la partie
    }

    [Fact]
    public void Player_address_change_alone_is_a_gc_move_not_a_new_play()
    {
        var t = new LazerPlayTracker();
        string? id = t.Observe(A, Read(5, 5, player: 0x1000)).PlayIdentity;
        Assert.Equal(id, t.Observe(A, Read(6, 5, player: 0x9000)).PlayIdentity);
        Assert.Equal(1, t.PlayerMoves);
    }

    [Fact]
    public void Retry_is_detected_by_restart_count_even_without_leaving_gameplay()
    {
        var t = new LazerPlayTracker();
        string? first = t.Observe(A, Read(20, 30, restart: 0)).PlayIdentity;
        (string? id, LazerPlayTransition tr) = t.Observe(A, Read(0, 0, restart: 1, player: 0x2000));
        Assert.NotEqual(first, id);
        Assert.Equal(LazerPlayTransition.Retry, tr);
    }

    [Fact]
    public void Retry_through_the_loader_is_still_a_retry()
    {
        var t = new LazerPlayTracker();
        t.Observe(A, Read(20, 30, restart: 0));
        t.Observe(A, SongSelect with { Failure = LazerKeyFailure.PlayerLoading });
        Assert.Equal(LazerPlayTransition.Retry, t.Observe(A, Read(0, 0, restart: 1)).Transition);
    }

    [Fact]
    public void Returning_to_gameplay_after_song_select_is_a_new_play()
    {
        var t = new LazerPlayTracker();
        string? first = t.Observe(A, Read(10, 10)).PlayIdentity;
        Assert.Null(t.Observe(A, SongSelect).PlayIdentity);
        (string? id, LazerPlayTransition tr) = t.Observe(A, Read(0, 0));
        Assert.NotEqual(first, id);
        Assert.Equal(LazerPlayTransition.NewGameplay, tr);
    }

    [Fact]
    public void Counter_going_back_is_a_new_play()
    {
        var t = new LazerPlayTracker();
        string? first = t.Observe(A, Read(10, 10)).PlayIdentity;
        Assert.NotEqual(first, t.Observe(A, Read(2, 10)).PlayIdentity);
    }

    [Fact]
    public void Doubtful_reads_decide_nothing_and_are_never_attached_to_a_play()
    {
        var t = new LazerPlayTracker();
        string? id = t.Observe(A, Read(10, 10)).PlayIdentity;
        Assert.Null(t.Observe(A, Doubt).PlayIdentity);
        Assert.Equal(id, t.Observe(A, Read(11, 10)).PlayIdentity); // même partie après un doute
    }

    [Fact]
    public void Process_change_starts_over_and_is_reported()
    {
        var t = new LazerPlayTracker();
        string? first = t.Observe(A, Read(10, 10)).PlayIdentity;
        (string? id, LazerPlayTransition tr) = t.Observe(B, Read(10, 10));
        Assert.NotEqual(first, id);
        Assert.StartsWith(B, id);
        Assert.Equal(LazerPlayTransition.ProcessChanged, tr);
    }

    [Fact]
    public void Unsupported_player_closes_the_play()
    {
        var t = new LazerPlayTracker();
        string? first = t.Observe(A, Read(10, 10)).PlayIdentity;
        t.Observe(A, SongSelect with { Validity = LazerKeyValidity.Unsupported, Failure = LazerKeyFailure.UnsupportedPlayer });
        Assert.NotEqual(first, t.Observe(A, Read(10, 10)).PlayIdentity);
    }
}
