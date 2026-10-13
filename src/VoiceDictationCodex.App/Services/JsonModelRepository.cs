using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using VoiceDictationCodex.Models;

namespace VoiceDictationCodex.Services;

public class JsonModelRepository : IModelRepository
{
    private readonly string _metadataPath;

    public JsonModelRepository()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoiceDictationCodex"))
    {
    }

    public JsonModelRepository(string appDirectory)
    {
        Directory.CreateDirectory(appDirectory);
        _metadataPath = Path.Combine(appDirectory, "models.json");
    }

    public async Task<IReadOnlyList<WhisperModelInfo>> LoadAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_metadataPath))
        {
            return Array.Empty<WhisperModelInfo>();
        }

        await using var stream = File.OpenRead(_metadataPath);
        var models = await JsonSerializer.DeserializeAsync<List<WhisperModelInfo>>(stream, cancellationToken: cancellationToken);
        if (models is null)
        {
            return Array.Empty<WhisperModelInfo>();
        }

        return models;
    }

    public async Task<WhisperModelInfo?> GetInstalledModelAsync(string id, CancellationToken cancellationToken = default)
    {
        var models = await LoadAvailableModelsAsync(cancellationToken);
        return models.FirstOrDefault(model => model.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SaveModelMetadataAsync(WhisperModelInfo model, CancellationToken cancellationToken = default)
    {
        var models = (await LoadAvailableModelsAsync(cancellationToken)).ToList();
        var existingIndex = models.FindIndex(m => m.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            models[existingIndex] = model;
        }
        else
        {
            models.Add(model);
        }

        await using var stream = File.Create(_metadataPath);
        await JsonSerializer.SerializeAsync(stream, models, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
    }
}