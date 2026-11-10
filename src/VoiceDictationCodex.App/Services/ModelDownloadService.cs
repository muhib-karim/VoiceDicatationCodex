using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using VoiceDictationCodex.Models;

namespace VoiceDictationCodex.Services;

public class ModelDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly IModelRepository _modelRepository;

    public ModelDownloadService(HttpClient httpClient, IModelRepository modelRepository)
    {
        _httpClient = httpClient;
        _modelRepository = modelRepository;
    }

    public async Task<WhisperModelInfo> DownloadAsync(WhisperModelInfo model, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var modelsFolder = GetModelsFolder();
        Directory.CreateDirectory(modelsFolder);

        var destinationFile = Path.Combine(modelsFolder, $"{model.Id}.bin");

        using var response = await _httpClient.GetAsync(GetDownloadUrl(model.Id), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(destinationFile);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalRead += read;
            if (totalBytes > 0)
            {
                progress?.Report((double)totalRead / totalBytes);
            }
        }

        var updatedModel = model with { LocalPath = destinationFile, IsInstalled = true };
        await _modelRepository.SaveModelMetadataAsync(updatedModel, cancellationToken);
        return updatedModel;
    }

    private static string GetModelsFolder()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documents, "VoiceDictationCodex", "Models");
    }

    private static Uri GetDownloadUrl(string modelId)
    {
        // Placeholder endpoint - replace with official Whisper.ggml mirrors or Azure Blob etc.
        return new Uri($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{modelId}.bin");
    }
}
