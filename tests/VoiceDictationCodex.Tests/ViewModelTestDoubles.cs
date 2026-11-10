using VoiceDictationCodex.Models;
using VoiceDictationCodex.Services;

namespace VoiceDictationCodex.Tests;

internal sealed class FakeWhisperRuntime : IWhisperRuntime
{
    private TaskCompletionSource<WhisperExit>? _run;

    public event EventHandler<string>? TranscriptionReceived;

    public bool IsRunning => _run is not null && !_run.Task.IsCompleted;

    public int StopCalls { get; private set; }

    public Exception? StartFailure { get; set; }

    public string? StartedModelPath { get; private set; }

    public Task<WhisperExit> StartAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        if (StartFailure is not null)
        {
            return Task.FromException<WhisperExit>(StartFailure);
        }

        StartedModelPath = modelPath;
        _run = new TaskCompletionSource<WhisperExit>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _run.Task;
    }

    public void Stop()
    {
        StopCalls++;
        _run?.TrySetResult(new WhisperExit(-1, string.Empty, true));
    }

    /// <summary>Simulates the process ending on its own.</summary>
    public void Exit(int exitCode, string standardError)
        => _run?.TrySetResult(new WhisperExit(exitCode, standardError, false));

    public void EmitLine(string line) => TranscriptionReceived?.Invoke(this, line);
}

internal sealed class FakeAudioCapture : IAudioCapture
{
    public event EventHandler? BufferLimitReached;

    public bool IsCapturing { get; private set; }

    public bool Disposed { get; private set; }

    public int StopCalls { get; private set; }

    public Exception? StartFailure { get; set; }

    public void Start(int deviceNumber = 0)
    {
        if (StartFailure is not null)
        {
            throw StartFailure;
        }

        IsCapturing = true;
    }

    public Task<byte[]> StopAsync()
    {
        StopCalls++;
        IsCapturing = false;
        return Task.FromResult(Array.Empty<byte>());
    }

    public void RaiseBufferLimit() => BufferLimitReached?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        Disposed = true;
        IsCapturing = false;
    }
}

internal sealed class FakeDownloader : IModelDownloader
{
    public Func<WhisperModelInfo, CancellationToken, Task<WhisperModelInfo>> Handler { get; set; } = (model, _) => Task.FromResult(model with { IsInstalled = true, LocalPath = "installed.bin" });

    public CancellationToken LastToken { get; private set; }

    public Task<WhisperModelInfo> DownloadAsync(WhisperModelInfo model, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        LastToken = cancellationToken;
        return Handler(model, cancellationToken);
    }
}

internal sealed class RecordingDialogs : IDialogService
{
    public List<string> Messages { get; } = new();

    public void ShowInfo(string message, string title) => Messages.Add(title + ": " + message);
}

/// <summary>Runs posted work immediately on the calling thread.</summary>
internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

/// <summary>Holds posted work until the test pumps it, like a UI thread that has not yet run its queue.</summary>
internal sealed class QueuedDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _queue = new();

    public void Post(Action action)
    {
        lock (_queue)
        {
            _queue.Enqueue(action);
        }
    }

    public void Pump()
    {
        while (true)
        {
            Action action;
            lock (_queue)
            {
                if (_queue.Count == 0)
                {
                    return;
                }

                action = _queue.Dequeue();
            }

            action();
        }
    }
}
