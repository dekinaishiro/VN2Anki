using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class ProcessMonitoringService : IProcessMonitoringService
    {
        private readonly IVnDatabaseService _vnDatabaseService;
        private readonly ILogger<ProcessMonitoringService> _logger;
        
        private CancellationTokenSource? _cts;
        private Task? _pollingTask;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);
        
        // Keeps track of known active processes: PID -> ProcessName
        private Dictionary<int, string> _activeProcessIds = new Dictionary<int, string>();
        // Keeps track of VNs we have actively alerted as started: PID -> VisualNovel
        private Dictionary<int, VisualNovel> _activeVns = new Dictionary<int, VisualNovel>();

        public event EventHandler<VnProcessEventArgs>? VnProcessStarted;
        public event EventHandler<VnProcessEventArgs>? VnProcessStopped;

        public ProcessMonitoringService(IVnDatabaseService vnDatabaseService, ILogger<ProcessMonitoringService> logger)
        {
            _vnDatabaseService = vnDatabaseService;
            _logger = logger;
        }

        public void StartMonitoring()
        {
            if (_cts != null) return;

            _logger.LogInformation("Starting Process Monitoring (Polling mode)...");
            
            // Initial snapshot to avoid triggering 'Started' for everything already running
            RefreshActiveProcessesSnapshot();

            _cts = new CancellationTokenSource();
            _pollingTask = Task.Run(() => PollingLoopAsync(_cts.Token));
            
            _logger.LogInformation("Process monitoring started successfully.");
        }

        public void StopMonitoring()
        {
            if (_cts == null) return;

            _logger.LogInformation("Stopping process monitoring...");
            
            _cts.Cancel();
            try
            {
                _pollingTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException) { /* Ignored */ }
            
            _cts.Dispose();
            _cts = null;
            _pollingTask = null;
            
            _activeProcessIds.Clear();
            _activeVns.Clear();
            
            _logger.LogInformation("Process monitoring stopped.");
        }

        private void RefreshActiveProcessesSnapshot()
        {
            _activeProcessIds.Clear();
            try
            {
                var currentProcesses = Process.GetProcesses();
                foreach (var p in currentProcesses)
                {
                    if (!IsSystemProcess(p.ProcessName))
                    {
                        _activeProcessIds[p.Id] = p.ProcessName;
                    }
                }
            }
            catch { /* Ignore */ }
        }

        private async Task PollingLoopAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(_pollingInterval);
            
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    await CheckProcessesAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // Task was canceled, exit gracefully
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in polling loop.");
            }
        }

        private async Task CheckProcessesAsync()
        {
            try
            {
                var currentProcesses = Process.GetProcesses();
                var currentProcessDict = new Dictionary<int, Process>();
                
                foreach (var p in currentProcesses)
                {
                    if (!IsSystemProcess(p.ProcessName))
                    {
                        currentProcessDict[p.Id] = p;
                    }
                }

                var currentPids = currentProcessDict.Keys.ToHashSet();
                var oldPids = _activeProcessIds.Keys.ToHashSet();

                var startedPids = currentPids.Except(oldPids).ToList();
                var stoppedPids = oldPids.Except(currentPids).ToList();

                // Process STOPS
                foreach (var pid in stoppedPids)
                {
                    string processName = _activeProcessIds[pid];
                    _activeProcessIds.Remove(pid);

                    if (_activeVns.TryGetValue(pid, out var vn))
                    {
                        _activeVns.Remove(pid);
                        _logger.LogInformation($"Detected VN stop: {vn.Title} (PID: {pid})");
                        VnProcessStopped?.Invoke(this, new VnProcessEventArgs 
                        { 
                            VisualNovel = vn, 
                            Process = null, 
                            ProcessId = pid 
                        });
                    }
                }

                // Process STARTS
                if (startedPids.Any())
                {
                    var vns = await _vnDatabaseService.GetAllVisualNovelsAsync();

                    foreach (var pid in startedPids)
                    {
                        if (!currentProcessDict.TryGetValue(pid, out var process)) continue;

                        _activeProcessIds[pid] = process.ProcessName;

                        VisualNovel? matchingVn = null;
                        
                        // 1. Try match by ProcessName exactly
                        matchingVn = vns.FirstOrDefault(v => 
                            string.Equals(v.ProcessName, process.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(v.ProcessName, process.ProcessName + ".exe", StringComparison.OrdinalIgnoreCase));

                        // 2. Try match by ExecutablePath (needs process module info)
                        if (matchingVn == null)
                        {
                            try
                            {
                                string? executablePath = process.MainModule?.FileName;
                                if (!string.IsNullOrEmpty(executablePath))
                                {
                                    matchingVn = vns.FirstOrDefault(v => 
                                        string.Equals(v.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
                                }
                            }
                            catch
                            {
                                // Access denied or bitness mismatch, ignore gracefully
                            }
                        }

                        if (matchingVn != null)
                        {
                            _activeVns[pid] = matchingVn;
                            _logger.LogInformation($"Detected VN start: {matchingVn.Title} (PID: {pid})");
                            VnProcessStarted?.Invoke(this, new VnProcessEventArgs 
                            { 
                                VisualNovel = matchingVn, 
                                Process = process,
                                ProcessId = pid
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Error while checking processes.");
            }
        }

        public async Task<List<(VisualNovel Vn, Process Process)>> GetRunningVnsAsync()
        {
            var runningVns = new List<(VisualNovel, Process)>();
            
            try 
            {
                var vns = await _vnDatabaseService.GetAllVisualNovelsAsync();
                if (vns == null || !vns.Any()) return runningVns;

                var allProcesses = Process.GetProcesses();

                foreach (var process in allProcesses)
                {
                    if (IsSystemProcess(process.ProcessName)) continue;

                    try
                    {
                        var processNameWithExt = process.ProcessName + ".exe";
                        
                        var vn = vns.FirstOrDefault(v => 
                            string.Equals(v.ProcessName, processNameWithExt, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(v.ProcessName, process.ProcessName, StringComparison.OrdinalIgnoreCase));

                        if (vn == null)
                        {
                            try
                            {
                                var path = process.MainModule?.FileName;
                                if (!string.IsNullOrEmpty(path))
                                {
                                    vn = vns.FirstOrDefault(v => string.Equals(v.ExecutablePath, path, StringComparison.OrdinalIgnoreCase));
                                }
                            }
                            catch { /* Ignore access denied */ }
                        }

                        if (vn != null)
                        {
                            runningVns.Add((vn, process));
                        }
                    }
                    catch
                    {
                        // Process access denied or exited
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting running VNs.");
            }

            return runningVns;
        }

        private bool IsSystemProcess(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return true;

            string lowerName = processName.ToLowerInvariant();
            
            if (lowerName == "svchost" || lowerName == "conhost" || lowerName == "dllhost" || 
                lowerName == "cmd" || lowerName == "taskhostw" || lowerName == "explorer" || 
                lowerName == "csrss" || lowerName == "lsass" || lowerName == "winlogon" || 
                lowerName == "services" || lowerName == "smss" || lowerName == "idle" || 
                lowerName == "system" || lowerName == "registry" || lowerName == "fontdrvhost")
            {
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            StopMonitoring();
            GC.SuppressFinalize(this);
        }
    }
}
