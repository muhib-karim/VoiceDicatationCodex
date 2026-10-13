using VoiceDictationCodex.Services;
using Xunit;

namespace VoiceDictationCodex.Tests;

public class WhisperRuntimeTests
{
    [Fact]
    public void BuildArguments_QuotesModelPath()
    {
        var args = WhisperRuntime.BuildArguments(@"C:\My Models\ggml-base.en.bin");

        Assert.Equal("--model \"C:\\My Models\\ggml-base.en.bin\" --output-json", args);
    }

    [Fact]
    public void ResolveExecutablePath_UsesEnvironmentOverrideOrDefault()
    {
        var original = Environment.GetEnvironmentVariable(WhisperRuntime.ExecutablePathVariable);
        try
        {
            Environment.SetEnvironmentVariable(WhisperRuntime.ExecutablePathVariable, null);
            Assert.Equal(WhisperRuntime.DefaultExecutable, WhisperRuntime.ResolveExecutablePath());

            Environment.SetEnvironmentVariable(WhisperRuntime.ExecutablePathVariable, "  D:\\tools\\whisper-cli.exe  ");
            Assert.Equal("D:\\tools\\whisper-cli.exe", WhisperRuntime.ResolveExecutablePath());
        }
        finally
        {
            Environment.SetEnvironmentVariable(WhisperRuntime.ExecutablePathVariable, original);
        }
    }

    [Fact]
    public void NewRuntime_IsNotRunning()
    {
        Assert.False(new WhisperRuntime().IsRunning);
    }
}
