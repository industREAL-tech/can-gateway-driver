using System;
using System.Threading;

namespace industREAL.CAN.CANGW.Utils
{
    /// <summary>
    /// Severity levels. Kept for source compatibility; mirrors
    /// <see cref="industREAL.CAN.CORE.LogLevel"/> value for value.
    /// </summary>
    public enum LogLevel { Trace, Debug, Info, Warn, Error, Fatal }

    /// <summary>
    /// Sink interface. Kept for source compatibility with existing consumers of
    /// this package; an instance assigned to <see cref="Log.Sink"/> is adapted
    /// onto the shared CORE sink.
    /// </summary>
    public interface ILogSink
    {
        bool IsEnabled(LogLevel level);
        void Log(LogLevel level, string message, System.Exception? ex = null, params object[] args);
    }

    /// <summary>
    /// Logging facade for this package.
    /// </summary>
    /// <remarks>
    /// This used to be a second, wholly independent facade with its own private
    /// sink field, so an application that installed a sink on
    /// industREAL.CAN.CORE.Log - the one CanDispatcher and the device drivers
    /// write to - silently received nothing from this assembly, and vice versa.
    /// It now forwards to the CORE facade, so there is a single sink for the whole
    /// stack. Prefer using industREAL.CAN.CORE.Log directly in new code.
    /// </remarks>
    public static class Log
    {
        /// <summary>Adapts a CANGW sink onto the CORE sink interface.</summary>
        private sealed class SinkAdapter : industREAL.CAN.CORE.ILogSink
        {
            private readonly ILogSink _inner;
            public SinkAdapter(ILogSink inner) => _inner = inner;

            public bool IsEnabled(industREAL.CAN.CORE.LogLevel level)
                => _inner.IsEnabled((LogLevel)level);

            public void Log(industREAL.CAN.CORE.LogLevel level, string message,
                            System.Exception? ex = null, params object[] args)
                => _inner.Log((LogLevel)level, message, ex, args);
        }

        private static ILogSink? _sink;

        /// <summary>
        /// Assigning a sink here installs it on industREAL.CAN.CORE.Log, so the
        /// whole stack logs to one place. Reading it back returns whatever was
        /// last assigned through this property, or null if the CORE facade was
        /// configured directly.
        /// </summary>
        public static ILogSink? Sink
        {
            get => Volatile.Read(ref _sink);
            set
            {
                Volatile.Write(ref _sink, value);
                industREAL.CAN.CORE.Log.Sink = value is null ? null! : new SinkAdapter(value);
            }
        }

        public static void Trace(string msg, params object[] args) => industREAL.CAN.CORE.Log.Trace(msg, args);
        public static void Debug(string msg, params object[] args) => industREAL.CAN.CORE.Log.Debug(msg, args);
        public static void Info(string msg, params object[] args) => industREAL.CAN.CORE.Log.Info(msg, args);
        public static void Warn(string msg, params object[] args) => industREAL.CAN.CORE.Log.Warn(msg, args);
        public static void Error(string msg, params object[] args) => industREAL.CAN.CORE.Log.Error(msg, args);
        public static void Error(System.Exception ex, string msg, params object[] args) => industREAL.CAN.CORE.Log.Error(ex, msg, args);
        public static void Fatal(string msg, params object[] args) => industREAL.CAN.CORE.Log.Fatal(msg, args);
        public static void Fatal(System.Exception ex, string msg, params object[] args) => industREAL.CAN.CORE.Log.Fatal(ex, msg, args);
    }
}
