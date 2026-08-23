// NTP-style clock sync.
//
// On every TimeReq response the client records (T1, T2, T3, T4) and
// computes:
//
//   offset = ((T2 - T1) + (T3 - T4)) / 2
//   delay  = ((T4 - T1) - (T3 - T2)) / 2
//
// Outliers (delay > 3 × median) are rejected. The accepted offsets are
// fed into an EWMA so the device→host conversion is smooth across
// transport jitter. The skew estimate is updated by linear regression
// over the most recent samples so a steady drift is corrected without
// needing high sample rate.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace industREAL.CAN.CANGW.V2
{
    public sealed class ClockSync
    {
        private readonly object _lock = new();
        private readonly List<(long deviceUs, long hostUs, long delayUs)> _samples = new();
        private const int MaxSamples = 32;

        // host_us(device_us) = device_us * skew + offset
        private double _skew = 1.0;
        private double _offsetUs;
        private bool   _hasFix;

        public bool   HasFix    { get { lock (_lock) return _hasFix; } }
        public double SkewPpm   { get { lock (_lock) return (_skew - 1.0) * 1_000_000.0; } }
        public long   OffsetUs  { get { lock (_lock) return (long)_offsetUs; } }
        public long   LastDelayUs
        {
            get { lock (_lock) return _samples.Count > 0 ? _samples[^1].delayUs : 0; }
        }

        public DateTime DeviceToHost(long deviceUs)
        {
            lock (_lock)
            {
                if (!_hasFix) return DateTime.UtcNow;
                double hostUs = deviceUs * _skew + _offsetUs;
                long ticks = (long)(hostUs * 10.0); // 1 tick = 100 ns
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        public void Submit(long t1Us, long t2Us, long t3Us, long t4Us)
        {
            // Offset and delay in client (host) microsecond domain.
            long offset = ((t2Us - t1Us) + (t3Us - t4Us)) / 2;
            long delay  = ((t4Us - t1Us) - (t3Us - t2Us)) / 2;
            if (delay < 0) return;

            // Convert offset into a (device_us, host_us) sample pair anchored
            // at the midpoint of the exchange.
            long deviceMid = (t2Us + t3Us) / 2;
            long hostMid   = (t1Us + t4Us) / 2;

            lock (_lock)
            {
                _samples.Add((deviceMid, hostMid, delay));
                if (_samples.Count > MaxSamples) _samples.RemoveAt(0);

                // Reject this sample if it is far slower than median.
                if (_samples.Count >= 4)
                {
                    var delays = new long[_samples.Count];
                    for (int i = 0; i < _samples.Count; i++) delays[i] = _samples[i].delayUs;
                    Array.Sort(delays);
                    long median = delays[delays.Length / 2];
                    if (delay > 3 * Math.Max(median, 1)) { _samples.RemoveAt(_samples.Count - 1); return; }
                }

                Refit();
            }
        }

        // Linear regression on (deviceUs, hostUs).
        private void Refit()
        {
            int n = _samples.Count;
            if (n == 0) return;
            if (n == 1)
            {
                _skew = 1.0;
                _offsetUs = _samples[0].hostUs - (double)_samples[0].deviceUs;
                _hasFix = true;
                return;
            }

            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            foreach (var s in _samples)
            {
                sx  += s.deviceUs;
                sy  += s.hostUs;
                sxx += (double)s.deviceUs * s.deviceUs;
                sxy += (double)s.deviceUs * s.hostUs;
            }
            double denom = n * sxx - sx * sx;
            if (Math.Abs(denom) < 1e-9)
            {
                _offsetUs = sy / n - sx / n;
                _skew = 1.0;
            }
            else
            {
                _skew     = (n * sxy - sx * sy) / denom;
                _offsetUs = (sy - _skew * sx) / n;
            }
            _hasFix = true;
        }
    }
}
