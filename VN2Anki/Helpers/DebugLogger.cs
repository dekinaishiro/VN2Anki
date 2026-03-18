using System;
using System.IO;

namespace VN2Anki
{
    public static class DebugLogger
    {
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hook_debug.log");
        private static readonly object _lock = new object();

        public static void Log(string message)
        {
            lock (_lock)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
    }
}