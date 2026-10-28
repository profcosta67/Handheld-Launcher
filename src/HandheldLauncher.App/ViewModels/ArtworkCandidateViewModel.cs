using HandheldLauncher.Core.Services.Artwork;

namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// Wraps one ArtworkCandidate (Core model) for display in the cover/banner
/// picker grid — just adds IsSelected for the highlighted-cell border, same
/// pattern as ContextMenuItemViewModel.
/// </summary>
public sealed class ArtworkCandidateViewModel : ViewModelBase
{
    public ArtworkCandidateViewModel(ArtworkCandidate candidate)
    {
        Candidate = candidate;
    }

    public ArtworkCandidate Candidate { get; }

    public string LocalThumbnailPath => Candidate.LocalThumbnailPath;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}
