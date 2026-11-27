using System.Collections.ObjectModel;
using UAM.Application;
using UAM.Core.Models;
using UAM.Core.Validation;
using UAM.Desktop.Shared.Formatting;
using UAM.Desktop.Shared.Mvvm;
using UAM.Desktop.Shared.Navigation;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Directional and speed analysis for the whole data set and for one game,
/// with data-quality information (accepted and rejected observations).
/// </summary>
public sealed class AnalysisViewModel : PageViewModelBase
{
    private readonly IAnalysisService _service;

    private AnalysisResult? _result;
    private string? _selectedGame;
    private bool _suppressSelection;

    private IReadOnlyList<HeatmapCellViewModel> _globalHeatmap = HeatmapCellViewModel.BuildRing(null);
    private IReadOnlyList<HeatmapCellViewModel> _gameHeatmap = HeatmapCellViewModel.BuildRing(null);
    private IReadOnlyList<SpeedEstimateViewModel> _globalSpeeds = SpeedEstimateViewModel.BuildAll(null);
    private IReadOnlyList<SpeedEstimateViewModel> _gameSpeeds = SpeedEstimateViewModel.BuildAll(null);
    private IReadOnlyList<string> _rejectionSummaries = [];

    public AnalysisViewModel(IAnalysisService service)
        : base(PageKey.Analysis)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(), () => !IsBusy);
        GenerateRecommendationCommand = new RelayCommand(
            () => RequestNavigation(PageKey.Optimization),
            () => HasData);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Opens the Optimization page; recommendations are created there.</summary>
    public RelayCommand GenerateRecommendationCommand { get; }

    public ObservableCollection<string> Games { get; } = [];

    public string? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (_suppressSelection || !SetProperty(ref _selectedGame, value))
            {
                return;
            }

            // Errors are captured by RunSafeAsync, so the discarded task
            // cannot fault unobserved.
            _ = RefreshAsync();
        }
    }

    public bool HasData => _result?.HasData ?? false;

    public bool IsEmpty => HasLoaded && !HasData && !HasError;

    public bool HasGameData => _result?.GameModel.DirectionalEstimates.Count > 0;

    public IReadOnlyList<HeatmapCellViewModel> GlobalHeatmap => _globalHeatmap;

    public IReadOnlyList<HeatmapCellViewModel> GameHeatmap => _gameHeatmap;

    public IReadOnlyList<SpeedEstimateViewModel> GlobalSpeeds => _globalSpeeds;

    public IReadOnlyList<SpeedEstimateViewModel> GameSpeeds => _gameSpeeds;

    public string TotalObservationsText => _result is null
        ? "—"
        : UamFormat.Count(_result.TotalObservations);

    public string AcceptedText => _result is null
        ? "—"
        : UamFormat.Count(_result.AcceptedCount);

    public string RejectedText => _result is null
        ? "—"
        : UamFormat.Count(_result.RejectedCount);

    public bool HasRejections => (_result?.RejectedCount ?? 0) > 0;

    /// <summary>
    /// One line per validation code, with counts, so the player can see why
    /// observations were excluded.
    /// </summary>
    public IReadOnlyList<string> RejectionSummaries => _rejectionSummaries;

    public override Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsync(cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var requested = _selectedGame;

        return LoadLatestAsync(
            token => _service.AnalyzeAsync(requested, token),
            Apply,
            cancellationToken);
    }

    /// <summary>Selects a game and waits for the analysis to finish.</summary>
    public async Task SelectGameAsync(
        string? gameId,
        CancellationToken cancellationToken = default)
    {
        _suppressSelection = true;
        try
        {
            SetProperty(ref _selectedGame, gameId, nameof(SelectedGame));
        }
        finally
        {
            _suppressSelection = false;
        }

        await RefreshAsync(cancellationToken);
    }

    protected override void OnBusyChanged()
    {
        RefreshCommand.RaiseCanExecuteChanged();
    }

    private void Apply(AnalysisResult result)
    {
        _result = result;

        _globalHeatmap = HeatmapCellViewModel.BuildRing(result.GlobalModel.DirectionalEstimates);
        _gameHeatmap = HeatmapCellViewModel.BuildRing(result.GameModel.DirectionalEstimates);
        _globalSpeeds = SpeedEstimateViewModel.BuildAll(result.GlobalModel.SpeedEstimates);
        _gameSpeeds = SpeedEstimateViewModel.BuildAll(result.GameModel.SpeedEstimates);
        _rejectionSummaries = SummarizeRejections(result.Rejections);

        _suppressSelection = true;
        try
        {
            Games.Clear();
            foreach (var game in result.AvailableGames)
            {
                Games.Add(game);
            }

            SetProperty(ref _selectedGame, result.GameId, nameof(SelectedGame));
        }
        finally
        {
            _suppressSelection = false;
        }

        OnPropertyChanged(string.Empty);
        GenerateRecommendationCommand.RaiseCanExecuteChanged();
    }

    private static IReadOnlyList<string> SummarizeRejections(
        IReadOnlyList<RejectedObservation> rejections)
    {
        return rejections
            .SelectMany(x => x.Validation.Codes)
            .GroupBy(x => x)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key.ToString(), StringComparer.Ordinal)
            .Select(x => $"{x.Key}: {UamFormat.Count(x.Count())}")
            .ToList();
    }
}
