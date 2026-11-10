using System.IO;
using VoiceDictationCodex.Models;
using VoiceDictationCodex.Services;
using VoiceDictationCodex.ViewModels;
using Xunit;

namespace VoiceDictationCodex.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vdc-vm-tests-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingModelRepository _repository = new();
    private readonly FakeDownloader _downloader = new();
    private readonly FakeWhisperRuntime _runtime = new();
    private readonly FakeAudioCapture _capture = new();
    private readonly RecordingDialogs _dialogs = new();

    private static WhisperModelInfo Installed(string id = "ggml-base.en")
        => new(id, "Saved", "Saved model", 1, "saved-" + id + ".bin", "English", true);

    private MainViewModel Create(IUiDispatcher? dispatcher = null, Func<string, bool>? fileExists = null, string? exportFolder = null)
        => new(
            _repository,
            _downloader,
            _runtime,
            _capture,
            _dialogs,
            dispatcher ?? new ImmediateDispatcher(),
            exportFolder ?? _folder,
            fileExists ?? (_ => true));

    private async Task<MainViewModel> CreateWithInstalledModelAsync(IUiDispatcher? dispatcher = null)
    {
        _repository.Saved.Add(Installed());
        var viewModel = Create(dispatcher);
        await viewModel.Initialization;
        return viewModel;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The expected state was not reached in time.");
            }

            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Initialization_ShowsTheCatalog_AndMarksSavedModelsInstalled()
    {
        using var viewModel = await CreateWithInstalledModelAsync();

        Assert.Equal(3, viewModel.AvailableModels.Count);
        Assert.True(viewModel.AvailableModels.Single(m => m.Id == "ggml-base.en").IsInstalled);
        Assert.Equal("saved-ggml-base.en.bin", viewModel.AvailableModels.Single(m => m.Id == "ggml-base.en").LocalPath);
        Assert.Equal(viewModel.AvailableModels[0], viewModel.SelectedModel);
        Assert.True(viewModel.ToggleRecordingCommand.CanExecute(null));
    }

    [Fact]
    public async Task Initialization_TreatsAModelAsNotInstalled_WhenItsFileIsMissing()
    {
        _repository.Saved.Add(Installed());
        using var viewModel = Create(fileExists: _ => false);
        await viewModel.Initialization;

        Assert.All(viewModel.AvailableModels, model => Assert.False(model.IsInstalled));
        Assert.True(viewModel.DownloadSelectedModelCommand.CanExecute(null));
        Assert.False(viewModel.ToggleRecordingCommand.CanExecute(null));
    }

    [Fact]
    public async Task Initialization_StillShowsTheCatalog_WhenSavedMetadataCannotBeRead()
    {
        _repository.LoadFailure = new IOException("file is locked");
        using var viewModel = Create();
        await viewModel.Initialization;

        Assert.Equal(3, viewModel.AvailableModels.Count);
        Assert.Contains("file is locked", viewModel.RecordingStatusText);
    }

    [Fact]
    public async Task TranscriptUpdates_FromABackgroundThread_AreAppliedOnlyThroughTheDispatcher()
    {
        var dispatcher = new QueuedDispatcher();
        using var viewModel = await CreateWithInstalledModelAsync(dispatcher);
        dispatcher.Pump();
        var exportChanges = 0;
        viewModel.ExportTextCommand.CanExecuteChanged += (_, _) => exportChanges++;
        await viewModel.ToggleRecordingCommand.ExecuteAsync();

        await Task.Run(() => _runtime.EmitLine("hello world"));

        Assert.Equal(string.Empty, viewModel.ActiveTranscription.Text);
        Assert.Equal(0, exportChanges);

        dispatcher.Pump();

        Assert.Equal("hello world" + Environment.NewLine, viewModel.ActiveTranscription.Text);
        Assert.Equal(2, viewModel.ActiveTranscription.WordCount);
        Assert.Equal(1, exportChanges);
        Assert.True(viewModel.ExportTextCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhisperExitingOnItsOwn_ResetsTheUi_AndStopsCapture()
    {
        using var viewModel = await CreateWithInstalledModelAsync();
        await viewModel.ToggleRecordingCommand.ExecuteAsync();
        Assert.Equal("Stop Recording", viewModel.RecordingButtonText);
        Assert.Equal("saved-ggml-base.en.bin", _runtime.StartedModelPath);
        Assert.True(_capture.IsCapturing);

        _runtime.Exit(3, "loading model" + Environment.NewLine + "error: no input files specified");
        await WaitUntilAsync(() => _capture.StopCalls == 1);

        Assert.Equal("Start Recording", viewModel.RecordingButtonText);
        Assert.Equal("Whisper exited (code 3): error: no input files specified", viewModel.RecordingStatusText);
        Assert.False(_capture.IsCapturing);
        Assert.True(viewModel.ToggleRecordingCommand.CanExecute(null));
    }

    [Fact]
    public async Task UserStop_ShowsIdle_AndIsNotReportedAsAFailure()
    {
        using var viewModel = await CreateWithInstalledModelAsync();
        await viewModel.ToggleRecordingCommand.ExecuteAsync();

        await viewModel.ToggleRecordingCommand.ExecuteAsync();
        await Task.Delay(200);

        Assert.Equal("Idle", viewModel.RecordingStatusText);
        Assert.Equal("Start Recording", viewModel.RecordingButtonText);
        Assert.Equal(1, _runtime.StopCalls);
        Assert.Equal(1, _capture.StopCalls);
    }

    [Fact]
    public async Task RuntimeStartFailure_ResetsTheUi()
    {
        _runtime.StartFailure = new InvalidOperationException("whisper-cli.exe was not found");
        using var viewModel = await CreateWithInstalledModelAsync();

        await viewModel.ToggleRecordingCommand.ExecuteAsync();
        await WaitUntilAsync(() => _capture.StopCalls == 1);

        Assert.Equal("Could not start: whisper-cli.exe was not found", viewModel.RecordingStatusText);
        Assert.Equal("Start Recording", viewModel.RecordingButtonText);
    }

    [Fact]
    public async Task MissingMicrophone_ResetsTheUi_AndDoesNotStartWhisper()
    {
        _capture.StartFailure = new InvalidOperationException("no microphone");
        using var viewModel = await CreateWithInstalledModelAsync();

        await viewModel.ToggleRecordingCommand.ExecuteAsync();

        Assert.Equal("Could not start: no microphone", viewModel.RecordingStatusText);
        Assert.Equal("Start Recording", viewModel.RecordingButtonText);
        Assert.Null(_runtime.StartedModelPath);
    }

    [Fact]
    public async Task StopStaysAvailable_WhenAnUninstalledModelIsSelectedWhileRecording()
    {
        using var viewModel = await CreateWithInstalledModelAsync();
        await viewModel.ToggleRecordingCommand.ExecuteAsync();

        viewModel.SelectedModel = viewModel.AvailableModels.First(model => !model.IsInstalled);

        Assert.True(viewModel.ToggleRecordingCommand.CanExecute(null));
        await viewModel.ToggleRecordingCommand.ExecuteAsync();
        Assert.Equal("Start Recording", viewModel.RecordingButtonText);
        Assert.False(viewModel.ToggleRecordingCommand.CanExecute(null));
    }

    [Fact]
    public async Task RecordingLimit_StopsTheSession()
    {
        using var viewModel = await CreateWithInstalledModelAsync();
        await viewModel.ToggleRecordingCommand.ExecuteAsync();

        _capture.RaiseBufferLimit();

        Assert.Equal("Stopped: recording limit reached", viewModel.RecordingStatusText);
        Assert.Equal(1, _capture.StopCalls);
    }

    [Fact]
    public async Task Download_Success_MarksTheModelInstalled()
    {
        using var viewModel = Create();
        await viewModel.Initialization;

        await viewModel.DownloadSelectedModelCommand.ExecuteAsync();

        Assert.Equal("Model ready", viewModel.RecordingStatusText);
        Assert.True(viewModel.SelectedModel?.IsInstalled);
        Assert.True(viewModel.AvailableModels[0].IsInstalled);
        Assert.True(viewModel.ToggleRecordingCommand.CanExecute(null));
        Assert.False(viewModel.CancelDownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Download_CanBeCancelled()
    {
        _downloader.Handler = async (model, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return model;
        };
        using var viewModel = Create();
        await viewModel.Initialization;

        var run = viewModel.DownloadSelectedModelCommand.ExecuteAsync();
        Assert.True(viewModel.CancelDownloadCommand.CanExecute(null));
        viewModel.CancelDownloadCommand.Execute(null);
        await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("Download cancelled", viewModel.RecordingStatusText);
        Assert.False(viewModel.SelectedModel?.IsInstalled);
        Assert.False(viewModel.CancelDownloadCommand.CanExecute(null));
        Assert.True(viewModel.DownloadSelectedModelCommand.CanExecute(null));
    }

    [Fact]
    public async Task Download_UnexpectedFailure_IsReportedInsteadOfThrown()
    {
        _downloader.Handler = (_, _) => Task.FromException<WhisperModelInfo>(new InvalidOperationException("metadata could not be saved"));
        using var viewModel = Create();
        await viewModel.Initialization;

        await viewModel.DownloadSelectedModelCommand.ExecuteAsync();

        Assert.Equal("Download failed: metadata could not be saved", viewModel.RecordingStatusText);
        Assert.False(viewModel.SelectedModel?.IsInstalled);
    }

    [Fact]
    public async Task Export_WritesTheTranscript()
    {
        using var viewModel = Create();
        await viewModel.Initialization;
        _runtime.EmitLine("hello");

        viewModel.ExportTextCommand.Execute(null);

        var file = Assert.Single(Directory.GetFiles(_folder, "transcription-*.txt"));
        Assert.Equal("hello" + Environment.NewLine, await File.ReadAllTextAsync(file));
        Assert.Contains("exported", Assert.Single(_dialogs.Messages));
    }

    [Fact]
    public async Task Export_Failure_IsReportedInsteadOfThrown()
    {
        Directory.CreateDirectory(_folder);
        var blocker = Path.Combine(_folder, "not-a-folder");
        await File.WriteAllTextAsync(blocker, "a file where the export folder should be");
        using var viewModel = Create(exportFolder: blocker);
        await viewModel.Initialization;
        _runtime.EmitLine("hello");

        viewModel.ExportTextCommand.Execute(null);

        Assert.StartsWith("Export failed", viewModel.RecordingStatusText);
        Assert.Contains("could not be exported", Assert.Single(_dialogs.Messages));
    }

    [Fact]
    public async Task Dispose_StopsWhisper_ReleasesCapture_AndCancelsTheDownload()
    {
        _downloader.Handler = async (model, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return model;
        };
        var viewModel = await CreateWithInstalledModelAsync();
        await viewModel.ToggleRecordingCommand.ExecuteAsync();
        viewModel.SelectedModel = viewModel.AvailableModels.First(model => !model.IsInstalled);
        var download = viewModel.DownloadSelectedModelCommand.ExecuteAsync();

        viewModel.Dispose();
        await download.WaitAsync(TimeSpan.FromSeconds(30));
        viewModel.Dispose();

        Assert.Equal(1, _runtime.StopCalls);
        Assert.True(_capture.Disposed);
        Assert.True(_downloader.LastToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData(0, "", "Whisper finished")]
    [InlineData(2, "", "Whisper exited (code 2)")]
    [InlineData(2, "first line\nlast line\n", "Whisper exited (code 2): last line")]
    public void DescribeExit_SummarisesTheExitCodeAndLastErrorLine(int exitCode, string standardError, string expected)
    {
        Assert.Equal(expected, MainViewModel.DescribeExit(new WhisperExit(exitCode, standardError, false)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
