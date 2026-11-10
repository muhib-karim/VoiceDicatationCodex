using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using VoiceDictationCodex.Models;

namespace VoiceDictationCodex.Services;

public interface IModelDownloader
{
    Task<WhisperModelInfo> DownloadAsync(WhisperModelInfo model, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when a downloaded model does not match its expected size or SHA-256.</summary>
public class ModelIntegrityException : IOException
{
    public ModelIntegrityException(string message)
        : base(message)
    {
    }
}

public class ModelDownloadService : IModelDownloader
{
    /// <summary>Commit of the Hugging Face repository the catalog sizes and hashes were taken from.</summary>
    public const string ModelRevision = "5359861c739e955e79d9a303bcbc70fb988958b1";

    private const int BufferSize = 81920;
    private static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _httpClient;
    private readonly IModelRepository _modelRepository;
    private readonly string _modelsFolder;
    private readonly TimeSpan _stallTimeout;

    public ModelDownloadService(HttpClient httpClient, IModelRepository modelRepository)
        : this(httpClient, modelRepository, AppPaths.ModelsFolder, DefaultStallTimeout)
    {
    }

    internal ModelDownloadService(HttpClient httpClient, IModelRepository modelRepository, string modelsFolder, TimeSpan stallTimeout)
    {
        _httpClient = httpClient;
        _modelRepository = modelRepository;
        _modelsFolder = modelsFolder;
        _stallTimeout = stallTimeout;
    }

    public async Task<WhisperModelInfo> DownloadAsync(WhisperModelInfo model, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateModelId(model.Id);
        Directory.CreateDirectory(_modelsFolder);

        var destinationFile = Path.Combine(_modelsFolder, $"{model.Id}.bin");
        var partialFile = destinationFile + ".partial";

        // The download goes to a .partial file and is moved into place only after it has been verified,
        // so a failed, cancelled or corrupted download never leaves a file at the final path.
        try
        {
            await DownloadAndVerifyAsync(model, partialFile, progress, cancellationToken).ConfigureAwait(false);
            File.Move(partialFile, destinationFile, overwrite: true);
        }
        catch
        {
            TryDelete(partialFile);
            throw;
        }

        var updatedModel = model with { LocalPath = destinationFile, IsInstalled = true };
        await _modelRepository.SaveModelMetadataAsync(updatedModel, cancellationToken).ConfigureAwait(false);
        return updatedModel;
    }

    internal static Uri GetDownloadUrl(string modelId)
        => new($"https://huggingface.co/ggerganov/whisper.cpp/resolve/{ModelRevision}/{modelId}.bin");

    internal static void ValidateModelId(string? modelId)
    {
        var valid = !string.IsNullOrEmpty(modelId)
            && modelId.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '_' || c == '-')
            && !modelId.Contains("..", StringComparison.Ordinal);
        if (!valid)
        {
            throw new ArgumentException($"'{modelId}' is not a valid model id.", nameof(modelId));
        }
    }

    private async Task DownloadAndVerifyAsync(WhisperModelInfo model, string partialFile, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(GetDownloadUrl(model.Id), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var expectedBytes = model.SizeBytes > 0 ? model.SizeBytes : response.Content.Headers.ContentLength ?? -1L;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long totalRead = 0;
        var lastPercent = -1;

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var fileStream = new FileStream(partialFile, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        try
        {
            var buffer = new byte[BufferSize];
            while (true)
            {
                var read = await ReadWithStallTimeoutAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                totalRead += read;

                if (expectedBytes > 0)
                {
                    // Report once per whole percent instead of once per chunk.
                    var percent = (int)Math.Min(100, totalRead * 100 / expectedBytes);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Report(percent / 100.0);
                    }
                }
            }

            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Both streams are closed before the caller moves or deletes the partial file.
            await fileStream.DisposeAsync().ConfigureAwait(false);
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        if (model.SizeBytes > 0 && totalRead != model.SizeBytes)
        {
            throw new ModelIntegrityException($"Downloaded {totalRead} bytes for {model.Id} but expected {model.SizeBytes}.");
        }

        if (!string.IsNullOrWhiteSpace(model.Sha256))
        {
            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!actual.Equals(model.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new ModelIntegrityException($"SHA-256 mismatch for {model.Id}: the downloaded file is corrupted or was altered.");
            }
        }
    }

    private async Task<int> ReadWithStallTimeoutAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        using var stallCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stallCancellation.CancelAfter(_stallTimeout);
        try
        {
            return await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), stallCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The download stalled for more than {_stallTimeout.TotalSeconds:0} seconds.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: the partial file is ignored by the app and overwritten by the next attempt.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort, as above.
        }
    }
}
