using VoiceDictationCodex.Services;
using Xunit;

namespace VoiceDictationCodex.Tests;

public class TranscriptionServiceTests
{
    private static (TranscriptionService Service, List<string> Updates) Create()
    {
        var service = new TranscriptionService(new WhisperRuntime());
        var updates = new List<string>();
        service.TextUpdated += (_, text) => updates.Add(text);
        return (service, updates);
    }

    [Fact]
    public void JsonLines_AppendTextProperty()
    {
        var (service, updates) = Create();

        service.ProcessLine("{\"text\":\"Hello\"}");
        service.ProcessLine("{\"text\":\" world\"}");

        Assert.Equal(new[] { "Hello", "Hello world" }, updates);
    }

    [Fact]
    public void JsonWithoutTextOrBlankText_IsIgnored()
    {
        var (service, updates) = Create();

        service.ProcessLine("{\"progress\":42}");
        service.ProcessLine("{\"text\":\"   \"}");

        Assert.Empty(updates);
    }

    [Fact]
    public void NonJsonLines_FallBackToRawText()
    {
        var (service, updates) = Create();

        service.ProcessLine("plain output");
        service.ProcessLine("   ");

        var update = Assert.Single(updates);
        Assert.Equal("plain output" + Environment.NewLine, update);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"quoted\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[1, 2]")]
    public void ValidJsonThatIsNotAnObject_FallsBackToRawText(string line)
    {
        var (service, updates) = Create();

        service.ProcessLine(line);

        var update = Assert.Single(updates);
        Assert.Equal(line + Environment.NewLine, update);
    }

    [Theory]
    [InlineData("{\"text\":5}")]
    [InlineData("{\"text\":null}")]
    [InlineData("{\"text\":{\"nested\":true}}")]
    public void JsonObjectWithNonStringText_IsIgnoredWithoutThrowing(string line)
    {
        var (service, updates) = Create();

        service.ProcessLine(line);

        Assert.Empty(updates);
    }

    [Fact]
    public async Task StartAsync_ClearsTheTranscriptOfThePreviousSession()
    {
        using var runtime = TestProcess.Runtime("[Console]::Out.WriteLine('second')", "echo second");
        var service = new TranscriptionService(runtime);
        var updates = new List<string>();
        service.TextUpdated += (_, text) =>
        {
            lock (updates)
            {
                updates.Add(text);
            }
        };
        service.ProcessLine("first");

        await service.StartAsync("unused.bin").WaitAsync(TestProcess.Timeout);

        Assert.Equal("second" + Environment.NewLine, updates[^1]);
    }
}
