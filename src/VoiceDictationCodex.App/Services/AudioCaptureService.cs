using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace VoiceDictationCodex.Services;

public interface IAudioCapture : IDisposable
{
    /// <summary>Raised once per recording when the in-memory buffer is full.</summary>
    event EventHandler? BufferLimitReached;

    void Start(int deviceNumber = 0);

    /// <summary>Stops the device and returns the captured PCM. Does not block the calling thread.</summary>
    Task<byte[]> StopAsync();
}

public class AudioCaptureService : IAudioCapture
{
    /// <summary>Thirty minutes of 16 kHz mono 16-bit PCM.</summary>
    public const int DefaultMaxBufferedBytes = 16000 * 2 * 60 * 30;

    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(2);

    private readonly Func<int, IWaveIn> _deviceFactory;
    private readonly int _maxBufferedBytes;
    private readonly TimeSpan _stopTimeout;
    private readonly object _gate = new();

    private IWaveIn? _waveIn;
    private MemoryStream? _buffer;
    private TaskCompletionSource<bool>? _stopped;
    private bool _limitReported;

    public AudioCaptureService()
        : this(CreateDevice, DefaultMaxBufferedBytes, DefaultStopTimeout)
    {
    }

    internal AudioCaptureService(Func<int, IWaveIn> deviceFactory, int maxBufferedBytes, TimeSpan stopTimeout)
    {
        _deviceFactory = deviceFactory;
        _maxBufferedBytes = maxBufferedBytes;
        _stopTimeout = stopTimeout;
    }

    public event EventHandler<byte[]>? AudioChunkAvailable;

    public event EventHandler? BufferLimitReached;

    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _waveIn is not null;
            }
        }
    }

    public void Start(int deviceNumber = 0)
    {
        IWaveIn waveIn;
        lock (_gate)
        {
            if (_waveIn is not null)
            {
                throw new InvalidOperationException("Audio capture is already running.");
            }

            waveIn = _deviceFactory(deviceNumber);
            _waveIn = waveIn;
            _buffer = new MemoryStream();
            _stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _limitReported = false;
        }

        waveIn.DataAvailable += OnDataAvailable;
        waveIn.RecordingStopped += OnRecordingStopped;
        try
        {
            waveIn.StartRecording();
        }
        catch
        {
            // The device could not be opened: release it so that a later Start can succeed.
            Release(waveIn);
            throw;
        }
    }

    public async Task<byte[]> StopAsync()
    {
        IWaveIn? waveIn;
        TaskCompletionSource<bool>? stopped;
        lock (_gate)
        {
            waveIn = _waveIn;
            stopped = _stopped;
        }

        if (waveIn is null || stopped is null)
        {
            return Array.Empty<byte>();
        }

        try
        {
            waveIn.StopRecording();

            // The device delivers its last buffers and then raises RecordingStopped. WaveInEvent posts that
            // event to the thread that created it, so this must be awaited rather than blocked on.
            await Task.WhenAny(stopped.Task, Task.Delay(_stopTimeout)).ConfigureAwait(false);
        }
        catch
        {
            Release(waveIn);
            throw;
        }

        return Release(waveIn);
    }

    public void Dispose()
    {
        IWaveIn? waveIn;
        lock (_gate)
        {
            waveIn = _waveIn;
        }

        if (waveIn is not null)
        {
            Release(waveIn);
        }

        GC.SuppressFinalize(this);
    }

    private static IWaveIn CreateDevice(int deviceNumber)
    {
        return new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(16000, 1)
        };
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        var limitReached = false;
        lock (_gate)
        {
            if (_buffer is null)
            {
                return;
            }

            var room = _maxBufferedBytes - (int)_buffer.Length;
            var count = Math.Min(room, args.BytesRecorded);
            if (count > 0)
            {
                _buffer.Write(args.Buffer, 0, count);
            }

            if (count < args.BytesRecorded && !_limitReported)
            {
                _limitReported = true;
                limitReached = true;
            }
        }

        var chunkHandler = AudioChunkAvailable;
        if (chunkHandler is not null && args.BytesRecorded > 0)
        {
            chunkHandler(this, args.Buffer.AsSpan(0, args.BytesRecorded).ToArray());
        }

        if (limitReached)
        {
            BufferLimitReached?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        TaskCompletionSource<bool>? stopped;
        lock (_gate)
        {
            stopped = _stopped;
        }

        stopped?.TrySetResult(true);
    }

    /// <summary>Detaches and disposes the device and returns what was captured. Safe to call more than once.</summary>
    private byte[] Release(IWaveIn waveIn)
    {
        byte[] data;
        lock (_gate)
        {
            if (!ReferenceEquals(_waveIn, waveIn))
            {
                return Array.Empty<byte>();
            }

            data = _buffer?.ToArray() ?? Array.Empty<byte>();
            _buffer?.Dispose();
            _buffer = null;
            _waveIn = null;
            _stopped = null;
        }

        waveIn.DataAvailable -= OnDataAvailable;
        waveIn.RecordingStopped -= OnRecordingStopped;
        waveIn.Dispose();
        return data;
    }
}
