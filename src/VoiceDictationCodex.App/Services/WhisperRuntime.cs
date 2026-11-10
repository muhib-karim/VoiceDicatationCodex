using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceDictationCodex.Services;

/// <summary>Outcome of one whisper process run.</summary>
/// <param name="ExitCode">Exit code reported by the process.</param>
/// <param name="StandardErrorTail">Last lines the process wrote to stderr.</param>
/// <param name="StoppedByUser">True when the run ended because of <see cref="IWhisperRuntime.Stop"/> or cancellation.</param>
public sealed record WhisperExit(int ExitCode, string StandardErrorTail, bool StoppedByUser);

public interface IWhisperRuntime
{
    bool IsRunning { get; }

    event EventHandler<string>? TranscriptionReceived;

    /// <summary>Starts the process. The task completes when the process has exited.</summary>
    Task<WhisperExit> StartAsync(string modelPath, CancellationToken cancellationToken = default);

    void Stop();
}

public class WhisperRuntime : IWhisperRuntime, IDisposable
{
    /// <summary>Environment variable that overrides the whisper.cpp executable location.</summary>
    public const string ExecutablePathVariable = "VOICEDICTATION_WHISPER_PATH";
    public const string DefaultExecutable = "whisper-cli.exe";

    internal const int StandardErrorTailLines = 20;
    private const int MaxTailLineLength = 2000;

    private readonly Func<string, ProcessStartInfo> _startInfoFactory;
    private readonly object _gate = new();
    private Process? _whisperProcess;
    private bool _stopRequested;

    public WhisperRuntime()
        : this(CreateStartInfo)
    {
    }

    internal WhisperRuntime(Func<string, ProcessStartInfo> startInfoFactory)
    {
        _startInfoFactory = startInfoFactory;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _whisperProcess is not null;
            }
        }
    }

    public event EventHandler<string>? TranscriptionReceived;

    public async Task<WhisperExit> StartAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        Process process;
        lock (_gate)
        {
            if (_whisperProcess is not null)
            {
                throw new InvalidOperationException("The whisper runtime is already running.");
            }

            _stopRequested = false;
            process = Process.Start(_startInfoFactory(modelPath))
                ?? throw new InvalidOperationException("Unable to start whisper runtime");
            _whisperProcess = process;
        }

        try
        {
            using var registration = cancellationToken.Register(() => TryKill(process));
            var errorTail = new Queue<string>();

            // Both pipes are drained concurrently. Leaving stderr unread blocks the child once the pipe fills.
            var standardError = Task.Run(() => DrainStandardErrorAsync(process, errorTail));
            var standardOutput = Task.Run(() => PumpStandardOutputAsync(process));
            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

            bool stopped;
            lock (_gate)
            {
                stopped = _stopRequested || cancellationToken.IsCancellationRequested;
            }

            string tail;
            lock (errorTail)
            {
                tail = string.Join(Environment.NewLine, errorTail);
            }

            return new WhisperExit(process.ExitCode, tail, stopped);
        }
        finally
        {
            lock (_gate)
            {
                _whisperProcess = null;
            }

            process.Dispose();
        }
    }

    public static string ResolveExecutablePath()
    {
        var overridePath = Environment.GetEnvironmentVariable(ExecutablePathVariable);
        return string.IsNullOrWhiteSpace(overridePath) ? DefaultExecutable : overridePath.Trim();
    }

    /// <summary>Arguments are passed as a list so that paths with spaces, quotes or a trailing backslash survive intact.</summary>
    public static IReadOnlyList<string> BuildArgumentList(string modelPath) => new[] { "--model", modelPath, "--output-json" };

    internal static ProcessStartInfo CreateStartInfo(string modelPath)
        => CreateStartInfo(ResolveExecutablePath(), BuildArgumentList(modelPath));

    internal static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // whisper.cpp writes UTF-8; without this Windows decodes the pipes with the ANSI/OEM code page.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /// <summary>Requests the process to end. The task returned by <see cref="StartAsync"/> completes once it has.</summary>
    public void Stop()
    {
        Process? process;
        lock (_gate)
        {
            process = _whisperProcess;
            if (process is null)
            {
                return;
            }

            _stopRequested = true;
        }

        TryKill(process);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private async Task PumpStandardOutputAsync(Process process)
    {
        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            try
            {
                TranscriptionReceived?.Invoke(this, line);
            }
            catch (Exception ex)
            {
                // A faulty subscriber must not end the session or leave the pipe undrained.
                Trace.TraceError("Transcription handler failed: {0}", ex);
            }
        }
    }

    private static async Task DrainStandardErrorAsync(Process process, Queue<string> tail)
    {
        string? line;
        while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            var entry = line.Length > MaxTailLineLength ? line.Substring(0, MaxTailLineLength) : line;
            lock (tail)
            {
                tail.Enqueue(entry);
                if (tail.Count > StandardErrorTailLines)
                {
                    tail.Dequeue();
                }
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process has already exited or been disposed.
        }
        catch (Win32Exception)
        {
            // The process is exiting or cannot be terminated.
        }
        catch (AggregateException)
        {
            // Part of the process tree could not be terminated.
        }
    }
}
