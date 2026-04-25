using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace industREAL.CAN.CANGW.Utils
{
    public enum LogLevel { Trace, Debug, Info, Warn, Error, Fatal }

    public interface ILogSink
    {
        bool IsEnabled(LogLevel level);
        void Log(LogLevel level, string message, System.Exception? ex = null, params object[] args);
    }

    /// <summary>Static facade used by your DLL code.</summary>
    public static class Log
    {
        private sealed class NullSink : ILogSink
        {
            public bool IsEnabled(LogLevel level) => false;
            public void Log(LogLevel level, string message, System.Exception? ex = null, params object[] args) { }
        }

        private static ILogSink _sink = new NullSink();

        /// <summary>App sets this once at startup to plug in its logger.</summary>
        public static ILogSink Sink
        {
            get => Volatile.Read(ref _sink);
            set => Volatile.Write(ref _sink, value ?? new NullSink());
        }

        // Convenience methods your DLL will call:
        public static void Trace(string msg, params object[] args) => Sink.Log(LogLevel.Trace, msg, null, args);
        public static void Debug(string msg, params object[] args) => Sink.Log(LogLevel.Debug, msg, null, args);
        public static void Info(string msg, params object[] args) => Sink.Log(LogLevel.Info, msg, null, args);
        public static void Warn(string msg, params object[] args) => Sink.Log(LogLevel.Warn, msg, null, args);
        public static void Error(string msg, params object[] args) => Sink.Log(LogLevel.Error, msg, null, args);
        public static void Error(System.Exception ex, string msg, params object[] args) => Sink.Log(LogLevel.Error, msg, ex, args);
        public static void Fatal(string msg, params object[] args) => Sink.Log(LogLevel.Fatal, msg, null, args);
        public static void Fatal(System.Exception ex, string msg, params object[] args) => Sink.Log(LogLevel.Fatal, msg, ex, args);
    }
}

