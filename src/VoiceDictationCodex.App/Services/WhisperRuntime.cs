using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace VoiceDictationCodex.Services;

public class WhisperRuntime
{
    /// <summary>Environment variable that overrides the whisper.cpp executable location.</summary>
    public const string ExecutablePathVariable = "VOICEDICTATION_WHISPER_PATH";
    public const string DefaultExecutable = "whisper.cpp.exe";

    private Process? _whisperProcess;

    public bool IsRunning => _whisperProcess is { HasExited: false };

    public event EventHandler<string>? TranscriptionReceived;

    public async Task StartAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveExecutablePath(),
            Arguments = BuildArguments(modelPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _whisperProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start whisper runtime");

        await Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested && _whisperProcess is { HasExited: false })
            {
                var line = await _whisperProcess.StandardOutput.ReadLineAsync();
                if (line is null)
                {
                    break;
                }

                TranscriptionReceived?.Invoke(this, line);
            }
        }, cancellationToken);
    }

    public static string ResolveExecutablePath()
    {
        var overridePath = Environment.GetEnvironmentVariable(ExecutablePathVariable);
        return string.IsNullOrWhiteSpace(overridePath) ? DefaultExecutable : overridePath.Trim();
    }

    public static string BuildArguments(string modelPath) => $"--model \"{modelPath}\" --output-json";

    public void Stop()
    {
        if (_whisperProcess is { HasExited: false })
        {
            _whisperProcess.Kill(true);
            _whisperProcess.Dispose();
        }

        _whisperProcess = null;
    }
}
