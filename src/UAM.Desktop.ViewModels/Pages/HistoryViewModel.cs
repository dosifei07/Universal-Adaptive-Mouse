using System.Collections.ObjectModel;
using UAM.Application;
using UAM.Core.Models;
using UAM.Desktop.Shared.Formatting;
using UAM.Desktop.Shared.Mvvm;
using UAM.Desktop.Shared.Navigation;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Stored training sessions with game and scenario filters. Read-only.
/// </summary>
public sealed class HistoryViewModel : PageViewModelBase
{
    private sealed record HistoryData(
        IReadOnlyList<string> Games,
        IReadOnlyList<string> Scenarios,
        IReadOnlyList<TrainingSessionSummary> Sessions);

    private readonly IHistoryService _service;

    private string? _selectedGame;
    private string? _selectedScenario;
    private bool _suppressSelection;

    public HistoryViewModel(IHistoryService service)
        : base(PageKey.History)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(), () => !IsBusy);
        ClearFiltersCommand = new AsyncRelayCommand(
            () => SetFiltersAsync(null, null),
            () => !IsBusy && (_selectedGame is not null || _selectedScenario is not null));
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ClearFiltersCommand { get; }

    public ObservableCollection<string> Games { get; } = [];

    public ObservableCollection<string> Scenarios { get; } = [];

    public ObservableCollection<SessionRowViewModel> Sessions { get; } = [];

    public string? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (_suppressSelection || _selectedGame == value)
            {
                return;
            }

            // A scenario belongs to a game, so changing the game resets it.
            _ = SetFiltersAsync(value, null);
        }
    }

    public string? SelectedScenario
    {
        get => _selectedScenario;
        set
        {
            if (_suppressSelection || _selectedScenario == value)
            {
                return;
            }

            _ = SetFiltersAsync(_selectedGame, value);
        }
    }

    public bool HasSessions => Sessions.Count > 0;

    public bool IsEmpty => HasLoaded && !HasSessions && !HasError;

    public bool IsFiltered => _selectedGame is not null || _selectedScenario is not null;

    /// <summary>True when data exists but the filters hide all of it.</summary>
    public bool IsFilteredEmpty => IsEmpty && IsFiltered;

    public string SessionCountText => UamFormat.Count(Sessions.Count);

    public string ObservationTotalText =>
        UamFormat.Count(Sessions.Sum(x => x.ObservationCount));

    public override Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsync(cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var game = _selectedGame;
        var scenario = _selectedScenario;

        return LoadLatestAsync(
            async token =>
            {
                var games = await _service.GetGamesAsync(token);

                // A filter that no longer exists (for example after the
                // database changed) is dropped instead of showing nothing.
                var effectiveGame = game is not null && games.Contains(game)
                    ? game
                    : null;

                var scenarios = await _service.GetScenariosAsync(effectiveGame, token);

                var effectiveScenario = scenario is not null && scenarios.Contains(scenario)
                    ? scenario
                    : null;

                var sessions = await _service.GetSessionsAsync(
                    effectiveGame,
                    effectiveScenario,
                    token);

                return (Data: new HistoryData(games, scenarios, sessions),
                        Game: effectiveGame,
                        Scenario: effectiveScenario);
            },
            loaded => Apply(loaded.Data, loaded.Game, loaded.Scenario),
            cancellationToken);
    }

    /// <summary>Sets both filters at once and waits for the reload.</summary>
    public Task SetFiltersAsync(
        string? gameId,
        string? scenarioId,
        CancellationToken cancellationToken = default)
    {
        _suppressSelection = true;
        try
        {
            _selectedGame = gameId;
            _selectedScenario = scenarioId;
        }
        finally
        {
            _suppressSelection = false;
        }

        OnPropertyChanged(nameof(SelectedGame));
        OnPropertyChanged(nameof(SelectedScenario));

        return RefreshAsync(cancellationToken);
    }

    protected override void OnBusyChanged()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        ClearFiltersCommand.RaiseCanExecuteChanged();
    }

    private void Apply(HistoryData data, string? game, string? scenario)
    {
        _suppressSelection = true;
        try
        {
            Replace(Games, data.Games);
            Replace(Scenarios, data.Scenarios);

            Sessions.Clear();
            foreach (var session in data.Sessions.OrderByDescending(x => x.LastObservationUtc))
            {
                Sessions.Add(new SessionRowViewModel(session));
            }

            _selectedGame = game;
            _selectedScenario = scenario;
        }
        finally
        {
            _suppressSelection = false;
        }

        OnPropertyChanged(string.Empty);
        ClearFiltersCommand.RaiseCanExecuteChanged();
    }

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
