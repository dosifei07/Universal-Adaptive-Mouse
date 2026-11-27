using System.Collections.ObjectModel;
using System.Diagnostics;
using UAM.Application;
using UAM.Core.Models;
using UAM.Desktop.Shared.Formatting;
using UAM.Desktop.Shared.Mvvm;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// First-party 2D pointer task. The view model owns trial bookkeeping; the
/// Avalonia canvas only forwards pointer coordinates. Completing a session
/// sends a canonical document through the normal import path, so lab results
/// appear on every page without manual JSON handling.
/// </summary>
public sealed class LabViewModel : ObservableObject
{
    public const double CanvasWidth = 520;
    public const double CanvasHeight = 320;
    public const double TargetRadius = 26;
    public const int DefaultTargetCount = 12;

    private readonly ILabSessionService? _service;
    private readonly Stopwatch _clock = new();
    private readonly List<LabTarget> _targets = [];
    private readonly List<LabTrial> _trials = [];

    private int _currentIndex;
    private LabPoint _previousPoint = new(CanvasWidth / 2, CanvasHeight / 2);
    private bool _isRunning;
    private bool _isSaving;
    private string _sessionId = string.Empty;
    private DateTime _startedUtc;
    private string? _statusMessage;
    private string? _errorMessage;
    private LabSessionResult? _lastResult;

    public LabViewModel(ILabSessionService? service)
    {
        _service = service;

        StartCommand = new RelayCommand(
            () => StartSession(),
            () => CanStart);
        CompleteCommand = new AsyncRelayCommand(
            () => CompleteSessionAsync(),
            () => CanComplete);
        ResetCommand = new RelayCommand(Reset, () => !_isSaving);

        Reset();
    }

    public RelayCommand StartCommand { get; }

    public AsyncRelayCommand CompleteCommand { get; }

    public RelayCommand ResetCommand { get; }

    public ObservableCollection<LabTargetMarkerViewModel> Targets { get; } = [];

    public string Title => "UAM Lab · 2D flick grid";

    public string Description =>
        "Click the highlighted target as quickly and accurately as you can. " +
        "Your pointer input is captured locally and imported automatically.";

    public string Limitation => LabSessionEngine.Limitation;

    public double SurfaceWidth => CanvasWidth;

    public double SurfaceHeight => CanvasHeight;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(HasCurrentTarget));
                OnPropertyChanged(nameof(IsIdle));
                StartCommand.RaiseCanExecuteChanged();
                CompleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => !_isRunning && !IsComplete;

    public bool IsComplete => _trials.Count > 0 && _currentIndex >= _targets.Count;

    public bool CanStart => !_isRunning && !_isSaving && _service is not null;

    public bool CanComplete =>
        !_isSaving && _service is not null && _trials.Count > 0;

    public bool HasCurrentTarget =>
        _isRunning && _currentIndex >= 0 && _currentIndex < _targets.Count;

    public LabTarget? CurrentTarget =>
        HasCurrentTarget ? _targets[_currentIndex] : null;

    public double CurrentTargetX => CurrentTarget?.X ?? 0;

    public double CurrentTargetY => CurrentTarget?.Y ?? 0;

    public double CurrentTargetDiameter => (CurrentTarget?.Radius ?? TargetRadius) * 2;

    public double CurrentTargetLeft => CurrentTarget is null
        ? 0
        : CurrentTarget.X - CurrentTarget.Radius;

    public double CurrentTargetTop => CurrentTarget is null
        ? 0
        : CurrentTarget.Y - CurrentTarget.Radius;

    public string SessionId => _sessionId;

    public int TrialCount => _trials.Count;

    public int HitCount => _trials.Count(x => x.Hit);

    public string HitRateText => _trials.Count == 0
        ? "—"
        : UamFormat.Percent((double)HitCount / _trials.Count);

    public string ProgressText => _targets.Count == 0
        ? "Not started"
        : $"{Math.Min(_currentIndex, _targets.Count)} / {_targets.Count} targets";

    public string StatusMessage
    {
        get => _statusMessage ?? string.Empty;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(_statusMessage);

    public string ErrorMessage
    {
        get => _errorMessage ?? string.Empty;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    public LabSessionResult? LastResult => _lastResult;

    public void StartSession(int targetCount = DefaultTargetCount)
    {
        if (!CanStart)
        {
            return;
        }

        if (targetCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetCount),
                "A lab session needs at least one target.");
        }

        _targets.Clear();
        _trials.Clear();
        Targets.Clear();
        _lastResult = null;
        _currentIndex = 0;
        _previousEndMs = 0;
        _previousPoint = new LabPoint(CanvasWidth / 2, CanvasHeight / 2);
        _sessionId = $"lab-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..28];
        _startedUtc = DateTime.UtcNow;
        _clock.Restart();

        var centerX = CanvasWidth / 2;
        var centerY = CanvasHeight / 2;

        for (var index = 0; index < targetCount; index++)
        {
            // Alternate near and far targets around the field to exercise
            // different movement distances without pretending to model 3D aim.
            var angle = (index * 137.5) * Math.PI / 180.0;
            var radius = index % 3 == 0 ? 70 : 150;
            var target = new LabTarget
            {
                Index = index,
                X = centerX + Math.Cos(angle) * radius,
                Y = centerY + Math.Sin(angle) * radius,
                Radius = TargetRadius
            };

            _targets.Add(target);
            Targets.Add(new LabTargetMarkerViewModel(target));
        }

        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        IsRunning = true;
        OnPropertyChanged(nameof(CurrentTarget));
        RaiseTargetChanged();
        RaiseProgressChanged();
    }

    /// <summary>
    /// Records one pointer click in canvas coordinates. The previous click is
    /// the start point for the next movement, which mirrors continuous aim.
    /// </summary>
    public void RecordClick(double x, double y)
    {
        if (!HasCurrentTarget || _service is null)
        {
            return;
        }

        var target = _targets[_currentIndex];
        var dx = x - target.X;
        var dy = y - target.Y;
        var hit = Math.Sqrt(dx * dx + dy * dy) <= target.Radius;

        var endMs = Math.Max(_clock.Elapsed.TotalMilliseconds, 1);

        _trials.Add(new LabTrial
        {
            Target = target,
            Start = _previousPoint,
            End = new LabPoint(x, y),
            StartMilliseconds = _previousEndMs,
            EndMilliseconds = endMs,
            Hit = hit
        });

        _previousEndMs = endMs;
        _previousPoint = new LabPoint(x, y);
        Targets[_currentIndex].IsHit = hit;
        Targets[_currentIndex].IsReached = true;
        _currentIndex++;

        OnPropertyChanged(nameof(TrialCount));
        OnPropertyChanged(nameof(HitCount));
        OnPropertyChanged(nameof(HitRateText));

        if (_currentIndex >= _targets.Count)
        {
            IsRunning = false;
            StatusMessage = "All targets completed. Save the session to import it.";
        }
        else
        {
            OnPropertyChanged(nameof(CurrentTarget));
            RaiseTargetChanged();
        }

        RaiseProgressChanged();
    }

    public async Task CompleteSessionAsync(
        CancellationToken cancellationToken = default)
    {
        if (_service is null)
        {
            ErrorMessage = "The UAM Lab service is not configured in this build.";
            return;
        }

        if (_trials.Count == 0)
        {
            ErrorMessage = "Complete at least one target before saving.";
            return;
        }

        if (_isSaving)
        {
            return;
        }

        _isSaving = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        RaiseCommandState();

        try
        {
            var capture = new LabSessionCapture
            {
                SessionId = _sessionId,
                GameId = "uam-lab",
                ScenarioId = "2d-flick-grid",
                StartedUtc = _startedUtc,
                Trials = _trials.ToList()
            };

            _lastResult = await _service.CompleteAsync(capture, cancellationToken);

            StatusMessage = _lastResult.Import.Outcome == ImportOutcome.Duplicate
                ? "This lab session was already imported; nothing was duplicated."
                : $"Saved {_lastResult.TrialCount} trials " +
                  $"({HitRateText} hits) and imported them into UAM.";

            _clock.Stop();
            IsRunning = false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            _isSaving = false;
            RaiseCommandState();
        }
    }

    public void Reset()
    {
        _clock.Reset();
        _targets.Clear();
        _trials.Clear();
        Targets.Clear();
        _currentIndex = 0;
        _previousEndMs = 0;
        _previousPoint = new LabPoint(CanvasWidth / 2, CanvasHeight / 2);
        _sessionId = string.Empty;
        _lastResult = null;
        IsRunning = false;
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;

        OnPropertyChanged(nameof(CurrentTarget));
        RaiseTargetChanged();
        RaiseProgressChanged();
        OnPropertyChanged(nameof(TrialCount));
        OnPropertyChanged(nameof(HitCount));
        OnPropertyChanged(nameof(HitRateText));
    }

    private double _previousEndMs;

    private void RaiseTargetChanged()
    {
        OnPropertyChanged(nameof(CurrentTargetX));
        OnPropertyChanged(nameof(CurrentTargetY));
        OnPropertyChanged(nameof(CurrentTargetDiameter));
        OnPropertyChanged(nameof(CurrentTargetLeft));
        OnPropertyChanged(nameof(CurrentTargetTop));
        OnPropertyChanged(nameof(HasCurrentTarget));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsIdle));
        RaiseCommandState();
    }

    private void RaiseProgressChanged()
    {
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsIdle));
        RaiseCommandState();
    }

    private void RaiseCommandState()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanComplete));
        StartCommand.RaiseCanExecuteChanged();
        CompleteCommand.RaiseCanExecuteChanged();
        ResetCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>One canvas target and whether it has been reached.</summary>
public sealed class LabTargetMarkerViewModel : ObservableObject
{
    private bool _isHit;
    private bool _isReached;

    public LabTargetMarkerViewModel(LabTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        Index = target.Index;
        X = target.X;
        Y = target.Y;
        Radius = target.Radius;
    }

    public int Index { get; }

    public double X { get; }

    public double Y { get; }

    public double Radius { get; }

    public bool IsHit
    {
        get => _isHit;
        set => SetProperty(ref _isHit, value);
    }

    public bool IsReached
    {
        get => _isReached;
        set => SetProperty(ref _isReached, value);
    }
}
