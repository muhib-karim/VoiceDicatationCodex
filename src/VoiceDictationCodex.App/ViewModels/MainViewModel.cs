using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using VoiceDictationCodex.Models;
using VoiceDictationCodex.Services;

namespace VoiceDictationCodex.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly ModelCatalogService _catalogService = new();
    private readonly IModelRepository _modelRepository = new JsonModelRepository();
    private readonly ModelDownloadService _downloadService;
    private readonly WhisperRuntime _runtime = new();
    private readonly TranscriptionService _transcriptionService;
    private readonly AudioCaptureService _audioCapture = new();

    private WhisperModelInfo? _selectedModel;
    private bool _isRecording;
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
    public RelayCommand CreateSessionCommand { get; }
    public RelayCommand ImportAudioCommand { get; }
    public RelayCommand ToggleRecordingCommand { get; }
    public RelayCommand ExportTextCommand { get; }

    public MainViewModel()
    {
        _downloadService = new ModelDownloadService(new HttpClient(), _modelRepository);
        _transcriptionService = new TranscriptionService(_runtime);
        _transcriptionService.TextUpdated += (_, text) =>
        {
            ActiveTranscription.Text = text;
            ActiveTranscription.WordCount = TranscriptionState.CountWords(text);
            RaisePropertyChanged(nameof(ActiveTranscription));
            ExportTextCommand.RaiseCanExecuteChanged();
        };

        DownloadSelectedModelCommand = new AsyncRelayCommand(DownloadSelectedModelAsync, () => SelectedModel is { IsInstalled: false });
        CreateSessionCommand = new RelayCommand(CreateSession);
        ImportAudioCommand = new RelayCommand(ImportAudio);
        ToggleRecordingCommand = new RelayCommand(ToggleRecording, () => SelectedModel?.IsInstalled == true);
        ExportTextCommand = new RelayCommand(ExportText, () => !string.IsNullOrWhiteSpace(ActiveTranscription.Text));

        _ = LoadModelsAsync();
    }

    private async Task LoadModelsAsync()
    {
        var catalog = _catalogService.GetBuiltInCatalog();
        var installed = await _modelRepository.LoadAvailableModelsAsync();
        var merged = catalog.Select(model =>
        {
            var saved = installed.FirstOrDefault(savedModel => savedModel.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase));
            return saved is null ? model : model with { LocalPath = saved.LocalPath, IsInstalled = saved.IsInstalled };
        }).ToList();

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            AvailableModels.Clear();
            foreach (var model in merged)
            {
                AvailableModels.Add(model);
            }

            SelectedModel = AvailableModels.FirstOrDefault();
        }
        else
        {
            await dispatcher.InvokeAsync(() =>
            {
                AvailableModels.Clear();
                foreach (var model in merged)
                {
                    AvailableModels.Add(model);
                }

                SelectedModel = AvailableModels.FirstOrDefault();
            });
        }
    }

    private async Task DownloadSelectedModelAsync()
    {
        if (SelectedModel is null)
        {
            return;
        }

        RecordingStatusText = "Downloading model...";
        var progress = new Progress<double>(value =>
        {
            RecordingStatusText = $"Downloading {(int)(value * 100)}%";
        });

        WhisperModelInfo updated;
        try
        {
            updated = await _downloadService.DownloadAsync(SelectedModel, progress);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            RecordingStatusText = $"Download failed: {ex.Message}";
            return;
        }

        UpdateModel(updated);
        SelectedModel = updated;
        RecordingStatusText = "Model ready";
        ToggleRecordingCommand.RaiseCanExecuteChanged();
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
        MessageBox.Show("Audio import workflow will go here.", "Import Audio", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void ToggleRecording()
    {
        if (_isRecording)
        {
            _audioCapture.Stop();
            _transcriptionService.Stop();
            _isRecording = false;
            RecordingIndicatorBrush = Brushes.Gray;
            RecordingStatusText = "Idle";
            RecordingButtonText = "Start Recording";
        }
        else if (SelectedModel?.IsInstalled == true)
        {
            _isRecording = true;
            RecordingIndicatorBrush = new SolidColorBrush(Color.FromRgb(255, 99, 132));
            RecordingStatusText = "Listening";
            RecordingButtonText = "Stop Recording";
            ActiveTranscription.ModelName = SelectedModel.Name;
            ActiveTranscription.Language = SelectedModel.LanguageSupport;
            RaisePropertyChanged(nameof(ActiveTranscription));
            try
            {
                _audioCapture.Start();
                await _transcriptionService.StartAsync(SelectedModel.LocalPath);
            }
            catch (Exception ex)
            {
                // Missing microphone or whisper runtime: reset instead of crashing the async void handler.
                _audioCapture.Stop();
                _transcriptionService.Stop();
                _isRecording = false;
                RecordingIndicatorBrush = Brushes.Gray;
                RecordingStatusText = $"Could not start: {ex.Message}";
                RecordingButtonText = "Start Recording";
            }
        }
    }

    private void ExportText()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VoiceDictationCodex", $"transcription-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ActiveTranscription.Text);
        MessageBox.Show($"Transcription exported to {path}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
