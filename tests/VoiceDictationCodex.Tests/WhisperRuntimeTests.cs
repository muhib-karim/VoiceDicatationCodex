using VoiceDictationCodex.Services;
using Xunit;

namespace VoiceDictationCodex.Tests;

public class WhisperRuntimeTests
{
    [Theory]
    [InlineData(@"C:\My Models\ggml-base.en.bin")]
    [InlineData(@"C:\models with space\trailing\")]
    [InlineData("C:\\odd \"quoted\" name.bin")]
    public void BuildArgumentList_PassesTheModelPathAsOneUnmodifiedArgument(string modelPath)
    {
        var args = WhisperRuntime.BuildArgumentList(modelPath);

        Assert.Equal(new[] { "--model", modelPath, "--output-json" }, args);
    }

    [Fact]
    public void CreateStartInfo_RedirectsBothPipesAsUtf8AndUsesAnArgumentList()
    {
        var startInfo = WhisperRuntime.CreateStartInfo(@"C:\My Models\ggml-base.en.bin");

        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(System.Text.Encoding.UTF8, startInfo.StandardOutputEncoding);
        Assert.Equal(System.Text.Encoding.UTF8, startInfo.StandardErrorEncoding);
        Assert.Equal(string.Empty, startInfo.Arguments);
        Assert.Equal(new[] { "--model", @"C:\My Models\ggml-base.en.bin", "--output-json" }, startInfo.ArgumentList);
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
    public void DefaultExecutable_IsTheWhisperCppCommandLineBinary()
    {
        Assert.Equal("whisper-cli.exe", WhisperRuntime.DefaultExecutable);
    }

    [Fact]
    public void NewRuntime_IsNotRunning_AndStopIsANoOp()
    {
        using var runtime = new WhisperRuntime();

        runtime.Stop();

        Assert.False(runtime.IsRunning);
    }
}
