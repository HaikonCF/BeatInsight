using BeatInsight.Models.Persistence;
using BeatInsight.Services;
using System.Windows.Input;

namespace BeatInsight.Tests.Services;

/// <summary>
/// Vérifie le mapping pur clavier -> label humain utilisé par le mode
/// Fast Labeling, indépendamment de MainWindow et de tout état WPF.
/// </summary>
public sealed class HumanLabelHotkeysTests
{
    [Theory]
    [InlineData(Key.D1, "Stream")]
    [InlineData(Key.D2, "Jump")]
    [InlineData(Key.D3, "Tech")]
    [InlineData(Key.D4, "ClassicMixed")]
    [InlineData(Key.NumPad1, "Stream")]
    [InlineData(Key.NumPad4, "ClassicMixed")]
    public void TryMapPrimaryKey_RecognizedKeys_ReturnExpectedLabel(
        Key key,
        string expectedName)
    {
        Assert.True(HumanLabelHotkeys.TryMapPrimaryKey(key, out MlHumanLabel label));
        Assert.Equal(Enum.Parse<MlHumanLabel>(expectedName), label);
    }

    [Theory]
    [InlineData(Key.D0)]
    [InlineData(Key.D5)]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    [InlineData(Key.Back)]
    public void TryMapPrimaryKey_UnrecognizedKeys_ReturnFalse(Key key)
    {
        Assert.False(HumanLabelHotkeys.TryMapPrimaryKey(key, out _));
    }

    [Theory]
    [InlineData(Key.D1, false, "Stream")]
    [InlineData(Key.D2, false, "Jump")]
    [InlineData(Key.D3, false, "Tech")]
    [InlineData(Key.D4, false, "ClassicMixed")]
    [InlineData(Key.D0, true, null)]
    [InlineData(Key.NumPad0, true, null)]
    public void TryMapSecondaryKey_RecognizedKeys_ReturnExpected(
        Key key,
        bool expectedIsNone,
        string? expectedName)
    {
        Assert.True(HumanLabelHotkeys.TryMapSecondaryKey(
            key,
            out bool isNone,
            out MlHumanLabel label));

        Assert.Equal(expectedIsNone, isNone);

        if (!expectedIsNone)
        {
            Assert.Equal(Enum.Parse<MlHumanLabel>(expectedName!), label);
        }
    }

    [Theory]
    [InlineData(Key.D5)]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void TryMapSecondaryKey_UnrecognizedKeys_ReturnFalse(Key key)
    {
        Assert.False(HumanLabelHotkeys.TryMapSecondaryKey(
            key,
            out _,
            out _));
    }

    [Fact]
    public void SecondaryEqualToPrimary_MustBeRejectedByCaller()
    {
        // HumanLabelHotkeys ne connaît pas la sélection primaire en
        // cours : c'est à l'appelant (MainWindow) de refuser la
        // combinaison. Ce test documente ce contrat en simulant la
        // règle telle qu'appliquée par SetSelectedSecondaryHumanLabel.
        Assert.True(HumanLabelHotkeys.TryMapPrimaryKey(
            Key.D2,
            out MlHumanLabel primary));
        Assert.True(HumanLabelHotkeys.TryMapSecondaryKey(
            Key.D2,
            out bool isNone,
            out MlHumanLabel secondary));

        Assert.False(isNone);
        Assert.Equal(primary, secondary);

        bool wouldBeRejected = secondary == primary;
        Assert.True(wouldBeRejected);
    }
}
