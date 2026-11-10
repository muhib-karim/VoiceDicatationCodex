using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using VoiceDictationCodex.Models;

namespace VoiceDictationCodex.Services;

public class JsonModelRepository : IModelRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _metadataPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonModelRepository()
        : this(AppPaths.MetadataFolder)
    {
    }

    public JsonModelRepository(string appDirectory)
    {
        Directory.CreateDirectory(appDirectory);
        _metadataPath = Path.Combine(appDirectory, "models.json");
    }

    public async Task<IReadOnlyList<WhisperModelInfo>> LoadAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WhisperModelInfo?> GetInstalledModelAsync(string id, CancellationToken cancellationToken = default)
    {
        var models = await LoadAvailableModelsAsync(cancellationToken).ConfigureAwait(false);
        return models.FirstOrDefault(model => model.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SaveModelMetadataAsync(WhisperModelInfo model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        // One writer at a time: the save is a read-modify-write of the whole file.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var models = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            var existingIndex = models.FindIndex(m => m.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                models[existingIndex] = model;
            }
            else
            {
                models.Add(model);
            }

            await WriteAtomicallyAsync(models, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<WhisperModelInfo>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_metadataPath))
        {
            return new List<WhisperModelInfo>();
        }

        List<WhisperModelInfo?>? models;
        try
        {
            var stream = File.OpenRead(_metadataPath);
            try
            {
                models = await JsonSerializer.DeserializeAsync<List<WhisperModelInfo?>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (JsonException)
        {
            // A truncated or hand-edited file must not take the model list down with it.
            QuarantineCorruptFile();
            return new List<WhisperModelInfo>();
        }

        var result = new List<WhisperModelInfo>();
        if (models is null)
        {
            return result;
        }

        foreach (var entry in models)
        {
            if (entry is not null && !string.IsNullOrWhiteSpace(entry.Id))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private async Task WriteAtomicallyAsync(List<WhisperModelInfo> models, CancellationToken cancellationToken)
    {
        // Write the new content next to the file and swap it in, so a crash never leaves truncated JSON.
        var tempPath = _metadataPath + ".tmp";
        try
        {
            var stream = File.Create(tempPath);
            try
            {
                await JsonSerializer.SerializeAsync(stream, models, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }

            File.Move(tempPath, _metadataPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private void QuarantineCorruptFile()
    {
        try
        {
            File.Move(_metadataPath, _metadataPath + ".corrupt", overwrite: true);
        }
        catch (IOException)
        {
            // Best effort: the next save replaces the file anyway.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort, as above.
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
            // Best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
