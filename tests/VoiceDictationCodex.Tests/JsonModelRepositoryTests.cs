using System.IO;
using VoiceDictationCodex.Models;
using VoiceDictationCodex.Services;
using Xunit;

namespace VoiceDictationCodex.Tests;

public sealed class JsonModelRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vdc-tests-" + Guid.NewGuid().ToString("N"));

    private string MetadataPath => Path.Combine(_directory, "models.json");

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

    [Theory]
    [InlineData("")]
    [InlineData("[{\"Id\":\"ggml-small\",\"Name\":")]
    [InlineData("{\"not\":\"a list\"}")]
    [InlineData("not json at all")]
    public async Task Load_ReturnsEmptyAndQuarantinesTheFile_WhenItIsCorrupt(string content)
    {
        var repository = new JsonModelRepository(_directory);
        await File.WriteAllTextAsync(MetadataPath, content);

        var models = await repository.LoadAvailableModelsAsync();

        Assert.Empty(models);
        Assert.False(File.Exists(MetadataPath));
        Assert.True(File.Exists(MetadataPath + ".corrupt"));

        await repository.SaveModelMetadataAsync(Sample("ggml-small"));
        Assert.Single(await repository.LoadAvailableModelsAsync());
    }

    [Fact]
    public async Task Load_DropsNullEntriesAndEntriesWithoutAnId()
    {
        var repository = new JsonModelRepository(_directory);
        await File.WriteAllTextAsync(MetadataPath, "[null, {\"Name\":\"no id\"}, {\"Id\":\"ggml-small\",\"Name\":\"ok\",\"IsInstalled\":true}]");

        var models = await repository.LoadAvailableModelsAsync();

        var model = Assert.Single(models);
        Assert.Equal("ggml-small", model.Id);
        Assert.Null(await repository.GetInstalledModelAsync("missing"));
    }

    [Fact]
    public async Task Load_AcceptsMetadataWrittenBeforeIntegrityFieldsExisted()
    {
        var repository = new JsonModelRepository(_directory);
        await File.WriteAllTextAsync(MetadataPath, "[{\"Id\":\"ggml-base.en\",\"Name\":\"Whisper Base (English)\",\"Description\":\"d\",\"DownloadSize\":140,\"LocalPath\":\"C:\\\\models\\\\ggml-base.en.bin\",\"LanguageSupport\":\"English\",\"IsInstalled\":true}]");

        var model = Assert.Single(await repository.LoadAvailableModelsAsync());

        Assert.True(model.IsInstalled);
        Assert.Equal(@"C:\models\ggml-base.en.bin", model.LocalPath);
        Assert.Equal(0, model.SizeBytes);
        Assert.Null(model.Sha256);
    }

    [Fact]
    public async Task ConcurrentSaves_AllPersistAndLeaveNoTempFile()
    {
        var repository = new JsonModelRepository(_directory);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => repository.SaveModelMetadataAsync(Sample("ggml-" + i))));

        var models = await new JsonModelRepository(_directory).LoadAvailableModelsAsync();
        Assert.Equal(20, models.Count);
        Assert.False(File.Exists(MetadataPath + ".tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
