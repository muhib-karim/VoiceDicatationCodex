using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using VoiceDictationCodex.Models;
using VoiceDictationCodex.Services;
using Xunit;

namespace VoiceDictationCodex.Tests;

public sealed class ModelDownloadServiceTests : IDisposable
{
    private static readonly byte[] Payload = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vdc-download-tests-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingModelRepository _repository = new();

    private string FinalPath => Path.Combine(_folder, "ggml-test.bin");

    private static WhisperModelInfo Model(string id = "ggml-test")
        => new(id, "Test", "Test model", 1, string.Empty, "English", false, Payload.Length, Convert.ToHexString(SHA256.HashData(Payload)));

    private static HttpResponseMessage Ok(byte[] content) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    private static HttpResponseMessage Ok(Stream content) => new(HttpStatusCode.OK) { Content = new StreamContent(content) };

    private ModelDownloadService Service(Func<HttpRequestMessage, HttpResponseMessage> respond, TimeSpan? stallTimeout = null)
        => new(new HttpClient(new StubHandler(respond)), _repository, _folder, stallTimeout ?? TimeSpan.FromSeconds(30));

    private void AssertNothingLeftBehind()
    {
        Assert.False(File.Exists(FinalPath));
        Assert.False(File.Exists(FinalPath + ".partial"));
        Assert.Empty(_repository.Saved);
    }

    [Fact]
    public async Task Download_VerifiesMovesIntoPlaceAndSavesMetadata()
    {
        Uri? requested = null;
        var service = Service(request =>
        {
            requested = request.RequestUri;
            return Ok(Payload);
        });
        var progress = new SynchronousProgress();

        var installed = await service.DownloadAsync(Model(), progress);

        Assert.True(installed.IsInstalled);
        Assert.Equal(FinalPath, installed.LocalPath);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(FinalPath));
        Assert.False(File.Exists(FinalPath + ".partial"));
        Assert.Equal(installed, Assert.Single(_repository.Saved));
        Assert.Equal(ModelDownloadService.GetDownloadUrl("ggml-test"), requested);
        Assert.Equal(1.0, progress.Values[^1]);
        Assert.InRange(progress.Values.Count, 1, 101);
    }

    [Fact]
    public async Task Download_ReplacesAnExistingFile()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(FinalPath, "stale");
        var service = Service(_ => Ok(Payload));

        await service.DownloadAsync(Model());

        Assert.Equal(Payload, await File.ReadAllBytesAsync(FinalPath));
    }

    [Fact]
    public async Task HashMismatch_ThrowsAndLeavesNothingBehind()
    {
        var corrupted = (byte[])Payload.Clone();
        corrupted[10] ^= 0xFF;
        var service = Service(_ => Ok(corrupted));

        await Assert.ThrowsAsync<ModelIntegrityException>(() => service.DownloadAsync(Model()));

        AssertNothingLeftBehind();
    }

    [Fact]
    public async Task TruncatedBody_ThrowsAndLeavesNothingBehind()
    {
        var service = Service(_ => Ok(Payload.Take(100).ToArray()));

        await Assert.ThrowsAsync<ModelIntegrityException>(() => service.DownloadAsync(Model()));

        AssertNothingLeftBehind();
    }

    [Fact]
    public async Task MidStreamFailure_ThrowsAndLeavesNothingBehind()
    {
        var stream = new ScriptedStream((call, _) => call == 0
            ? Task.FromResult(Payload.Take(100).ToArray())
            : Task.FromException<byte[]>(new IOException("connection reset")));
        var service = Service(_ => Ok(stream));

        await Assert.ThrowsAnyAsync<IOException>(() => service.DownloadAsync(Model()));

        AssertNothingLeftBehind();
    }

    [Fact]
    public async Task Cancellation_ThrowsAndLeavesNothingBehind()
    {
        using var cancellation = new CancellationTokenSource();
        var stream = new ScriptedStream(async (call, token) =>
        {
            if (call == 0)
            {
                return Payload.Take(100).ToArray();
            }

            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Array.Empty<byte>();
        });
        var service = Service(_ => Ok(stream));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(Model(), null, cancellation.Token));

        AssertNothingLeftBehind();
    }

    [Fact]
    public async Task StalledConnection_TimesOutAndLeavesNothingBehind()
    {
        var stream = new ScriptedStream(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Array.Empty<byte>();
        });
        var service = Service(_ => Ok(stream), TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<TimeoutException>(() => service.DownloadAsync(Model()));

        AssertNothingLeftBehind();
    }

    [Fact]
    public async Task HttpError_ThrowsAndLeavesNothingBehind()
    {
        var service = Service(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<HttpRequestException>(() => service.DownloadAsync(Model()));

        AssertNothingLeftBehind();
    }

    [Fact]
    public async Task MetadataFailure_IsReportedToTheCaller()
    {
        _repository.SaveFailure = new IOException("disk full");
        var service = Service(_ => Ok(Payload));

        await Assert.ThrowsAsync<IOException>(() => service.DownloadAsync(Model()));

        Assert.False(File.Exists(FinalPath + ".partial"));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("..")]
    [InlineData("sub/model")]
    [InlineData("sub\\model")]
    [InlineData("C:model")]
    [InlineData("")]
    public async Task InvalidModelId_IsRejectedBeforeAnyRequest(string id)
    {
        var requests = 0;
        var service = Service(_ =>
        {
            requests++;
            return Ok(Payload);
        });

        await Assert.ThrowsAsync<ArgumentException>(() => service.DownloadAsync(Model(id)));

        Assert.Equal(0, requests);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void DownloadUrl_IsPinnedToARevision()
    {
        var url = ModelDownloadService.GetDownloadUrl("ggml-base.en").ToString();

        Assert.Equal($"https://huggingface.co/ggerganov/whisper.cpp/resolve/{ModelDownloadService.ModelRevision}/ggml-base.en.bin", url);
        Assert.DoesNotContain("/resolve/main/", url);
        Assert.Equal(40, ModelDownloadService.ModelRevision.Length);
    }

    [Fact]
    public void ModelsFolder_IsBuiltUnderLocalAppData()
    {
        Assert.Equal(Path.Combine("base", "VoiceDictationCodex", "Models"), AppPaths.BuildModelsFolder("base"));
        Assert.Throws<InvalidOperationException>(() => AppPaths.BuildModelsFolder(" "));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
