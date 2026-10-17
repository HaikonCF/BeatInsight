using System.Windows;
using System.IO.Packaging;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BeatInsight.ViewModels;

public enum BiaVisualState
{
    Idle,
    Analyzing,
    Error,
    GoodRun,
    Failed,
    FullCombo,
}

internal static class BiaVisualAssetMap
{
    internal const string IdleAsset = "Assets/BiaChan/BiaChanIdle.png";

    private static readonly IReadOnlyDictionary<BiaVisualState, string> Assets =
        new Dictionary<BiaVisualState, string>
        {
            [BiaVisualState.Idle] = IdleAsset,
            [BiaVisualState.Analyzing] = IdleAsset,
            [BiaVisualState.Error] = "Assets/BiaChan/BiaChanError.png",
            [BiaVisualState.GoodRun] = "Assets/BiaChan/BiaChanGoodrun.png",
            [BiaVisualState.Failed] = "Assets/BiaChan/BiaChanFailed.png",
            [BiaVisualState.FullCombo] = "Assets/BiaChan/BiaChanFC.png",
        };

    internal static string GetAssetPath(BiaVisualState state) =>
        Assets.TryGetValue(state, out string? assetPath)
            ? assetPath
            : IdleAsset;

    internal static Uri GetPackUri(string assetPath) => PackUriHelper.Create(
        new Uri("application:///", UriKind.Absolute),
        new Uri($"/BeatInsight;component/{assetPath}", UriKind.Relative));

    internal static ImageSource GetImage(BiaVisualState state)
        => GetImageFromAssetPath(GetAssetPath(state));

    internal static ImageSource GetImageFromAssetPath(string assetPath)
    {
        try
        {
            return Load(assetPath);
        }
        catch
        {
            if (!string.Equals(
                    assetPath,
                    IdleAsset,
                    StringComparison.Ordinal))
            {
                try
                {
                    return Load(IdleAsset);
                }
                catch
                {
                }
            }

            return new DrawingImage();
        }
    }

    private static BitmapImage Load(string assetPath)
    {
        Uri uri = GetPackUri(assetPath);
        using var resource = Application.GetResourceStream(uri)?.Stream;
        if (resource is null)
            throw new System.IO.FileNotFoundException(assetPath);

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = uri;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

public sealed class BiaAssistantViewModel : ViewModelBase
{
    private readonly Func<BiaVisualState, ImageSource> imageLoader;
    private string name = "BeatInsight";
    private string title = "Your Analysis Assistant";
    private string message = "Select or play a beatmap to see my detailed analytical breakdown!";
    private string statusBadge = "Ready";
    private BiaVisualState state = BiaVisualState.Idle;
    private ImageSource imageSource = new DrawingImage();
    private bool visualAssetsEnabled;

    public BiaAssistantViewModel()
        : this(BiaVisualAssetMap.GetImage)
    {
    }

    internal BiaAssistantViewModel(Func<BiaVisualState, ImageSource> imageLoader)
    {
        this.imageLoader = imageLoader ?? throw new ArgumentNullException(nameof(imageLoader));
    }

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public string Title
    {
        get => title;
        set => SetProperty(ref title, value);
    }

    public string Message
    {
        get => message;
        set => SetProperty(ref message, value);
    }

    public string StatusBadge
    {
        get => statusBadge;
        set => SetProperty(ref statusBadge, value);
    }

    public BiaVisualState State
    {
        get => state;
        set
        {
            if (SetProperty(ref state, value) && visualAssetsEnabled)
            {
                ImageSource = imageLoader(value);
            }
        }
    }

    public ImageSource ImageSource
    {
        get => imageSource;
        private set => SetProperty(ref imageSource, value);
    }

    internal bool VisualAssetsEnabled => visualAssetsEnabled;

    internal void SetVisualAssetsEnabled(bool enabled)
    {
        if (visualAssetsEnabled != enabled)
        {
            visualAssetsEnabled = enabled;
            ImageSource = enabled
                ? imageLoader(state)
                : new DrawingImage();
        }

        Name = enabled ? "Bia-chan" : "BeatInsight";
    }

    public void SetState(
        BiaVisualState visualState,
        string status,
        string? secondaryText = null)
    {
        State = visualState;
        StatusBadge = status;

        if (secondaryText is not null)
        {
            Message = secondaryText;
        }
    }
}
