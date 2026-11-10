using VoiceDictationCodex.Services;
using Xunit;

namespace VoiceDictationCodex.Tests;

public class WhisperRuntimeProcessTests
{
    [Fact]
    public async Task LargeStandardErrorOutput_DoesNotBlockTheProcess()
    {
        // About 2 MB on stderr before the only stdout line. An undrained stderr pipe would hang the child.
        using var runtime = TestProcess.Runtime(
            "$e = [Console]::Error; $l = 'e' * 100; for ($i = 0; $i -lt 20000; $i++) { $e.WriteLine($l) }; [Console]::Out.WriteLine('done')",
            "i=0; while [ $i -lt 20000 ]; do echo eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee 1>&2; i=$((i+1)); done; echo done");
        var lines = new List<string>();
        runtime.TranscriptionReceived += (_, line) =>
        {
            lock (lines)
            {
                lines.Add(line);
            }
        };

        var exit = await runtime.StartAsync("unused.bin").WaitAsync(TestProcess.Timeout);

        Assert.Equal(0, exit.ExitCode);
        Assert.Contains("done", lines);
        Assert.False(exit.StoppedByUser);
        Assert.False(runtime.IsRunning);
    }

    [Fact]
    public async Task ProcessExit_ReportsExitCodeAndStandardErrorTail()
    {
        using var runtime = TestProcess.Runtime(
            "[Console]::Error.WriteLine('boom'); exit 3",
            "echo boom 1>&2; exit 3");

        var exit = await runtime.StartAsync("unused.bin").WaitAsync(TestProcess.Timeout);

        Assert.Equal(3, exit.ExitCode);
        Assert.Contains("boom", exit.StandardErrorTail);
        Assert.False(exit.StoppedByUser);
    }

    [Fact]
    public async Task Stop_EndsARunningProcessWithoutThrowing()
    {
        using var runtime = TestProcess.Runtime(
            "[Console]::Out.WriteLine('ready'); Start-Sleep -Seconds 120",
            "echo ready; sleep 120");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.TranscriptionReceived += (_, line) =>
        {
            if (line == "ready")
            {
                ready.TrySetResult();
            }
        };

        var run = runtime.StartAsync("unused.bin");
        await ready.Task.WaitAsync(TestProcess.Timeout);
        Assert.True(runtime.IsRunning);

        runtime.Stop();
        var exit = await run.WaitAsync(TestProcess.Timeout);

        Assert.True(exit.StoppedByUser);
        Assert.False(runtime.IsRunning);
        runtime.Stop();
    }

    [Fact]
    public async Task Cancellation_EndsARunningProcess()
    {
        using var runtime = TestProcess.Runtime(
            "[Console]::Out.WriteLine('ready'); Start-Sleep -Seconds 120",
            "echo ready; sleep 120");
        using var cancellation = new CancellationTokenSource();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.TranscriptionReceived += (_, line) =>
        {
            if (line == "ready")
            {
                ready.TrySetResult();
            }
        };

        var run = runtime.StartAsync("unused.bin", cancellation.Token);
        await ready.Task.WaitAsync(TestProcess.Timeout);
        cancellation.Cancel();
        var exit = await run.WaitAsync(TestProcess.Timeout);

        Assert.True(exit.StoppedByUser);
    }

    [Fact]
    public async Task StandardOutput_IsDecodedAsUtf8()
    {
        using var runtime = TestProcess.Runtime(
            "$b = [Text.Encoding]::UTF8.GetBytes('h' + [char]0xE9 + 'llo' + [char]10); $o = [Console]::OpenStandardOutput(); $o.Write($b, 0, $b.Length); $o.Flush()",
            "printf 'h\\303\\251llo\\n'");
        var lines = new List<string>();
        runtime.TranscriptionReceived += (_, line) =>
        {
            lock (lines)
            {
                lines.Add(line);
            }
        };

        await runtime.StartAsync("unused.bin").WaitAsync(TestProcess.Timeout);

        Assert.Contains("h\u00e9llo", lines);
    }

    [Fact]
    public async Task ThrowingSubscriber_DoesNotEndTheSession()
    {
        using var runtime = TestProcess.Runtime(
            "[Console]::Out.WriteLine('one'); [Console]::Out.WriteLine('two')",
            "echo one; echo two");
        var lines = new List<string>();
        runtime.TranscriptionReceived += (_, line) =>
        {
            lock (lines)
            {
                lines.Add(line);
            }

            throw new InvalidOperationException("subscriber failure");
        };

        var exit = await runtime.StartAsync("unused.bin").WaitAsync(TestProcess.Timeout);

        Assert.Equal(0, exit.ExitCode);
        Assert.Equal(new[] { "one", "two" }, lines);
    }

    [Fact]
    public async Task StartAsync_FaultsTheTask_WhenTheExecutableIsMissing()
    {
        using var runtime = new WhisperRuntime(_ => WhisperRuntime.CreateStartInfo("vdc-missing-executable-for-tests", Array.Empty<string>()));

        await Assert.ThrowsAnyAsync<Exception>(() => runtime.StartAsync("unused.bin"));

        Assert.False(runtime.IsRunning);
    }
}
