using System.Diagnostics;

namespace Capta.Recording;

/// <summary>
/// Tracks the media timeline independently from wall-clock time. Pausing freezes elapsed media
/// time; stopping is terminal and safe to call more than once.
/// </summary>
internal sealed class RecordingTimeline
{
    private readonly object _gate = new();
    private readonly Stopwatch _clock = new();
    private bool _started;
    private bool _paused;
    private bool _stopped;

    public TimeSpan Elapsed
    {
        get { lock (_gate) return _clock.Elapsed; }
    }

    public bool IsPaused
    {
        get { lock (_gate) return _paused; }
    }

    public bool IsStopped
    {
        get { lock (_gate) return _stopped; }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _stopped)
                throw new InvalidOperationException("A recording timeline can only be started once.");
            _started = true;
            _clock.Start();
        }
    }

    public bool Pause()
    {
        lock (_gate)
        {
            if (!_started || _stopped || _paused) return false;
            _clock.Stop();
            _paused = true;
            return true;
        }
    }

    public bool Resume()
    {
        lock (_gate)
        {
            if (!_started || _stopped || !_paused) return false;
            _paused = false;
            _clock.Start();
            return true;
        }
    }

    public TimeSpan Stop()
    {
        lock (_gate)
        {
            if (!_stopped)
            {
                _clock.Stop();
                _paused = false;
                _stopped = true;
            }
            return _clock.Elapsed;
        }
    }
}
