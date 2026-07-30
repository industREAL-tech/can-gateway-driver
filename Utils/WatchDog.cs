using System;
using System.Timers;
namespace industREAL.CAN.CANGW.Utils;

public sealed class Watchdog : IDisposable
{
    private readonly object _sync = new object();
    private readonly System.Timers.Timer _timer;
    private bool _enabled;

    public event Action? TimedOut;

    /// <param name="timeout">Timeout interval</param>
    /// <param name="repeatUntilReset">If true, keep firing every timeout until Reset() is called</param>
    public Watchdog(TimeSpan timeout, bool repeatUntilReset = false)
    {
        _timer = new System.Timers.Timer(timeout.TotalMilliseconds);
        _timer.AutoReset = repeatUntilReset;       // one-shot by default
        _timer.Elapsed += OnElapsed;
    }

    public void Start()
    {
        lock (_sync)
        {
            _enabled = true;
            _timer.Stop();
            _timer.Start();
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            if (!_enabled) return;
            _timer.Stop();
            _timer.Start();
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _enabled = false;
            _timer.Stop();
        }
    }

    private void OnElapsed(object? sender, ElapsedEventArgs e)
    {
        Action? h = null;
        lock (_sync)
        {
            if (!_enabled) return;
            if (!_timer.AutoReset) _enabled = false;  // one-shot: arm must be restarted
            h = TimedOut;
        }
        if (h != null) h();
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}
