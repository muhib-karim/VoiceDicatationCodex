using System.IO;
using VoiceDictationCodex.Models;
using VoiceDictationCodex.Services;
using Xunit;

namespace VoiceDictationCodex.Tests;

public sealed class JsonModelRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vdc-tests-" + Guid.NewGuid().ToString("N"));

    private static WhisperModelInfo Sample(string id, bool installed = true)
        => new(id, "Name " + id, "Description", 140, $@"C:\models\{id}.bin", "English", installed);

    [Fact]
    public async Task LoadAvailableModels_ReturnsEmpty_WhenNoMetadataFile()
    {
        var repository = new JsonModelRepository(_directory);

        var models = await repository.LoadAvailableModelsAsync();

        Assert.Empty(models);
    }

    [Fact]
    public async Task SaveModelMetadata_RoundTripsThroughJson()
    {
        var repository = new JsonModelRepository(_directory);
        var model = Sample("ggml-base.en");

        await repository.SaveModelMetadataAsync(model);
        var loaded = await new JsonModelRepository(_directory).GetInstalledModelAsync("GGML-BASE.EN");

        Assert.Equal(model, loaded);
        Assert.True(File.Exists(Path.Combine(_directory, "models.json")));
    }

    [Fact]
    public async Task SaveModelMetadata_ReplacesExistingEntryWithSameId()
    {
        var repository = new JsonModelRepository(_directory);
        await repository.SaveModelMetadataAsync(Sample("ggml-small", installed: false));
        await repository.SaveModelMetadataAsync(Sample("ggml-medium"));
        await repository.SaveModelMetadataAsync(Sample("ggml-small", installed: true));

        var models = await repository.LoadAvailableModelsAsync();

        Assert.Equal(2, models.Count);
        Assert.True(models.Single(m => m.Id == "ggml-small").IsInstalled);
    }

    [Fact]
    public async Task GetInstalledModel_ReturnsNull_ForUnknownId()
    {
        var repository = new JsonModelRepository(_directory);
        await repository.SaveModelMetadataAsync(Sample("ggml-small"));

        Assert.Null(await repository.GetInstalledModelAsync("ggml-large"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
