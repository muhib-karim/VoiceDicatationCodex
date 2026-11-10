using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;

namespace VoiceDictationCodex.Services;

public class TranscriptionService
{
    private readonly IWhisperRuntime _runtime;
    private readonly StringBuilder _buffer = new();

    public event EventHandler<string>? TextUpdated;

    public TranscriptionService(IWhisperRuntime runtime)
    {
        _runtime = runtime;
        _runtime.TranscriptionReceived += HandleTranscriptionLine;
    }

    /// <summary>Starts the runtime. The task completes when the whisper process has exited.</summary>
    public Task<WhisperExit> StartAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        lock (_buffer)
        {
            _buffer.Clear();
        }

        return _runtime.StartAsync(modelPath, cancellationToken);
    }

    public void Stop()
    {
        _runtime.Stop();
    }

    private void HandleTranscriptionLine(object? sender, string line) => ProcessLine(line);

    internal void ProcessLine(string line)
    {
        string? chunk = null;
        var isJsonObject = false;
        try
        {
            using var doc = JsonDocument.Parse(line);

            // Only JSON objects are structured output. A bare number, string, true or null is ordinary transcript text.
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                isJsonObject = true;
                if (doc.RootElement.TryGetProperty("text", out var textElement)
                    && textElement.ValueKind == JsonValueKind.String)
                {
                    chunk = textElement.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON output from runtime, treated as raw text below.
        }

        string snapshot;
        lock (_buffer)
        {
            if (isJsonObject)
            {
                if (string.IsNullOrWhiteSpace(chunk))
                {
                    return;
                }

                _buffer.Append(chunk);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                _buffer.AppendLine(line);
            }

            snapshot = _buffer.ToString();
        }

        TextUpdated?.Invoke(this, snapshot);
    }
}
