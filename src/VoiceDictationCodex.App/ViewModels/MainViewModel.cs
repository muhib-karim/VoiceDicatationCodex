using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using VoiceDictationCodex.Models;
using VoiceDictationCodex.Services;

namespace VoiceDictationCodex.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    private static readonly Brush RecordingBrush = CreateFrozenBrush(Color.FromRgb(255, 99, 132));

    private readonly ModelCatalogService _catalogService = new();
    private readonly IModelRepository _modelRepository;
    private readonly IModelDownloader _downloadService;
    private readonly IWhisperRuntime _runtime;
    private readonly TranscriptionService _transcriptionService;
    private readonly IAudioCapture _audioCapture;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _dispatcher;
    private readonly string _exportFolder;
    private readonly Func<string, bool> _fileExists;

    private WhisperModelInfo? _selectedModel;
    private CancellationTokenSource? _downloadCancellation;
    private bool _isRecording;
    private bool _disposed;
    private Brush _recordingIndicatorBrush = Brushes.Gray;
    private string _recordingStatusText = "Idle";
    private string _recordingButtonText = "Start Recording";

    public ObservableCollection<WhisperModelInfo> AvailableModels { get; } = new();
    public TranscriptionState ActiveTranscription { get; } = new();

    public WhisperModelInfo? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (Equals(value, _selectedModel))
            {
                return;
            }

            _selectedModel = value;
            RaisePropertyChanged();
            DownloadSelectedModelCommand.RaiseCanExecuteChanged();
            ToggleRecordingCommand.RaiseCanExecuteChanged();
            if (!_isRecording)
            {
                ActiveTranscription.ModelName = value?.Name ?? string.Empty;
                ActiveTranscription.Language = value?.LanguageSupport ?? "Auto";
                RaisePropertyChanged(nameof(ActiveTranscription));
            }
        }
    }

    public Brush RecordingIndicatorBrush
    {
        get => _recordingIndicatorBrush;
        private set
        {
            _recordingIndicatorBrush = value;
            RaisePropertyChanged();
        }
    }

    public string RecordingStatusText
    {
        get => _recordingStatusText;
        private set
        {
            _recordingStatusText = value;
            RaisePropertyChanged();
        }
    }

    public string RecordingButtonText
    {
        get => _recordingButtonText;
        private set
        {
            _recordingButtonText = value;
            RaisePropertyChanged();
        }
    }

    public AsyncRelayCommand DownloadSelectedModelCommand { get; }
    public RelayCommand CancelDownloadCommand { get; }
    public RelayCommand CreateSessionCommand { get; }
    public RelayCommand ImportAudioCommand { get; }
    public AsyncRelayCommand ToggleRecordingCommand { get; }
    public RelayCommand ExportTextCommand { get; }

    /// <summary>Completes when the model list has been loaded.</summary>
    internal Task Initialization { get; }

    /// <summary>Used by XAML. Wires the real services.</summary>
    public MainViewModel()
        : this(
            new JsonModelRepository(),
            null,
            new WhisperRuntime(),
            new AudioCaptureService(),
            new MessageBoxDialogService(),
            new WpfUiDispatcher(),
            AppPaths.ExportFolder,
            File.Exists)
    {
    }

    internal MainViewModel(
        IModelRepository modelRepository,
        IModelDownloader? downloader,
        IWhisperRuntime runtime,
        IAudioCapture audioCapture,
        IDialogService dialogs,
        IUiDispatcher dispatcher,
        string exportFolder,
        Func<string, bool> fileExists)
    {
        _modelRepository = modelRepository;
        _downloadService = downloader ?? new ModelDownloadService(new HttpClient(), modelRepository);
        _runtime = runtime;
        _audioCapture = audioCapture;
        _dialogs = dialogs;
        _dispatcher = dispatcher;
        _exportFolder = exportFolder;
        _fileExists = fileExists;

        _transcriptionService = new TranscriptionService(_runtime);

        DownloadSelectedModelCommand = new AsyncRelayCommand(DownloadSelectedModelAsync, () => SelectedModel is { IsInstalled: false }, ReportCommandFailure);
        CancelDownloadCommand = new RelayCommand(CancelDownload, () => _downloadCancellation is not null);
        CreateSessionCommand = new RelayCommand(CreateSession);
        ImportAudioCommand = new RelayCommand(ImportAudio);

        // Stop must stay available while recording, whatever model is selected.
        ToggleRecordingCommand = new AsyncRelayCommand(ToggleRecordingAsync, () => _isRecording || SelectedModel?.IsInstalled == true, ReportCommandFailure);
        ExportTextCommand = new RelayCommand(ExportText, () => !string.IsNullOrWhiteSpace(ActiveTranscription.Text));

        // Subscribed only after every command is assigned: a lambda created earlier in the constructor
        // sees the commands as possibly null (CS8602).
        // Whisper output arrives on a background thread; bound state and commands may only change on the UI thread.
        _transcriptionService.TextUpdated += (_, text) => _dispatcher.Post(() =>
        {
            ActiveTranscription.Text = text;
            ActiveTranscription.WordCount = TranscriptionState.CountWords(text);
            RaisePropertyChanged(nameof(ActiveTranscription));
            ExportTextCommand.RaiseCanExecuteChanged();
        });
        _audioCapture.BufferLimitReached += (_, _) => _dispatcher.Post(() =>
        {
            _ = StopRecordingAsync("Stopped: recording limit reached");
        });

        Initialization = LoadModelsAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _downloadCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The download finished at the same moment.
        }

        _transcriptionService.Stop();
        _audioCapture.Dispose();
        (_runtime as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private async Task LoadModelsAsync()
    {
        var catalog = _catalogService.GetBuiltInCatalog();
        IReadOnlyList<WhisperModelInfo> saved;
        string? warning = null;
        try
        {
            saved = await _modelRepository.LoadAvailableModelsAsync();
        }
        catch (Exception ex)
        {
            // The built-in catalog is always shown, even when the saved metadata cannot be read.
            saved = Array.Empty<WhisperModelInfo>();
            warning = $"Could not read installed models: {ex.Message}";
        }

        var merged = ModelCatalogMerger.Merge(catalog, saved, _fileExists);
        _dispatcher.Post(() =>
        {
            AvailableModels.Clear();
            foreach (var model in merged)
            {
                AvailableModels.Add(model);
            }

            SelectedModel = AvailableModels.FirstOrDefault();
            if (warning is not null)
            {
                RecordingStatusText = warning;
            }
        });
    }

    private async Task DownloadSelectedModelAsync()
    {
        var model = SelectedModel;
        if (model is null)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        CancelDownloadCommand.RaiseCanExecuteChanged();
        RecordingStatusText = "Downloading model...";
        var progress = new Progress<double>(value =>
        {
            RecordingStatusText = $"Downloading {(int)(value * 100)}%";
        });

        WhisperModelInfo updated;
        try
        {
            updated = await _downloadService.DownloadAsync(model, progress, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            RecordingStatusText = "Download cancelled";
            return;
        }
        catch (Exception ex)
        {
            RecordingStatusText = $"Download failed: {ex.Message}";
            return;
        }
        finally
        {
            _downloadCancellation = null;
            CancelDownloadCommand.RaiseCanExecuteChanged();
        }

        UpdateModel(updated);
        if (SelectedModel is null || SelectedModel.Id == updated.Id)
        {
            SelectedModel = updated;
        }

        RecordingStatusText = "Model ready";
        ToggleRecordingCommand.RaiseCanExecuteChanged();
    }

    private void CancelDownload()
    {
        try
        {
            _downloadCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The download finished at the same moment.
        }
    }

    private void UpdateModel(WhisperModelInfo updated)
    {
        var index = AvailableModels.ToList().FindIndex(m => m.Id == updated.Id);
        if (index >= 0)
        {
            AvailableModels[index] = updated;
            RaisePropertyChanged(nameof(AvailableModels));
        }
        else
        {
            AvailableModels.Add(updated);
        }
    }

    private void CreateSession()
    {
        ActiveTranscription.Text = string.Empty;
        ActiveTranscription.Duration = "00:00";
        ActiveTranscription.WordCount = 0;
        ActiveTranscription.ModelName = SelectedModel?.Name ?? string.Empty;
        ActiveTranscription.Language = SelectedModel?.LanguageSupport ?? "Auto";
        RaisePropertyChanged(nameof(ActiveTranscription));
        ExportTextCommand.RaiseCanExecuteChanged();
    }

    private void ImportAudio()
    {
        _dialogs.ShowInfo("Audio import workflow will go here.", "Import Audio");
    }

    private async Task ToggleRecordingAsync()
    {
        if (_isRecording)
        {
            await StopRecordingAsync("Idle");
            return;
        }

        var model = SelectedModel;
        if (model is null || !model.IsInstalled)
        {
            return;
        }

        SetRecordingState(true, "Listening");
        ActiveTranscription.ModelName = model.Name;
        ActiveTranscription.Language = model.LanguageSupport;
        RaisePropertyChanged(nameof(ActiveTranscription));

        var started = false;
        string failure = string.Empty;
        try
        {
            _audioCapture.Start();
            _ = ObserveSessionAsync(_transcriptionService.StartAsync(model.LocalPath));
            started = true;
        }
        catch (Exception ex)
        {
            // Missing microphone: reset instead of leaving the UI in the recording state.
            failure = $"Could not start: {ex.Message}";
        }

        if (!started)
        {
            await StopRecordingAsync(failure);
        }
    }

    /// <summary>Watches one whisper run and resets the UI when the process ends on its own or fails to start.</summary>
    private async Task ObserveSessionAsync(Task<WhisperExit> run)
    {
        string status;
        try
        {
            var exit = await run.ConfigureAwait(false);
            if (exit.StoppedByUser)
            {
                return;
            }

            status = DescribeExit(exit);
        }
        catch (Exception ex)
        {
            status = $"Could not start: {ex.Message}";
        }

        _dispatcher.Post(() =>
        {
            _ = StopRecordingAsync(status);
        });
    }

    internal static string DescribeExit(WhisperExit exit)
    {
        if (exit.ExitCode == 0)
        {
            return "Whisper finished";
        }

        var lastLine = exit.StandardErrorTail
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return string.IsNullOrEmpty(lastLine)
            ? $"Whisper exited (code {exit.ExitCode})"
            : $"Whisper exited (code {exit.ExitCode}): {lastLine}";
    }

    private async Task StopRecordingAsync(string status)
    {
        if (!_isRecording)
        {
            return;
        }

        SetRecordingState(false, status);
        _transcriptionService.Stop();
        try
        {
            await _audioCapture.StopAsync();
        }
        catch (Exception ex)
        {
            RecordingStatusText = $"{status} (audio device: {ex.Message})";
        }
    }

    private void SetRecordingState(bool recording, string status)
    {
        _isRecording = recording;
        RecordingIndicatorBrush = recording ? RecordingBrush : Brushes.Gray;
        RecordingStatusText = status;
        RecordingButtonText = recording ? "Stop Recording" : "Start Recording";
        ToggleRecordingCommand.RaiseCanExecuteChanged();
    }

    private void ReportCommandFailure(Exception exception)
    {
        RecordingStatusText = $"Something went wrong: {exception.Message}";
    }

    private void ExportText()
    {
        string path;
        try
        {
            Directory.CreateDirectory(_exportFolder);
            path = Path.Combine(_exportFolder, $"transcription-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, ActiveTranscription.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // A protected or redirected Documents folder must not lose the transcript.
            RecordingStatusText = $"Export failed: {ex.Message}";
            _dialogs.ShowInfo($"The transcription could not be exported: {ex.Message}", "Export failed");
            return;
        }

        _dialogs.ShowInfo($"Transcription exported to {path}", "Export Complete");
    }
}
