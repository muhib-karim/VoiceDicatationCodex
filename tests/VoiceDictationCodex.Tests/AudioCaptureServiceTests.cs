using NAudio.Wave;
using VoiceDictationCodex.Services;
using Xunit;

namespace VoiceDictationCodex.Tests;

public class AudioCaptureServiceTests
{
    private readonly List<FakeWaveIn> _devices = new();

    private AudioCaptureService Service(int maxBufferedBytes = 1000, int stopTimeoutMilliseconds = 5000)
        => new(
            _ =>
            {
                var device = new FakeWaveIn();
                _devices.Add(device);
                return device;
            },
            maxBufferedBytes,
            TimeSpan.FromMilliseconds(stopTimeoutMilliseconds));

    [Fact]
    public async Task StopAsync_ReturnsExactlyTheCapturedBytes()
    {
        using var service = Service();
        service.Start();
        _devices[0].Deliver(new byte[] { 1, 2, 3 });
        _devices[0].Deliver(new byte[] { 4, 5 });

        var data = await service.StopAsync();

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, data);
        Assert.True(_devices[0].Disposed);
        Assert.False(service.IsRecording);
    }

    [Fact]
    public async Task AudioDeliveredBetweenStopRequestAndStoppedEvent_IsKept()
    {
        using var service = Service();
        service.Start();
        _devices[0].Deliver(new byte[] { 1 });
        _devices[0].TailOnStop = new byte[] { 2, 3 };

        var data = await service.StopAsync();

        Assert.Equal(new byte[] { 1, 2, 3 }, data);
    }

    [Fact]
    public async Task StopAsync_DoesNotHang_WhenTheDeviceNeverReportsStopped()
    {
        using var service = Service(stopTimeoutMilliseconds: 100);
        service.Start();
        _devices[0].RaiseStoppedOnStop = false;
        _devices[0].Deliver(new byte[] { 9 });

        var data = await service.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(new byte[] { 9 }, data);
        Assert.True(_devices[0].Disposed);
    }

    [Fact]
    public void Start_Twice_ThrowsAndKeepsTheFirstDevice()
    {
        using var service = Service();
        service.Start();

        Assert.Throws<InvalidOperationException>(() => service.Start());

        Assert.Single(_devices);
        Assert.False(_devices[0].Disposed);
    }

    [Fact]
    public async Task BufferIsCapped_AndTheLimitIsReportedOnce()
    {
        using var service = Service(maxBufferedBytes: 10);
        var limitEvents = 0;
        service.BufferLimitReached += (_, _) => limitEvents++;
        service.Start();
        _devices[0].Deliver(new byte[8]);
        _devices[0].Deliver(new byte[8]);
        _devices[0].Deliver(new byte[8]);

        var data = await service.StopAsync();

        Assert.Equal(10, data.Length);
        Assert.Equal(1, limitEvents);
    }

    [Fact]
    public void StartFailure_ReleasesTheDevice_SoALaterStartWorks()
    {
        var failNext = true;
        using var service = new AudioCaptureService(
            _ =>
            {
                var device = new FakeWaveIn { StartFailure = failNext ? new InvalidOperationException("no microphone") : null };
                _devices.Add(device);
                return device;
            },
            1000,
            TimeSpan.FromSeconds(5));

        Assert.Throws<InvalidOperationException>(() => service.Start());
        Assert.True(_devices[0].Disposed);
        Assert.False(service.IsRecording);

        failNext = false;
        service.Start();

        Assert.True(service.IsRecording);
        Assert.True(_devices[1].Started);
    }

    [Fact]
    public async Task StopAsync_WithoutStart_ReturnsNoData()
    {
        using var service = Service();

        Assert.Empty(await service.StopAsync());
    }

    [Fact]
    public async Task AudioChunkAvailable_ReceivesACopyOfEachChunk()
    {
        using var service = Service();
        var chunks = new List<byte[]>();
        service.AudioChunkAvailable += (_, chunk) => chunks.Add(chunk);
        service.Start();
        var source = new byte[] { 7, 8 };

        _devices[0].Deliver(source);
        await service.StopAsync();

        var chunk = Assert.Single(chunks);
        Assert.Equal(source, chunk);
        Assert.NotSame(source, chunk);
    }

    [Fact]
    public void Dispose_ReleasesARunningDevice()
    {
        var service = Service();
        service.Start();

        service.Dispose();

        Assert.True(_devices[0].Disposed);
        Assert.False(service.IsRecording);
    }
}

internal sealed class FakeWaveIn : IWaveIn
{
    public event EventHandler<WaveInEventArgs>? DataAvailable;

    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public WaveFormat WaveFormat { get; set; } = new WaveFormat(16000, 1);

    public bool Started { get; private set; }

    public bool Disposed { get; private set; }

    public bool RaiseStoppedOnStop { get; set; } = true;

    public byte[]? TailOnStop { get; set; }

    public Exception? StartFailure { get; set; }

    public void StartRecording()
    {
        if (StartFailure is not null)
        {
            throw StartFailure;
        }

        Started = true;
    }

    public void StopRecording()
    {
        if (TailOnStop is not null)
        {
            Deliver(TailOnStop);
        }

        if (RaiseStoppedOnStop)
        {
            RecordingStopped?.Invoke(this, new StoppedEventArgs());
        }
    }

    public void Deliver(byte[] data) => DataAvailable?.Invoke(this, new WaveInEventArgs(data, data.Length));

    public void Dispose() => Disposed = true;
}
