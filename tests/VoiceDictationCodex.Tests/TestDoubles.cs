using System.IO;
using System.Net.Http;
using VoiceDictationCodex.Models;
using VoiceDictationCodex.Services;

namespace VoiceDictationCodex.Tests;

/// <summary>In-memory repository that records what was saved.</summary>
internal sealed class RecordingModelRepository : IModelRepository
{
    public List<WhisperModelInfo> Saved { get; } = new();

    public Exception? LoadFailure { get; set; }

    public Exception? SaveFailure { get; set; }

    public Task<IReadOnlyList<WhisperModelInfo>> LoadAvailableModelsAsync(CancellationToken cancellationToken = default)
        => LoadFailure is not null
            ? Task.FromException<IReadOnlyList<WhisperModelInfo>>(LoadFailure)
            : Task.FromResult<IReadOnlyList<WhisperModelInfo>>(Saved.ToList());

    public Task<WhisperModelInfo?> GetInstalledModelAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(Saved.FirstOrDefault(model => model.Id == id));

    public Task SaveModelMetadataAsync(WhisperModelInfo model, CancellationToken cancellationToken = default)
    {
        if (SaveFailure is not null)
        {
            return Task.FromException(SaveFailure);
        }

        Saved.RemoveAll(existing => existing.Id == model.Id);
        Saved.Add(model);
        return Task.CompletedTask;
    }
}

/// <summary>Answers every HTTP request from a delegate, so no test touches the network.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    public List<Uri?> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri);
        return Task.FromResult(_respond(request));
    }
}

/// <summary>Read-only stream whose chunks come from a delegate. An empty chunk ends the stream.</summary>
internal sealed class ScriptedStream : Stream
{
    private readonly Func<int, CancellationToken, Task<byte[]>> _next;
    private int _call;

    public ScriptedStream(Func<int, CancellationToken, Task<byte[]>> next)
    {
        _next = next;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var chunk = await _next(_call++, cancellationToken);
        chunk.AsMemory().CopyTo(buffer);
        return chunk.Length;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Reports progress on the calling thread, unlike <see cref="Progress{T}"/>.</summary>
internal sealed class SynchronousProgress : IProgress<double>
{
    public List<double> Values { get; } = new();

    public void Report(double value) => Values.Add(value);
}
