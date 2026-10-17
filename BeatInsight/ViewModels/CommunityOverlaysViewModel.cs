using System.Collections.ObjectModel;
using System.Globalization;
using BeatInsight.Integration.CommunityOverlays.Managed;
using BeatInsight.Services.CommunityOverlays;

namespace BeatInsight.ViewModels;

/// <summary>FEAT-003 : carte Settings → Overlay → Community Overlays.</summary>
public sealed class CommunityOverlaysViewModel : ViewModelBase
{
    private CommunityOverlayManager? manager;
    private string statusMessage = "";
    private bool isStatusError;

    public ObservableCollection<CommunityOverlayItemViewModel> Overlays { get; } = [];

    public bool IsAttached => manager is not null;

    public bool IsCompanionAvailable => manager?.IsCompanionAvailable ?? false;

    public bool IsCompanionMissing => manager is not null && !manager.IsCompanionAvailable;

    public bool HasOverlays => Overlays.Count > 0;

    public bool HasNoOverlays => Overlays.Count == 0;

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (SetProperty(ref statusMessage, value))
                OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => StatusMessage.Length > 0;

    public bool IsStatusError
    {
        get => isStatusError;
        private set => SetProperty(ref isStatusError, value);
    }

    public void Attach(CommunityOverlayManager overlayManager)
    {
        ArgumentNullException.ThrowIfNull(overlayManager);
        Detach();
        manager = overlayManager;
        manager.Changed += Refresh;
        Refresh();
    }

    public void Detach()
    {
        if (manager is not null)
            manager.Changed -= Refresh;
        manager = null;
        Refresh();
    }

    public void Import(string? folderPath)
    {
        if (manager is null)
            return;
        CommunityOverlayImportResult result = manager.Import(folderPath);
        IsStatusError = !result.Success;
        StatusMessage = result.Message;
    }

    public void Start(CommunityOverlayItemViewModel item) => manager?.Start(item.Id);

    public void Stop(CommunityOverlayItemViewModel item) => manager?.Stop(item.Id);

    public void Preview(CommunityOverlayItemViewModel item) => manager?.Preview(item.Id);

    public void ToggleEdit(CommunityOverlayItemViewModel item) => manager?.SetEditMode(item.Id, !item.IsEditing);

    /// <summary>Retire l'overlay de la configuration ; son dossier n'est jamais supprimé.</summary>
    public void Remove(CommunityOverlayItemViewModel item)
    {
        if (manager?.Remove(item.Id) != true)
            return;
        IsStatusError = false;
        StatusMessage = $"Removed {item.Name} from BeatInsight. Its folder was not deleted.";
    }

    public void ApplyBounds(CommunityOverlayItemViewModel item)
    {
        if (manager is null)
            return;
        if (item.TryReadBounds(out CompanionBounds bounds) && manager.SetBounds(item.Id, bounds))
        {
            IsStatusError = false;
            StatusMessage = $"Position and size saved for {item.Name}.";
            return;
        }
        IsStatusError = true;
        StatusMessage = $"Invalid position or size. Width and height must be between " +
            $"{CommunityOverlayCompanionProtocol.MinimumSize:0} and {CommunityOverlayCompanionProtocol.MaximumSize:0}.";
    }

    internal void SetEnabled(CommunityOverlayItemViewModel item, bool enabled) => manager?.SetEnabled(item.Id, enabled);

    internal void SetScale(CommunityOverlayItemViewModel item, int percent) => manager?.SetScale(item.Id, percent);

    public void ResetScale(CommunityOverlayItemViewModel item) => item.ScalePercent = CommunityOverlayCompanionProtocol.DefaultScalePercent;

    private void Refresh()
    {
        IReadOnlyList<CommunityOverlayStatus> statuses = manager?.Statuses ?? [];
        var ids = statuses.Select(s => s.Entry.Id).ToHashSet(StringComparer.Ordinal);
        for (int i = Overlays.Count - 1; i >= 0; i--)
        {
            if (!ids.Contains(Overlays[i].Id))
                Overlays.RemoveAt(i);
        }

        foreach (CommunityOverlayStatus status in statuses)
        {
            CommunityOverlayItemViewModel? item = Overlays.FirstOrDefault(o => o.Id == status.Entry.Id);
            if (item is null)
            {
                item = new CommunityOverlayItemViewModel(this, status.Entry.Id);
                Overlays.Add(item);
            }
            item.Update(status);
        }

        OnPropertyChanged(nameof(IsAttached));
        OnPropertyChanged(nameof(IsCompanionAvailable));
        OnPropertyChanged(nameof(IsCompanionMissing));
        OnPropertyChanged(nameof(HasOverlays));
        OnPropertyChanged(nameof(HasNoOverlays));
    }
}

public sealed class CommunityOverlayItemViewModel : ViewModelBase
{
    private readonly CommunityOverlaysViewModel owner;
    private string name = "";
    private string folderPath = "";
    private bool isEnabled;
    private CommunityOverlayRuntimeState state;
    private CommunityOverlayLaunchMode? mode;
    private string? message;
    private bool isEditing;
    private double scalePercent = CommunityOverlayCompanionProtocol.DefaultScalePercent;
    private CompanionBounds? appliedBounds;
    private string xText = "";
    private string yText = "";
    private string widthText = "";
    private string heightText = "";

    internal CommunityOverlayItemViewModel(CommunityOverlaysViewModel owner, string id)
    {
        this.owner = owner;
        Id = id;
    }

    public string Id { get; }

    public string Name
    {
        get => name;
        private set => SetProperty(ref name, value);
    }

    public string FolderPath
    {
        get => folderPath;
        private set => SetProperty(ref folderPath, value);
    }

    /// <summary>Activation persistée (relancé au démarrage de BeatInsight).</summary>
    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (isEnabled == value)
                return;
            isEnabled = value;
            OnPropertyChanged();
            owner.SetEnabled(this, value);
        }
    }

    public CommunityOverlayRuntimeState State => state;

    public string StateText => state switch
    {
        CommunityOverlayRuntimeState.Starting => "Starting…",
        CommunityOverlayRuntimeState.Running => mode == CommunityOverlayLaunchMode.Preview ? "Preview (sample data)" : "Running",
        CommunityOverlayRuntimeState.ClosedByUser => "Closed",
        CommunityOverlayRuntimeState.Failed => "Failed",
        CommunityOverlayRuntimeState.Duplicate => "Already open",
        CommunityOverlayRuntimeState.Missing => "Folder missing",
        CommunityOverlayRuntimeState.Unavailable => "Companion missing",
        CommunityOverlayRuntimeState.LimitReached => "Limit reached",
        _ => "Stopped",
    };

    public string MessageText => message ?? "";

    public bool HasMessage => !string.IsNullOrEmpty(message);

    public bool IsProblem => state is CommunityOverlayRuntimeState.Failed or CommunityOverlayRuntimeState.Duplicate
        or CommunityOverlayRuntimeState.Missing or CommunityOverlayRuntimeState.Unavailable or CommunityOverlayRuntimeState.LimitReached;

    public bool IsActive => state is CommunityOverlayRuntimeState.Starting or CommunityOverlayRuntimeState.Running;

    public bool CanStart => !IsActive || mode == CommunityOverlayLaunchMode.Preview;

    public bool CanPreview => !IsActive || mode == CommunityOverlayLaunchMode.Live;

    public bool IsEditing
    {
        get => isEditing;
        private set
        {
            if (SetProperty(ref isEditing, value))
                OnPropertyChanged(nameof(EditButtonText));
        }
    }

    public string EditButtonText => IsEditing ? "Done moving" : "Move / resize";

    public int MinimumScale => CommunityOverlayCompanionProtocol.MinimumScalePercent;
    public int MaximumScale => CommunityOverlayCompanionProtocol.MaximumScalePercent;
    public int ScaleStep => CommunityOverlayCompanionProtocol.ScaleStepPercent;

    /// <summary>FEAT-003.1 : échelle du contenu en pourcentage (Slider), appliquée et persistée à chaque cran de 5 %.</summary>
    public double ScalePercent
    {
        get => scalePercent;
        set
        {
            int normalized = CommunityOverlayCompanionProtocol.NormalizeScale(value);
            if (scalePercent == normalized)
                return;
            scalePercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScaleText));
            OnPropertyChanged(nameof(IsDefaultScale));
            OnPropertyChanged(nameof(CanResetScale));
            owner.SetScale(this, normalized);
        }
    }

    public string ScaleText => $"{scalePercent:0} %";

    public bool IsDefaultScale => (int)scalePercent == CommunityOverlayCompanionProtocol.DefaultScalePercent;

    public bool CanResetScale => !IsDefaultScale;

    public string XText { get => xText; set => SetProperty(ref xText, value); }
    public string YText { get => yText; set => SetProperty(ref yText, value); }
    public string WidthText { get => widthText; set => SetProperty(ref widthText, value); }
    public string HeightText { get => heightText; set => SetProperty(ref heightText, value); }

    internal bool TryReadBounds(out CompanionBounds bounds)
    {
        bounds = default;
        if (!TryParse(XText, out double x) || !TryParse(YText, out double y) ||
            !TryParse(WidthText, out double width) || !TryParse(HeightText, out double height))
            return false;
        bounds = new CompanionBounds(x, y, width, height);
        return CommunityOverlayCompanionProtocol.IsValid(bounds);
    }

    internal void Update(CommunityOverlayStatus status)
    {
        Name = status.Entry.Name;
        FolderPath = status.Entry.FolderPath;
        if (isEnabled != status.Entry.Enabled)
        {
            isEnabled = status.Entry.Enabled;
            OnPropertyChanged(nameof(IsEnabled));
        }

        if ((int)scalePercent != status.Entry.Scale)
        {
            scalePercent = status.Entry.Scale;
            OnPropertyChanged(nameof(ScalePercent));
            OnPropertyChanged(nameof(ScaleText));
            OnPropertyChanged(nameof(IsDefaultScale));
            OnPropertyChanged(nameof(CanResetScale));
        }

        state = status.State;
        mode = status.Mode;
        message = status.Message;
        IsEditing = status.IsEditing;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(MessageText));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(IsProblem));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanPreview));

        // Les champs suivent la position réelle, sans écraser une saisie en cours tant que rien n'a bougé.
        if (status.Entry.Bounds != appliedBounds)
        {
            appliedBounds = status.Entry.Bounds;
            if (appliedBounds is { } b)
            {
                XText = Format(b.X);
                YText = Format(b.Y);
                WidthText = Format(b.Width);
                HeightText = Format(b.Height);
            }
        }
    }

    private static string Format(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static bool TryParse(string? text, out double value) =>
        double.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
