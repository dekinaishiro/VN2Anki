using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using VN2Anki.Models.Entities;

namespace VN2Anki.Services.Interfaces
{
    public class VnProcessEventArgs : EventArgs
    {
        public VisualNovel VisualNovel { get; set; }
        public Process? Process { get; set; }
        public int ProcessId { get; set; }
    }

    public class ActiveWindowItem
    {
        public string Title { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public string? ExecutablePath { get; set; }
        public int ProcessId { get; set; }
        public string DisplayName => string.IsNullOrWhiteSpace(Title) ? ProcessName : $"{Title} ({ProcessName}.exe)";
    }

    public interface IProcessMonitoringService : IDisposable
    {
        event EventHandler<VnProcessEventArgs> VnProcessStarted;
        event EventHandler<VnProcessEventArgs> VnProcessStopped;

        void StartMonitoring();
        void StopMonitoring();

        Task<List<(VisualNovel Vn, Process Process)>> GetRunningVnsAsync();     
        bool IsAnyInstanceRunning(int vnId);

        List<ActiveWindowItem> GetActiveWindows();
        bool IsProcessRunning(string processName);
    }
}
